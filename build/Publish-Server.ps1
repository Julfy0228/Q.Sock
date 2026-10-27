param(
    [string]$Dotnet = 'dotnet',
    [switch]$InProcessWasmTasks,
    [string]$OutputDirectory,
    [switch]$SkipBrowserBuild
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
if (-not $SkipBrowserBuild) {
    & (Join-Path $PSScriptRoot 'Build-Browser.ps1') -Dotnet $Dotnet -Configuration Release -Publish -InProcessWasmTasks:$InProcessWasmTasks
}
$destination = if ($OutputDirectory) { $OutputDirectory } else { Join-Path $repository 'artifacts/products/server' }
if (-not $OutputDirectory -and (Test-Path -LiteralPath $destination)) {
    $resolved = (Resolve-Path -LiteralPath $destination).Path
    $artifactRoot = (Resolve-Path -LiteralPath (Join-Path $repository 'artifacts')).Path
    if (-not $resolved.StartsWith($artifactRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Неожиданный каталог серверной поставки.'
    }
    $history = Join-Path $artifactRoot ('product-history/server-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path (Split-Path $history -Parent) -Force | Out-Null
    Move-Item -LiteralPath $resolved -Destination $history
}
& $Dotnet publish (Join-Path $repository 'Q.Sock.Server/Q.Sock.Server.csproj') -c Release -o $destination -m:1 -nr:false -p:MSBuildEnableWorkloadResolver=false
if ($LASTEXITCODE -ne 0) {
    throw 'Не удалось опубликовать сервер.'
}
Get-Item -LiteralPath $destination
