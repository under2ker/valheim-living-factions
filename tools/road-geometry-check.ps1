$ErrorActionPreference = 'Stop'
$gameDir = if ($env:VALHEIM_INSTALL) { $env:VALHEIM_INSTALL } else { 'C:/Program Files (x86)/Steam/steamapps/common/Valheim' }
$managed = Join-Path $gameDir 'valheim_Data/Managed'
$core = Join-Path $gameDir 'BepInEx/core'
$plugin = Join-Path $PSScriptRoot '../src/LivingFactions/bin/Release/LivingFactions.dll'
[void][System.Reflection.Assembly]::LoadFrom("$core/BepInEx.dll")
[void][System.Reflection.Assembly]::LoadFrom("$core/0Harmony.dll")
[void][System.Reflection.Assembly]::LoadFrom("$managed/UnityEngine.CoreModule.dll")
[void][System.Reflection.Assembly]::LoadFrom("$managed/UnityEngine.dll")
[void][System.Reflection.Assembly]::LoadFrom("$managed/assembly_utils.dll")
[void][System.Reflection.Assembly]::LoadFrom("$managed/assembly_valheim.dll")
$mod = [System.Reflection.Assembly]::LoadFrom($plugin)
$clear = $mod.GetType('LivingFactions.Roads', $true).GetMethod('Clear', [System.Reflection.BindingFlags]'NonPublic,Static')
$pads = [System.Collections.Generic.List[System.Collections.Generic.KeyValuePair[UnityEngine.Vector3,single]]]::new()
$pads.Add([System.Collections.Generic.KeyValuePair[UnityEngine.Vector3,single]]::new([UnityEngine.Vector3]::new(0,0,0), [single]2))
function Test-Segment($a, $b, [single]$room, [bool]$expected, [string]$label) {
    $actual = $clear.Invoke($null, [object[]]@($a, $b, $pads, $room))
    if ($actual -ne $expected) { throw "$label failed" }
    Write-Output "PASS $label"
}
Test-Segment ([UnityEngine.Vector3]::new(-5,0,0)) ([UnityEngine.Vector3]::new(5,0,0)) 2.3 $false 'crossing a building'
Test-Segment ([UnityEngine.Vector3]::new(-5,0,4)) ([UnityEngine.Vector3]::new(5,0,4)) 2.3 $false 'road width overlaps a building'
Test-Segment ([UnityEngine.Vector3]::new(-5,0,5)) ([UnityEngine.Vector3]::new(5,0,5)) 2.3 $true 'full road clears a building'
Test-Segment ([UnityEngine.Vector3]::new(-5,0,5)) ([UnityEngine.Vector3]::new(0,0,4)) 2.3 $false 'blocked endpoint'
Test-Segment ([UnityEngine.Vector3]::new(-5,0,3)) ([UnityEngine.Vector3]::new(3,0,-5)) 2.3 $false 'diagonal clips between clear endpoints'
Test-Segment ([UnityEngine.Vector3]::new(0,0,0)) ([UnityEngine.Vector3]::new(0,0,0)) 2.3 $false 'stationary point in building'
Test-Segment ([UnityEngine.Vector3]::new(5,0,5)) ([UnityEngine.Vector3]::new(5,0,5)) 2.3 $true 'stationary point outside building'

$bankHeight = $mod.GetType('LivingFactions.Roads', $true).GetMethod('TryPadBankHeight', [System.Reflection.BindingFlags]'NonPublic,Static')
$banks = [System.Collections.Generic.List[UnityEngine.Vector4]]::new()
$banks.Add([UnityEngine.Vector4]::new(-5,45,0,3))
$banks.Add([UnityEngine.Vector4]::new(5,45,0,3))
function Test-Bank([single]$x, [single]$ground, [single]$expected, [string]$label) {
    $point = [UnityEngine.Vector3]::new($x,0,0)
    $args = [object[]]@($point, $ground, $ground, $banks, $null)
    [void]$bankHeight.Invoke($null, $args)
    $actual = $args[4]
    if ([Math]::Abs($actual - $expected) -gt 0.001) { throw "$label failed: $actual != $expected" }
    Write-Output "PASS $label"
}
Test-Bank -4 44.95 44.95 'pad itself retains its ground'
Test-Bank 0 39 44.3 'deep trench between pads is filled'
Test-Bank 0 44.3 44.3 'finished street keeps its height on reload'
Test-Bank 35 40 40 'ground beyond the pads is untouched'
$banks[1] = [UnityEngine.Vector4]::new(5,41,0,3)
Test-Bank -1 39 44 'rise is shared between pads at different heights'
$banks.Clear()
$banks.Add([UnityEngine.Vector4]::new(0,107,0,5))
Test-Bank 6.3 111 107.455 'peak in the outer levelling margin is smoothed'
Test-Bank 6.1 102 106.615 'pit in the outer levelling margin is filled'
$banks[0] = [UnityEngine.Vector4]::new(0,44.55,0,5)
$slope = foreach ($sample in @(@(17.61,35.06), @(19.16,34.52))) {
    $point = [UnityEngine.Vector3]::new([single]$sample[0],0,0)
    $height = [single]$sample[1]
    $args = [object[]]@($point, $height, $height, $banks, $null)
    [void]$bankHeight.Invoke($null, $args)
    $args[4]
}
if ([Math]::Abs($slope[1] - $slope[0]) -gt 1.5) { throw 'pad shoulder creates a new bank across a gentle slope' }
Write-Output 'PASS pad shoulder fades back into the original slope'
