$ErrorActionPreference = 'Stop'
$gameDir = if ($env:VALHEIM_INSTALL) { $env:VALHEIM_INSTALL } else { 'C:/Program Files (x86)/Steam/steamapps/common/Valheim' }
$managed = Join-Path $gameDir 'valheim_Data/Managed'
$core = Join-Path $gameDir 'BepInEx/core'
$plugin = Join-Path $PSScriptRoot '../src/LivingFactions/bin/Release/LivingFactions.dll'
[void][System.Reflection.Assembly]::LoadFrom("$core/BepInEx.dll")
[void][System.Reflection.Assembly]::LoadFrom("$core/0Harmony.dll")
[void][System.Reflection.Assembly]::LoadFrom("$managed/UnityEngine.CoreModule.dll")
[void][System.Reflection.Assembly]::LoadFrom("$managed/UnityEngine.dll")
[void][System.Reflection.Assembly]::LoadFrom("$managed/assembly_valheim.dll")
$mod = [System.Reflection.Assembly]::LoadFrom($plugin)
$promote = $mod.GetType('LivingFactions.SimHost', $true).GetMethod('PromoteFile', [System.Reflection.BindingFlags]'NonPublic,Static')
$directory = Join-Path ([System.IO.Path]::GetTempPath()) ('lf-save-check-' + [guid]::NewGuid().ToString('N'))
[void][System.IO.Directory]::CreateDirectory($directory)
[string]$source = Join-Path $directory 'new.tmp'
[string]$target = Join-Path $directory 'save.json'
[string]$backup = Join-Path $directory 'save.json.bak'
try {
    [System.IO.File]::WriteAllText($source, 'first')
    $promote.Invoke($null, [object[]]@($source, $target, $null))
    if ([System.IO.File]::ReadAllText($target) -ne 'first') { throw 'initial save failed' }
    Write-Output 'PASS initial save'
    [System.IO.File]::WriteAllText($source, 'second')
    $promote.Invoke($null, [object[]]@($source, $target, $backup))
    if ([System.IO.File]::ReadAllText($target) -ne 'second' -or [System.IO.File]::ReadAllText($backup) -ne 'first') { throw 'replacement or backup failed' }
    Write-Output 'PASS replacement and backup'
    [System.IO.File]::WriteAllText($source, 'third')
    $promote.Invoke($null, [object[]]@($source, $target, $backup))
    if ([System.IO.File]::ReadAllText($target) -ne 'third' -or [System.IO.File]::ReadAllText($backup) -ne 'second') { throw 'existing backup replacement failed' }
    Write-Output 'PASS existing backup replacement'
    $failed = $false
    try { $promote.Invoke($null, [object[]]@($source, $target, $backup)) }
    catch { $failed = $true }
    if (-not $failed) { throw 'missing source accepted' }
    if ([System.IO.File]::ReadAllText($target) -ne 'third' -or [System.IO.File]::ReadAllText($backup) -ne 'second') { throw 'failed save changed existing data' }
    Write-Output 'PASS failed replacement preserves files'
    [System.IO.File]::WriteAllText($source, 'recovered')
    $promote.Invoke($null, [object[]]@($source, $target, $null))
    if ([System.IO.File]::ReadAllText($target) -ne 'recovered' -or [System.IO.File]::ReadAllText($backup) -ne 'second') { throw 'recovery replaced the existing backup' }
    Write-Output 'PASS recovery preserves backup'
} finally {
    foreach ($file in @($source, $target, $backup)) { if ([System.IO.File]::Exists($file)) { [System.IO.File]::Delete($file) } }
    [System.IO.Directory]::Delete($directory, $false)
}
