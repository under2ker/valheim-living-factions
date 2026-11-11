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
$watchType = $mod.GetType('LivingFactions.NightWatch+Watch', $true)
$watch = [Activator]::CreateInstance($watchType, $true)
$nightWatch = $mod.GetType('LivingFactions.NightWatch', $true)
$target = $nightWatch.GetMethod('Target', [System.Reflection.BindingFlags]'NonPublic,Static')
$isTarget = $nightWatch.GetMethod('IsTarget', [System.Reflection.BindingFlags]'NonPublic,Static')
function Target([long]$id, [single]$now, [single]$until) { $target.Invoke($null, [object[]]@($watch, $id, $now, $until)) }
function Assert-Target([long]$id, [single]$now, [bool]$expected, [string]$label) {
    if ($isTarget.Invoke($null, [object[]]@($watch, $id, $now)) -ne $expected) { throw "$label failed" }
    Write-Output "PASS $label"
}
Target 100 10 100
Target 200 20 120
Assert-Target 100 30 $true 'first player remains targeted'
Assert-Target 200 30 $true 'second player is targeted'
Assert-Target 300 30 $false 'uninvolved player is not targeted'
Assert-Target 100 100 $false 'expired target is cleared'
Assert-Target 200 100 $true 'other target remains active'
Target 200 50 80
Assert-Target 200 100 $true 'shorter alarm cannot shorten an existing one'
Target 0 50 200
Assert-Target 0 60 $false 'missing identity cannot be targeted'
