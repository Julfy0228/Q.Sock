param(
    [string]$Dotnet = 'dotnet',
    [string]$Configuration = 'Debug',
    [string]$KeyPath,
    [string]$Version = '1.0.3',
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
if (-not $KeyPath) {
    $KeyPath = Join-Path $repository 'artifacts/publisher/moodle.pem'
}
& $Dotnet build (Join-Path $repository 'Q.Sock.Plugins.Moodle/Q.Sock.Plugins.Moodle.csproj') -c $Configuration -m:1 -nr:false -p:MSBuildEnableWorkloadResolver=false
if ($LASTEXITCODE -ne 0) {
    throw 'Не удалось собрать Moodle-модуль.'
}
& $Dotnet build (Join-Path $repository 'Tools/ModulePack/ModulePack.csproj') -c $Configuration -m:1 -nr:false -p:MSBuildEnableWorkloadResolver=false
if ($LASTEXITCODE -ne 0) {
    throw 'Не удалось собрать упаковщик модулей.'
}
$assembly = Join-Path $repository "Q.Sock.Plugins.Moodle/bin/$Configuration/net10.0/Q.Sock.Plugins.Moodle.dll"
$packer = Join-Path $repository "Tools/ModulePack/bin/$Configuration/net10.0/Q.Sock.Tools.ModulePack.dll"
$output = if ($OutputDirectory) { $OutputDirectory } else { Join-Path $repository 'artifacts/plugins/client' }
& $Dotnet $packer $assembly $output $KeyPath moodle $Version 'Планирование занятий Moodle' 'Q.Sock.Plugins.Moodle.MoodleClientPlugin' lesson-planning
if ($LASTEXITCODE -ne 0) {
    throw 'Не удалось подписать модуль.'
}
