$ErrorActionPreference = 'Stop'
$gameDir = if ($env:VALHEIM_INSTALL) { $env:VALHEIM_INSTALL } else { 'C:/Program Files (x86)/Steam/steamapps/common/Valheim' }
$managed = Join-Path $gameDir 'valheim_Data/Managed'
$plugin = Join-Path $PSScriptRoot '../src/LivingFactions/bin/Release/LivingFactions.dll'
$core = Join-Path $gameDir 'BepInEx/core'
[void][System.Reflection.Assembly]::LoadFrom("$core/BepInEx.dll")
[void][System.Reflection.Assembly]::LoadFrom("$core/0Harmony.dll")
[void][System.Reflection.Assembly]::LoadFrom("$managed/UnityEngine.CoreModule.dll")
[void][System.Reflection.Assembly]::LoadFrom("$managed/UnityEngine.dll")
$game = [System.Reflection.Assembly]::LoadFrom("$managed/assembly_valheim.dll")
$mod = [System.Reflection.Assembly]::LoadFrom($plugin)
$packageType = $game.GetType('ZPackage', $true)
$netType = $mod.GetType('LivingFactions.Net', $true)
$readCall = $netType.GetMethod('ReadCall', [System.Reflection.BindingFlags]'NonPublic,Static')
$readReport = $netType.GetMethod('ReadReport', [System.Reflection.BindingFlags]'NonPublic,Static')
$readAlarm = $netType.GetMethod('ReadAlarm', [System.Reflection.BindingFlags]'NonPublic,Static')
$alarmIdentity = $netType.GetMethod('AlarmIdentityMatches', [System.Reflection.BindingFlags]'NonPublic,Static')
$playerArgument = $netType.GetMethod('DirectPlayerArgument', [System.Reflection.BindingFlags]'NonPublic,Static')
foreach ($entry in @{ war=1; peace=1; ally=1; unally=1; gift=1; buy=3; sell=3; found=0; killed=-1; stock=-1 }.GetEnumerator()) {
    $actual = $playerArgument.Invoke($null, [object[]]@($entry.Key))
    if ($actual -ne $entry.Value) { throw "incorrect player field for $($entry.Key)" }
}
Write-Output 'PASS player action identity fields'
function Make-Package([byte[]]$bytes) { return [Activator]::CreateInstance($packageType, [object[]]@(,$bytes)) }
function Test-Packet([byte[]]$bytes, [bool]$expected, [string]$label) {
    $callArgs = [object[]]@((Make-Package $bytes), $null, $null)
    $actual = $readCall.Invoke($null, $callArgs)
    if ($actual -ne $expected) { throw "$label failed" }
    Write-Output "PASS $label"
}
$stream = [System.IO.MemoryStream]::new()
$writer = [System.IO.BinaryWriter]::new($stream)
$writer.Write('rep'); $writer.Write([int]3); $writer.Write([byte]0); $writer.Write([int]1); $writer.Write([byte]1); $writer.Write([long]5); $writer.Write([byte]2); $writer.Write([single]2)
Test-Packet $stream.ToArray() $true 'valid action'
$valid = $stream.ToArray()
Test-Packet $valid[0..($valid.Length-2)] $false 'truncated action'
$stream.SetLength(0); $stream.Position = 0
$writer.Write('rep'); $writer.Write([int]1); $writer.Write([byte]2); $writer.Write([single]::NaN)
Test-Packet $stream.ToArray() $false 'non-finite number'
$stream.SetLength(0); $stream.Position = 0
$writer.Write('rep'); $writer.Write([int]1); $writer.Write([byte]255)
Test-Packet $stream.ToArray() $false 'unknown argument type'
Test-Packet ([byte[]]::new(65537)) $false 'oversized action'
$stream.SetLength(0); $stream.Position = 0
$writer.Write('rep'); $writer.Write([int]17)
Test-Packet $stream.ToArray() $false 'too many action arguments'
$stream.SetLength(0); $stream.Position = 0
$writer.Write('name'); $writer.Write([int]1); $writer.Write([byte]4); $writer.Write(('x' * 1025))
Test-Packet $stream.ToArray() $false 'oversized action text'
function Test-Report([byte[]]$bytes, [bool]$expected, [string]$label) {
    $callArgs = [object[]]@((Make-Package $bytes), $null)
    $actual = $readReport.Invoke($null, $callArgs)
    if ($actual -ne $expected) { throw "$label failed" }
    if (-not $expected -and $null -ne $callArgs[1]) { throw "$label returned a partial report" }
    Write-Output "PASS $label"
}
function Frame-Report([byte[]]$body, [int]$count) {
    $frame = [System.IO.MemoryStream]::new()
    $framedWriter = [System.IO.BinaryWriter]::new($frame)
    $framedWriter.Write($count); $framedWriter.Write([int]$body.Length); $framedWriter.Write($body)
    return ,$frame.ToArray()
}
$stream.SetLength(0); $stream.Position = 0
$writer.Write([int]1); $writer.Write([int]2); $writer.Write([int]3); $writer.Write([single]4)
$writer.Write([int]5); $writer.Write([int]6); $writer.Write('wall')
$body = $stream.ToArray()
Test-Report (Frame-Report $body 1) $true 'valid building report'
Test-Report (Frame-Report $body 2) $false 'truncated building report'
$frame = [System.IO.MemoryStream]::new(); $frameWriter = [System.IO.BinaryWriter]::new($frame)
$frameWriter.Write([int]1); $frameWriter.Write([int]::MaxValue)
Test-Report $frame.ToArray() $false 'forged building report length'
$frame = [System.IO.MemoryStream]::new(); $frameWriter = [System.IO.BinaryWriter]::new($frame)
$frameWriter.Write([int]4097)
Test-Report $frame.ToArray() $false 'too many building updates'
$stream.SetLength(0); $stream.Position = 0
$writer.Write([int]1); $writer.Write([int]2); $writer.Write([int]3); $writer.Write([single]::PositiveInfinity)
$writer.Write([int]5); $writer.Write([int]6); $writer.Write('wall')
Test-Report (Frame-Report $stream.ToArray() 1) $false 'infinite terrain height'
$stream.SetLength(0); $stream.Position = 0
$writer.Write([int]1); $writer.Write([int]2); $writer.Write([int]3); $writer.Write([single]::NaN)
$writer.Write([int]5); $writer.Write([int]6); $writer.Write('wall')
Test-Report (Frame-Report $stream.ToArray() 1) $false 'NaN terrain height'
$stream.SetLength(0); $stream.Position = 0
$writer.Write([int]1); $writer.Write([int]3); $writer.Write([int]0); $writer.Write([single]::NaN)
$writer.Write([int]0); $writer.Write([int]6); $writer.Write('')
$writer.Write($body)
Test-Report (Frame-Report $stream.ToArray() 2) $true 'pending ground alongside a placed building'
$stream.SetLength(0); $stream.Position = 0
$writer.Write([int]1); $writer.Write([int]2); $writer.Write([int]1000001); $writer.Write([single]4)
$writer.Write([int]5); $writer.Write([int]6); $writer.Write('wall')
Test-Report (Frame-Report $stream.ToArray() 1) $false 'excessive building piece count'
$stream.SetLength(0); $stream.Position = 0
$writer.Write([int]1); $writer.Write([int]2); $writer.Write([int]3); $writer.Write([single]4)
$writer.Write([int]5); $writer.Write([int]6)
$wall = 'stoneC2:' + ((0..71 | ForEach-Object { "$(5 * $_).0" }) -join ',')
$writer.Write($wall)
Test-Report (Frame-Report $stream.ToArray() 1) $true 'city wall with many gates'
$stream.SetLength(0); $stream.Position = 0
$writer.Write([int]1); $writer.Write([int]2); $writer.Write([int]3); $writer.Write([single]4)
$writer.Write([int]5); $writer.Write([int]6); $writer.Write(('x' * 513))
Test-Report (Frame-Report $stream.ToArray() 1) $false 'excessive wall description'
function Test-Alarm([byte[]]$bytes, [bool]$expected, [string]$label) {
    $callArgs = [object[]]@((Make-Package $bytes), $null, $null, $null)
    if ($readAlarm.Invoke($null, $callArgs) -ne $expected) { throw "$label failed" }
    Write-Output "PASS $label"
}
$stream.SetLength(0); $stream.Position = 0
$writer.Write([int]1); $writer.Write([long]123); $writer.Write([uint32]456)
$writer.Write([single]10); $writer.Write([single]20); $writer.Write([single]30)
$validAlarm = $stream.ToArray()
Test-Alarm $validAlarm $true 'valid theft alarm'
Test-Alarm $validAlarm[0..($validAlarm.Length-2)] $false 'truncated theft alarm'
Test-Alarm ([byte[]]($validAlarm + [byte]0)) $false 'trailing theft alarm data'
Test-Alarm ([byte[]]::new(65)) $false 'oversized theft alarm'
$stream.SetLength(0); $stream.Position = 0
$writer.Write([int]0); $writer.Write([long]123); $writer.Write([uint32]456)
$writer.Write([single]10); $writer.Write([single]20); $writer.Write([single]30)
Test-Alarm $stream.ToArray() $false 'unknown settlement in theft alarm'
$stream.SetLength(0); $stream.Position = 0
$writer.Write([int]1); $writer.Write([long]123); $writer.Write([uint32]456)
$writer.Write([single]::NaN); $writer.Write([single]20); $writer.Write([single]30)
Test-Alarm $stream.ToArray() $false 'non-finite theft alarm coordinates'
foreach ($case in @(
    @{ Args = [object[]]@([long]11, [long]11, [long]21, [long]21); Expected = $true; Label = 'theft alarm sender matches the thief' },
    @{ Args = [object[]]@([long]11, [long]12, [long]21, [long]21); Expected = $false; Label = 'theft alarm sender has no matching peer' },
    @{ Args = [object[]]@([long]11, [long]11, [long]22, [long]21); Expected = $false; Label = 'theft alarm names another player' },
    @{ Args = [object[]]@([long]11, [long]11, [long]0, [long]0); Expected = $false; Label = 'theft alarm has no player identity' }
)) {
    $actual = $alarmIdentity.Invoke($null, $case.Args)
    if ($actual -ne $case.Expected) { throw "$($case.Label) failed" }
    Write-Output "PASS $($case.Label)"
}
