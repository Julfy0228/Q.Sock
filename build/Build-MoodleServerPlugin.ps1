param(
    [string]$Dotnet = 'dotnet',
    [string]$Configuration = 'Release',
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $repository 'artifacts/plugins/server/moodle'
}
& $Dotnet build (Join-Path $repository 'Q.Sock.Server.Plugins.Moodle/Q.Sock.Server.Plugins.Moodle.csproj') -c $Configuration -m:1 -nr:false -p:MSBuildEnableWorkloadResolver=false
if ($LASTEXITCODE -ne 0) {
    throw 'Не удалось собрать серверный модуль Moodle.'
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
foreach ($name in @('Q.Sock.Server.Plugins.Moodle.dll', 'Q.Sock.Server.Plugins.Moodle.deps.json')) {
    Copy-Item -LiteralPath (Join-Path $repository "Q.Sock.Server.Plugins.Moodle/bin/$Configuration/net10.0/$name") -Destination $OutputDirectory -Force
}
@{
    id = 'moodle'
    assembly = 'Q.Sock.Server.Plugins.Moodle.dll'
    entryType = 'Q.Sock.Server.Plugins.Moodle.MoodleModule'
    apiVersion = 1
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'module.json') -Encoding utf8NoBOM
