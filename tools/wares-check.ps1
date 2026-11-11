$ErrorActionPreference = 'Stop'
$gameDir = if ($env:VALHEIM_INSTALL) { $env:VALHEIM_INSTALL } else { 'C:/Program Files (x86)/Steam/steamapps/common/Valheim' }
$managed = Join-Path $gameDir 'valheim_Data/Managed'
$core = Join-Path $gameDir 'BepInEx/core'
[void][System.Reflection.Assembly]::LoadFrom("$core/BepInEx.dll")
[void][System.Reflection.Assembly]::LoadFrom("$core/0Harmony.dll")
[void][System.Reflection.Assembly]::LoadFrom("$managed/UnityEngine.CoreModule.dll")
[void][System.Reflection.Assembly]::LoadFrom("$managed/UnityEngine.dll")
[void][System.Reflection.Assembly]::LoadFrom("$managed/assembly_valheim.dll")
$mod = [System.Reflection.Assembly]::LoadFrom((Join-Path $PSScriptRoot '../src/LivingFactions/bin/Release/LivingFactions.dll'))
$parse = $mod.GetType('LivingFactions.Wares', $true).GetMethod('ParseOffer', [System.Reflection.BindingFlags]'NonPublic,Static')
function Assert-Offer([string]$line, [bool]$valid, [string]$label) {
    $result = $parse.Invoke($null, [object[]]@($line))
    if (($null -ne $result) -ne $valid) { throw "$label failed" }
    Write-Output "PASS $label"
}
Assert-Offer '* sell Wood 5 10 0' $true 'ordinary offer'
Assert-Offer '* mystery Wood 1 0 -10' $true 'free offer and negative reputation'
Assert-Offer '* relic Wood 1 10 0 keen' $true 'relic with affix'
Assert-Offer '* relic Wood 2 10 0 keen' $false 'relics must be sold individually'
Assert-Offer '* mystery Wood 2 10 0' $false 'mystery items must be sold individually'
Assert-Offer '* sell Wood 0 10 0' $false 'zero amount'
Assert-Offer '* sell Wood -1 10 0' $false 'negative amount'
Assert-Offer '* sell Wood 1 -10 0' $false 'negative price'
Assert-Offer '* unknown Wood 1 10 0' $false 'unknown offer kind'
Assert-Offer '* sell Wood broken 10 0' $false 'invalid amount'
Assert-Offer '* sell Wood 1' $false 'truncated offer'
Assert-Offer '* sell Stone 1 10 0' $true 'valid offer after invalid rows'
