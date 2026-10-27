param(
    [string]$Configuration = 'Debug',
    [string]$Dotnet = 'dotnet',
    [switch]$InProcessWasmTasks,
    [switch]$Publish
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repository 'Q.Sock.Browser/Q.Sock.Browser.csproj'
function Move-GeneratedOutputToHistory([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }
    $resolved = (Resolve-Path -LiteralPath $Path).Path
    $root = (Resolve-Path -LiteralPath $repository).Path
    if (-not $resolved.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Каталог браузерной сборки находится вне репозитория.'
    }
    $history = Join-Path $repository ('artifacts/browser-history/' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path (Split-Path $history -Parent) -Force | Out-Null
    Move-Item -LiteralPath $resolved -Destination $history
}
if ($Publish) {
    Move-GeneratedOutputToHistory (Join-Path $repository "Q.Sock.Browser/bin/$Configuration/net10.0-browser/publish/wwwroot")
    Move-GeneratedOutputToHistory (Join-Path $repository "Q.Sock.Browser/bin/$Configuration/net10.0-browser/wwwroot")
}
$verb = if ($Publish) { 'publish' } else { 'build' }
$arguments = @($verb, $project, '-c', $Configuration, '-m:1', '-nr:false')
if ($InProcessWasmTasks) {
    $arguments += '-p:QsockUseInProcessWasmTasks=true'
}
& $Dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw 'Не удалось собрать браузерный клиент.'
}

$source = Join-Path $repository "Q.Sock.Browser/bin/$Configuration/net10.0-browser/wwwroot"
if ($Publish) {
    $source = Join-Path $repository "Q.Sock.Browser/bin/$Configuration/net10.0-browser/publish/wwwroot"
}
if (-not (Test-Path -LiteralPath (Join-Path $source 'index.html'))) {
    throw "Не найден браузерный пакет: $source"
}
$destination = Join-Path $repository 'Q.Sock.Server/wwwroot/avalonia'
Move-GeneratedOutputToHistory $destination
New-Item -ItemType Directory -Path $destination -Force | Out-Null
Copy-Item -Path (Join-Path $source '*') -Destination $destination -Recurse -Force
