param(
    [string]$Dotnet = 'dotnet',
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $repository 'artifacts/products/client'
}
& $Dotnet publish (Join-Path $repository 'Q.Sock.Client/Q.Sock.Client.csproj') -c Release -o $OutputDirectory -m:1 -nr:false -p:MSBuildEnableWorkloadResolver=false
if ($LASTEXITCODE -ne 0) {
    throw 'Не удалось собрать клиент.'
}
Get-Item -LiteralPath $OutputDirectory
