param(
    [string]$Dotnet = 'dotnet',
    [string]$Runtime = 'linux-x64',
    [string]$OutputDirectory,
    [switch]$SkipBrowserBuild,
    [switch]$InProcessWasmTasks
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
if ($Runtime -notin @('linux-x64', 'linux-arm64', 'win-x64')) {
    throw 'Поддерживаются linux-x64, linux-arm64 и win-x64.'
}
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $repository 'artifacts/products/moodle'
}
$stage = Join-Path $repository ('artifacts/package-staging/' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage -Force | Out-Null
foreach ($type in @('local', 'mod')) {
    $destination = Join-Path $stage $type
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repository "integrations/moodle/$type/qsock") -Destination $destination -Recurse
}
if (-not $SkipBrowserBuild) {
    & (Join-Path $PSScriptRoot 'Build-Browser.ps1') -Dotnet $Dotnet -Configuration Release -Publish -InProcessWasmTasks:$InProcessWasmTasks
}
$server = Join-Path $stage 'local/qsock/server'
& $Dotnet publish (Join-Path $repository 'Q.Sock.Server/Q.Sock.Server.csproj') -c Release -r $Runtime --self-contained true -o $server -m:1 -nr:false -p:MSBuildEnableWorkloadResolver=false
if ($LASTEXITCODE -ne 0) {
    throw 'Не удалось собрать встроенный сервер.'
}
& (Join-Path $PSScriptRoot 'Build-MoodleServerPlugin.ps1') -Dotnet $Dotnet -OutputDirectory (Join-Path $server 'server-plugins/moodle')
& (Join-Path $PSScriptRoot 'Build-MoodlePlugin.ps1') -Dotnet $Dotnet -Configuration Release -OutputDirectory (Join-Path $server 'client-modules')
Set-Content -LiteralPath (Join-Path $server '.htaccess') -Value 'Require all denied' -Encoding ascii
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
foreach ($type in @('local', 'mod')) {
    $destination = Join-Path $OutputDirectory "$($type)_qsock_$Runtime.zip"
    Compress-Archive -LiteralPath (Join-Path $stage "$type/qsock") -DestinationPath $destination -Force
    Get-Item -LiteralPath $destination
}
