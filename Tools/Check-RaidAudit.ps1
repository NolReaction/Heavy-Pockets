[CmdletBinding()]
param([Parameter(Mandatory)][string]$RunDirectory)

$ErrorActionPreference = 'Stop'
$checks = [Collections.Generic.List[string]]::new()

function Require([bool]$ok, [string]$failure) {
    if (-not $ok) { throw "Wire acceptance failed: $failure" }
}
function Mark([string]$message) {
    $checks.Add($message)
    Write-Host "PASS $message"
}
function Read-Marker([string]$name) {
    return [IO.File]::ReadAllText((Join-Path $RunDirectory $name)).Trim()
}
# Independent description of the published wire grid and type footprints.
$widths = @(0,2,2,1,2,1,1,1,2,1,1,2,3,2,2,2,2,2,2,2,2,2,1,1)
$heights = @(0,2,1,1,1,1,1,2,1,2,2,1,1,1,3,2,1,1,1,1,2,1,1,1)
function Read-Audit([string]$name) {
    $path = Join-Path $RunDirectory "$name.jsonl"
    # Server still owns its open stream. Always read with shared access.
    $stream = [IO.File]::Open($path, 'Open', 'Read', 'ReadWrite')
    $reader = [IO.StreamReader]::new($stream)
    $prior = 0L
    $currentRound = @{}
    try {
        while ($null -ne ($line = $reader.ReadLine())) {
            $row = $line | ConvertFrom-Json
            Require ([long]$row.sequence -eq $prior + 1) "$name trace sequence gap"
            $prior = [long]$row.sequence
            [byte[]]$bytes = [Convert]::FromBase64String($row.payload)
            Require ($bytes.Length -gt 0) "$name empty packet"
            $code = $bytes[0]
            $round = 0L
            $queued = $null
            $doorOpen = $null
            $mechanism = $null
            $scaleKilograms = $null
            $streamId = [long]$row.peer
            $expectedDirection = if (($name -eq 'server') -eq ($row.channel -eq 'event')) { 'tx' } else { 'rx' }
            Require ($row.direction -eq $expectedDirection) "$name invalid message direction"
            $items = @()
            $types = @()
            $positions = @()
            $shapes = @()
            $flags = $targetX = $targetY = $originalX = $originalY = 0
            $complete = $false
            $instance = 0L
            $operation = 0L
            $pitch = $yaw = $mode = $null
            $worldX = $worldZ = $null
            $height = $crouch = $sprint = $slide = $null
            $layout = $null
            $remainingMs = 0
            $gearIds = @()
            $gearTypes = @()
            $pouchLevel = 0
            $active = $sourceSlot = $destSlot = $phase = $null
            $status = 0
            $actor = 0L
            if ($row.channel -eq 'event') {
                if ($code -in 3,4,5) {
                    Require ($bytes.Length -ge 3) "$name inventory header missing"
                    $hidden = [int]$bytes[1]
                    $count = [int]$bytes[2]
                    $isCrate = $code -eq 3
                    $columns = 6
                    $rows = if ($isCrate) { 6 } else { 9 }
                    $layoutSize = 3 + 8*$count + 4*$hidden
                    $timerSize = if ($isCrate -and $hidden -gt 0) { 2 } else { 0 }
                    $capacity = $columns * $rows
                    if ($code -eq 5) { $capacity += 9 }
                    Require ($bytes.Length -eq ($layoutSize + $timerSize) -and $count+$hidden -le $capacity) "$name extra or missing inventory data"
                    Require ($isCrate -or $hidden -eq 0) "$name anonymous geometry in a private bag or receipt"
                    $layout = [Convert]::ToBase64String($bytes,0,$layoutSize)
                    if ($timerSize) {
                        $remainingMs = [BitConverter]::ToUInt16($bytes,$layoutSize)
                        Require ($remainingMs -ge 1 -and $remainingMs -le 750) "$name invalid remaining time for this item"
                    }
                    $complete = $hidden -eq 0
                    $occupied = @{}
                    for ($i=0; $i -lt $count; $i++) {
                        $offset = 3 + 8*$i
                        $uid = [BitConverter]::ToUInt32($bytes,$offset)
                        $type = [int]$bytes[$offset + 4]
                        $x = [int]$bytes[$offset + 5]
                        $y = [int]$bytes[$offset + 6]
                        $rot = [int]$bytes[$offset + 7]
                        Require ($uid -ne 0 -and $uid -notin $items -and $type -ge 1 -and $type -lt $widths.Count -and $rot -in 0,1) "$name invalid item"
                        $w = if ($rot) { $heights[$type] } else { $widths[$type] }
                        $h = if ($rot) { $widths[$type] } else { $heights[$type] }
                        Require ($x + $w -le $columns -and $y + $h -le $rows) "$name item crosses grid edge"
                        for ($rowIndex=$y; $rowIndex -lt $y+$h; $rowIndex++) {
                            for ($col=$x; $col -lt $x+$w; $col++) {
                                $cell = $rowIndex*$columns+$col
                                if ($code -ne 5) { Require (!$occupied.ContainsKey($cell)) "$name overlapping items" }
                                $occupied[$cell] = $uid
                            }
                        }
                        $items += $uid
                        $types += $type
                        $positions += "$x/$y/$rot"
                    }
                    $lastAnchor = -1
                    for ($i=0; $i -lt $hidden; $i++) {
                        $offset = 3 + 8*$count + 4*$i
                        $x,$y,$w,$h = $bytes[$offset..($offset+3)]
                        Require ($w -in 1,2,3 -and $h -in 1,2,3 -and $x+$w -le $columns -and $y+$h -le $rows) "$name unknown footprint outside the grid"
                        $anchor = $y*$columns+$x
                        Require ($anchor -gt $lastAnchor) "$name unstable footprint order"
                        $lastAnchor = $anchor
                        for ($r=$y; $r -lt $y+$h; $r++) {
                            for ($c=$x; $c -lt $x+$w; $c++) {
                                $cell = $r*$columns+$c
                                Require (!$occupied.ContainsKey($cell)) "$name overlapping anonymous footprint"
                                $occupied[$cell] = 'unknown'
                            }
                        }
                        $shapes += "$x/$y/$w/$h"
                    }
                } elseif ($code -eq 10) {
                    Require ($bytes.Length -eq 47) "$name unexpected equipment snapshot"
                    $active = [int]$bytes[1]
                    $gearIds = @(0L)
                    $gearTypes = @(0)
                    for ($slot=1; $slot -le 9; $slot++) {
                        $offset = 2 + 5*($slot-1)
                        $uid = [long][BitConverter]::ToUInt32($bytes,$offset)
                        $type = [int]$bytes[$offset + 4]
                        Require (($uid -eq 0) -eq ($type -eq 0)) "$name invalid equipment record"
                        if ($uid -ne 0) {
                            Require ($uid -notin $gearIds) "$name duplicate equipment instance"
                            if ($slot -in 1,2) { Require ($type -in 12,13) "$name incompatible weapon" }
                            elseif ($slot -eq 3) { Require ($type -eq 14) "$name incompatible armor" }
                            elseif ($slot -eq 4) { Require ($type -in 15,20) "$name incompatible backpack" }
                            elseif ($slot -eq 5) { Require ($type -in 16..19) "$name incompatible pouch" }
                            else {
                                $cargo = $type -in 1..11 -or ($type -ge 21 -and $type -lt $widths.Count)
                                $fits = $cargo -and ($widths[$type] * $heights[$type] -le 2)
                                Require ($fits -and $slot-5 -le $gearTypes[5]-15) "$name incompatible or locked pocket"
                            }
                        }
                        $gearIds += $uid
                        $gearTypes += $type
                    }
                    Require ($active -in 0,1,2,6,7 -and ($active -eq 0 -or $gearIds[$active] -gt 0)) "$name invalid active slot"
                } elseif ($code -eq 11) {
                    Require ($bytes.Length -eq 2 -and $bytes[1] -in 1..4) "$name invalid base loadout event"
                    $pouchLevel = [int]$bytes[1]
                } elseif ($code -eq 12) {
                    Require ($bytes.Length -eq 12 -and $bytes[1] -in 0,1,2,3) "$name invalid passage snapshot"
                    $mechanism = [int]$bytes[1]
                    $minimum = if ($mechanism % 2 -eq 0) { 0 } else { 1 }
                    $startedAt = [BitConverter]::ToDouble($bytes,4)
                    Require ($bytes[2] -in $minimum..2 -and $bytes[3] -in $minimum..2 -and
                        ($bytes[2] -eq 1 -or $bytes[3] -eq 1) -and $startedAt -ge 0 -and
                        ![double]::IsInfinity($startedAt)) "$name invalid passage movement"
                    $doorOpen = $bytes[3] -ne 1
                } elseif ($code -eq 15) {
                    $when = if ($bytes.Length -eq 10) { [BitConverter]::ToDouble($bytes,2) } else { -1 }
                    Require ($when -ge 0 -and ![double]::IsInfinity($when) -and $bytes[1] -in 0..2) "$name invalid public container animation"
                } elseif ($code -eq 13) {
                    Require ($bytes.Length -eq 2 -and $bytes[1] -in 0,75,150,200) "$name invalid scale reading"
                    $scaleKilograms = [int]$bytes[1]
                } else {
                    $length = @{ 1 = 31; 2 = 9; 6 = 6; 7 = 5; 8 = 6; 9 = 6; 21 = 4 }[[int]$code]
                    if ($code -eq 21) { Require ($bytes.Length -eq 4 -and $bytes[2] -in 1..4 -and $bytes[1] -le $bytes[2] -and $bytes[3] -in 0,1) "$name invalid map vote count" }
                    Require ($null -ne $length -and $bytes.Length -eq $length) "$name unexpected public event fields"
                    if ($code -in 8,9) {
                        $round = [long][BitConverter]::ToUInt32($bytes,1)
                        Require ($round -gt 0) "$name invalid round number"
                        if ($code -eq 8) {
                            Require ($bytes[5] -in 0,1) "$name invalid raid map"
                            Require ($round -eq 1 + [long]$currentRound[$streamId]) "$name skipped or repeated a raid start"
                            $currentRound[$streamId] = $round
                        } else {
                            Require ($bytes[5] -in 0,1) "$name invalid queue state"
                            $queued = [int]$bytes[5]
                        }
                    }
                    if ($code -eq 6) { $status = $bytes[1]; $operation = [BitConverter]::ToUInt32($bytes,2) }
                    if ($code -in 1,2) { $actor = [BitConverter]::ToUInt64($bytes,1) }
                    if ($code -eq 1) {
                        $worldX = [BitConverter]::ToSingle($bytes,9)
                        $height = [BitConverter]::ToSingle($bytes,21)
                        $crouch = [int]$bytes[29]
                        $sprint = [int](($bytes[30] -band 1) -ne 0)
                        $slide = [int](($bytes[30] -band 2) -ne 0)
                        $speedY = [BitConverter]::ToSingle($bytes,25)
                        Require ($height -ge 0 -and $height -le 64 -and $bytes[30] -in 0,1,2,4,5 -and
                            [Math]::Abs($speedY) -le 30 -and (($bytes[30] -band 4) -or $speedY -eq 0)) "$name invalid locomotion"
                        $worldZ = [BitConverter]::ToSingle($bytes,13)
                    }
                }
            } else {
                Require ($row.channel -eq 'command') "$name unknown channel"
                if ($code -in 9,10) {
                    $length = if ($code -eq 9) { 14 } else { 6 }
                    Require ($bytes.Length -eq $length) "$name invalid equipment command"
                    $operation = [BitConverter]::ToUInt32($bytes,1)
                    Require ($operation -gt 0) "$name zero equipment operation"
                    if ($code -eq 9) {
                        $instance = [BitConverter]::ToUInt32($bytes,5)
                        $sourceSlot = [int]$bytes[9]
                        $destSlot = [int]$bytes[10]
                        Require ($instance -gt 0 -and $sourceSlot -le 10 -and $destSlot -le 10 -and $bytes[13] -le 1) "$name invalid equipment move"
                    } else { $destSlot = [int]$bytes[5] }
                } elseif ($code -eq 11) {
                    Require ($bytes.Length -eq 2 -and $bytes[1] -in 1..4) "$name invalid base loadout choice"
                    $pouchLevel = [int]$bytes[1]
                } elseif ($code -eq 12) {
                    Require ($bytes.Length -eq 1) "$name invalid base arrival"
                } elseif ($code -in 4,5,6) {
                    $length = if ($code -eq 6) { 14 } else { 9 }
                    Require ($bytes.Length -eq $length) "$name invalid transfer command length"
                    if ($code -eq 6) {
                        $flags = [int]$bytes[9]
                        Require ($flags -lt 16) "$name unexpected placement flags"
                        $targetX = $bytes[10]; $targetY = $bytes[11]; $originalX = $bytes[12]; $originalY = $bytes[13]
                    }
                    $operation = [BitConverter]::ToUInt32($bytes,1)
                    $instance = [BitConverter]::ToUInt32($bytes,5)
                    Require ($operation -gt 0 -and $instance -gt 0) "$name invalid transfer identifiers"
                } else {
                    if ($code -eq 1) {
                        Require ($bytes.Length -eq 24 -and $bytes[22] -lt 255 -and $bytes[23] -le 7) "$name invalid movement payload or shoulder phase"
                        $phase = [int]$bytes[22]
                        $yaw = [BitConverter]::ToSingle($bytes,9); $pitch = [BitConverter]::ToSingle($bytes,13); $mode = $bytes[17]
                        Require ($pitch -ge -82 -and $pitch -le 82 -and $mode -in 0,1,2) "$name invalid camera parameters"
                        $aspect = [BitConverter]::ToSingle($bytes,18)
                        Require ($aspect -ge 0.25 -and $aspect -le 3.60001) "$name invalid viewport"
                    } elseif ($code -in 7,8) {
                        $length = if ($code -eq 7) { 6 } else { 5 }
                        Require ($bytes.Length -eq $length) "$name invalid round command"
                        $round = [long][BitConverter]::ToUInt32($bytes,1)
                        Require ($round -gt 0) "$name zero round command"
                        if ($code -eq 7) {
                            Require ($bytes[5] -in 0,1) "$name invalid readiness flag"
                            $queued = [int]$bytes[5]
                        }
                    } elseif ($code -eq 21) {
                        Require ($bytes.Length -eq 6 -and $bytes[5] -in 0,1 -and [BitConverter]::ToUInt32($bytes,1) -gt 0) "$name invalid map vote"
                    } elseif ($code -eq 13) {
                        Require ($bytes.Length -eq 3 -and $bytes[1] -in 0,1 -and $bytes[2] -in 0,1) "$name invalid passage control request"
                    } elseif ($code -eq 2 -and $bytes.Length -eq 2) {
                        Require ($bytes[1] -in 1,2) "$name unknown cargo box"
                    } else { Require ($code -in 2,3 -and $bytes.Length -eq 1) "$name unknown command" }
                }
            }
            [pscustomobject]@{
                sequence = $prior; direction = $row.direction; peer = [long]$row.peer
                channel = $row.channel; payload = [string]$row.payload; code = $code
                doorOpen = $doorOpen; mechanism = $mechanism
                scaleKilograms = $scaleKilograms
                items = $items; types = $types; complete = $complete; status = $status; actor = $actor
                instance = $instance; operation = $operation; shapes = $shapes; positions = $positions
                pitch = $pitch; yaw = $yaw; mode = $mode; worldX = $worldX; worldZ = $worldZ
                height = $height; crouch = $crouch; sprint = $sprint; slide = $slide
                layout = $layout; remainingMs = $remainingMs
                gearIds = $gearIds; gearTypes = $gearTypes; pouchLevel = $pouchLevel
                active = $active; sourceSlot = $sourceSlot; destSlot = $destSlot; phase = $phase
                round = $round; queued = $queued; activeRound = [long]$currentRound[$streamId]
                flags = $flags; targetX = $targetX; targetY = $targetY; originalX = $originalX; originalY = $originalY
            }
        }
    } finally { $reader.Dispose() }
}

$server = @(Read-Audit 'server')
$id = @{ A = [long](Read-Marker 'A.connected'); B = [long](Read-Marker 'B.connected') }
Require ($id.A -gt 0 -and $id.B -gt 0 -and $id.A -ne $id.B) 'separate connection IDs'
$allEvents = @($server | Where-Object channel -eq 'event')
$allCommands = @($server | Where-Object channel -eq 'command')
$events = @($allEvents | Where-Object activeRound -eq 1)
$commands = @($allCommands | Where-Object activeRound -eq 1)
$clientLogs = @{ A = @(Read-Audit 'A'); B = @(Read-Audit 'B') }
$aOpens = @($commands | Where-Object { $_.peer -eq $id.A -and $_.code -eq 2 })
$bOpens = @($commands | Where-Object { $_.peer -eq $id.B -and $_.code -eq 2 })
Require ($aOpens.Count -eq 3 -and $bOpens.Count -eq 1) 'expected blocked, allowed and resumed opens from A and one from B'
$denied = @($events | Where-Object { $_.peer -eq $id.A -and $_.status -eq 2 -and $_.sequence -gt $aOpens[0].sequence -and $_.sequence -lt $aOpens[1].sequence })
Require ($denied.Count -eq 1) 'server denied opening from behind the wall'
$earlyA = @($events | Where-Object { $_.peer -eq $id.A -and $_.sequence -lt $aOpens[1].sequence -and ($_.code -eq 3 -or $_.items.Count -gt 0) })
Require ($earlyA.Count -eq 0) 'A received crate data before authorized opening'
Mark 'A was in reach behind the wall, was denied, and received no crate data.'
Require ((Read-Marker 'server-before-search.checked') -eq '0' -and (Read-Marker 'server-generation.count') -eq '2') 'expected zero initial generations and exactly two across both raids'
Mark 'The blocked open produced no loot. The server generated two inventories across the two raids.'

# Locate the probe's stationary mode switch, full cover, and subsequent first-person advance.
$viewInput = @($commands | Where-Object { $_.peer -eq $id.A -and $_.code -eq 1 })
$peek = @($viewInput | Where-Object { $_.mode -eq 1 -and $_.pitch -eq 5 -and $_.yaw -eq 327 })[0]
$leftShoulder = @($viewInput | Where-Object { $_.mode -eq 2 -and $_.pitch -eq 5.0625 -and $_.yaw -eq 327 })[0]
$rightShoulder = @($viewInput | Where-Object { $_.mode -eq 1 -and $_.pitch -eq 5.09375 -and $_.yaw -eq 327 })[0]
$cover = @($viewInput | Where-Object { $_.mode -eq 0 -and $_.pitch -eq 5.125 })[0]
$advance = @($viewInput | Where-Object { $_.mode -eq 0 -and $_.pitch -eq 5.25 })[0]
$away = @($viewInput | Where-Object { $_.mode -eq 0 -and $_.yaw -eq 147 -and $_.pitch -eq 6.25 })[0]
$back = @($viewInput | Where-Object { $_.mode -eq 0 -and $_.pitch -eq 5.375 })[0]
Require ($peek.sequence -gt $denied[0].sequence -and $leftShoulder.sequence -gt $peek.sequence -and
    $rightShoulder.sequence -gt $leftShoulder.sequence -and $cover.sequence -gt $rightShoulder.sequence -and
    $advance.sequence -gt $cover.sequence) 'missing ordered first-person and shoulder switches'
$actorA = @($events | Where-Object { $_.peer -eq $id.A -and $_.actor -eq $id.B -and $_.code -in 1,2 })
$early = @($actorA | Where-Object { $_.sequence -lt $peek.sequence })
$partial = @($actorA | Where-Object { $_.code -eq 1 -and $_.sequence -gt $peek.sequence -and $_.sequence -lt $leftShoulder.sequence })
$leftSight = @($actorA | Where-Object { $_.sequence -gt $leftShoulder.sequence -and $_.sequence -lt $rightShoulder.sequence })
$rightSight = @($actorA | Where-Object { $_.sequence -gt $rightShoulder.sequence -and $_.sequence -lt $cover.sequence })
$leftHides = @($leftSight | Where-Object code -eq 2)
Require ($leftHides.Count -eq 1 -and $leftSight[-1].code -eq 2 -and $rightSight.Count -gt 0 -and
    @($rightSight | Where-Object code -ne 1).Count -eq 0) 'the actor must disappear once on the way left and remain hidden until the return sightline opens'
$toLeft = @($viewInput | Where-Object { $_.sequence -ge $leftShoulder.sequence -and $_.sequence -lt $rightShoulder.sequence })
$toRight = @($viewInput | Where-Object { $_.sequence -ge $rightShoulder.sequence -and $_.sequence -lt $cover.sequence })
function Assert-ShoulderTravel($samples, [int]$mode, [int]$from, [int]$to) {
    Require ($samples[0].phase -eq $from -and $samples[-1].phase -eq $to -and
        @($samples | Where-Object { $_.phase -gt 0 -and $_.phase -lt 254 }).Count -ge 2) 'physical shoulder phases jumped to the requested endpoint'
    $previous = $from
    foreach ($sample in $samples) {
        Require ($sample.mode -eq $mode -and ($sample.phase - $previous)*($to - $from) -ge 0) 'unexpected direction in shoulder transition'
        $previous = $sample.phase
    }
}
Assert-ShoulderTravel $toLeft 2 254 0
Assert-ShoulderTravel $toRight 1 0 254
Require (@($viewInput | Where-Object { $_.mode -eq 0 -and $_.phase -ne 127 }).Count -eq 0) 'first-person reports must use the neutral camera sample'
$ownA = @($events | Where-Object { $_.peer -eq $id.A -and $_.actor -eq $id.A -and $_.code -eq 1 })
$stationary = @($ownA | Where-Object { $_.sequence -gt $peek.sequence -and $_.sequence -lt $cover.sequence })
Require ($stationary.Count -gt 0 -and @($stationary | Where-Object {
    [math]::Abs($_.worldX - 1.83) -gt .06 -or [math]::Abs($_.worldZ + 1) -gt .06
}).Count -eq 0) 'the shoulder switch must occur at the stationary corner'
# Independently trace the recorded camera against the Starter Assets Armature.
# With the standing target at yaw 126, vertex 24754 of the forefoot extends at
# least this far in both world axes across 32 samples of the complete idle loop.
function Test-ExposedBody($step, $observer, $subject) {
    $rad = [Math]::PI / 180
    $aspect = [BitConverter]::ToSingle([Convert]::FromBase64String($step.payload),18)
    $t = $step.phase / 254.0
    $side = .92 * [Math]::Min(1.0,$aspect/(16/9)) * (2*$t*$t*(3-2*$t)-1)
    $pitch = $step.pitch*$rad
    $yaw = $step.yaw*$rad
    $back = .22*[Math]::Sin($pitch) - 2.15*[Math]::Cos($pitch)
    $cx = $observer.worldX + $side*[Math]::Cos($yaw) + $back*[Math]::Sin($yaw)
    $cz = $observer.worldZ - $side*[Math]::Sin($yaw) + $back*[Math]::Cos($yaw)
    $bodyYaw = [BitConverter]::ToSingle([Convert]::FromBase64String($subject.payload),17)
    if ([Math]::Abs($bodyYaw - 126) -gt .05) { return $false }
    $x = $subject.worldX + .33933
    $z = $subject.worldZ - .01030
    $slope = ($x-$cx)/($z-$cz)
    return [Math]::Min($cx+(-.15-$cz)*$slope, $cx+(.15-$cz)*$slope) -gt 1.99995
}
foreach ($pose in @(($leftSight + $rightSight) | Where-Object code -eq 1)) {
    $step = @($viewInput | Where-Object { $_.sequence -lt $pose.sequence })[-1]
    $observer = @($ownA | Where-Object { $_.sequence -lt $pose.sequence })[-1]
    Require ($observer -and (Test-ExposedBody $step $observer $pose)) 'server sent the remote position or yaw before the reported physical camera had a sightline'
}
Require (![string]::IsNullOrWhiteSpace((Read-Marker 'A.shoulder-switch'))) 'missing shoulder switch evidence'
Mark 'At the same position, the right shoulder exposed the runner. Both directions reported intermediate camera positions. Each remote pose had an open sightline from the reported shoulder phase, and no new poses followed the hide until the return path cleared the wall.'
$covered = @($actorA | Where-Object { $_.sequence -gt $cover.sequence -and $_.sequence -lt $advance.sequence })
$firstPerson = @($actorA | Where-Object { $_.code -eq 1 -and $_.sequence -gt $advance.sequence -and $_.sequence -lt $away.sequence })
$outOfFrame = @($actorA | Where-Object { $_.sequence -gt $away.sequence -and $_.sequence -lt $back.sequence })
Require ($back.sequence -gt $away.sequence -and $outOfFrame.Count -eq 1 -and $outOfFrame[0].code -eq 2) 'turning away must hide the actor without position updates'
Require ($early.Count -eq 0 -and $partial.Count -gt 0 -and $covered.Count -eq 1 -and $covered[0].code -eq 2 -and $firstPerson.Count -gt 0) 'remote position or yaw outside the allowed corner sight lines, or late reveal'
foreach ($role in 'A','B') {
    $other = if ($role -eq 'A') { $id.B } else { $id.A }
    $sent = @($events | Where-Object { $_.peer -eq $id[$role] -and $_.actor -eq $other -and $_.code -in 1,2 })
    $received = @($clientLogs[$role] | Where-Object { $_.activeRound -eq 1 -and $_.direction -eq 'rx' -and $_.actor -eq $other -and $_.code -in 1,2 })
    Require (@($sent | Where-Object { $_.sequence -le $denied[0].sequence }).Count -eq 0) "$role received a remote actor at its hidden spawn"
    Require (($sent.payload -join ',') -ceq ($received.payload -join ',')) "$role remote pose stream did not match the client capture"
}
Mark 'No remote coordinates or yaw were sent before the first corner peek or during renewed full cover. Switching viewpoint at rest exposed the other runner. Both clients received exactly the recorded remote packets.'
$pass = @($commands | Where-Object { $_.peer -eq $id.B -and $_.code -eq 1 -and $_.pitch -eq 2.625 })[0]
$returned = @($viewInput | Where-Object { $_.mode -eq 0 -and $_.pitch -eq 4.875 })[0]
$duringPass = @($actorA | Where-Object { $_.sequence -gt $pass.sequence -and $_.sequence -lt $returned.sequence })
$observer = @($ownA | Where-Object { $_.sequence -gt $pass.sequence -and $_.sequence -lt $returned.sequence })
Require ($observer.Count -gt 0 -and @($observer | Where-Object {
    [math]::Abs($_.worldX - 1.90) -gt .06 -or [math]::Abs($_.worldZ + 1) -gt .06
}).Count -eq 0) 'the moving target must be observed from the original first-person test position'
$hidden = @($duringPass | Where-Object code -eq 2)
Require ($pass -and $returned -and $duringPass.Count -ge 2 -and $duringPass[0].code -eq 1 -and $hidden.Count -eq 1 -and $duringPass[-1].code -eq 2) 'moving runner must appear and disappear once during the pass'
$concealedSteps = @($events | Where-Object { $_.code -eq 1 -and $_.peer -eq $id.B -and $_.actor -eq $id.B -and $_.sequence -gt $hidden[0].sequence -and $_.sequence -lt $returned.sequence })
$distance = ($concealedSteps.worldX | Measure-Object -Maximum -Minimum)
Require ($concealedSteps.Count -ge 2 -and $distance.Maximum - $distance.Minimum -gt 0.2) 'target did not continue moving behind cover'
Mark 'The moving runner first appeared with its center behind the wall, then continued walking behind full cover. During that hidden movement the observer received zero new remote poses.'

$execution = Get-Content -Raw -LiteralPath (Join-Path $RunDirectory 'processes.json') | ConvertFrom-Json
if ($execution.captureFrames) {
    $distances = Read-Marker 'A.perspective'
    $smooth = Read-Marker 'A.shoulder-smooth'
    Require ($smooth -match '^(\d+) intermediate frames; largest screen-X step ([\d.]+); mid-flight reversal at ([\d.]+)$') 'missing physical camera reversal measurement'
    $frames = [int]$Matches[1]
    $jump = [double]::Parse($Matches[2], [cultureinfo]::InvariantCulture)
    $turn = [double]::Parse($Matches[3], [cultureinfo]::InvariantCulture)
    Require ($frames -ge 2 -and $jump -gt 0 -and $jump -lt .2 -and $turn -gt .33 -and $turn -lt .62) 'camera snapped or missed the mid-flight reversal'
    Mark "The renderer followed the shoulder transition and reversed from its current point ($smooth)."
    $dock = Read-Marker 'A.dock'
    Require ($distances -match 'chest X [\d.]+\s*% right / [\d.]+\s*% left') 'missing the measured viewport X on both shoulders'
    Require ($dock -match 'luminance delta [\d.]+/255 across 24 regions, both panels and rotated atlas images fit') 'missing the open/closed scene comparison or inventory geometry checks'
    $retracted = Read-Marker 'A.camera-wall'
    $reveal = Read-Marker 'A.remote-reveal'
    $thirdCorner = Read-Marker 'A.third-corner'
    $firstCorner = Read-Marker 'A.first-corner'
    Mark "The remote runner was immediately opaque and correctly oriented on both sightings. $reveal"
    Mark "The center was still behind the wall when the renderer showed a partial silhouette in third person ($thirdCorner pixels) and first person ($firstCorner pixels)."
    foreach ($name in 'A-left-shoulder.png','A-right-shoulder.png','A-field-pack.png','A-third-corner.png','A-first-corner.png','A-terminal.png','A-first-person.png','A-footprints.png','A-search.png','A-drag.png','A-inventory.png','B-inventory.png','A-bag.png','B-bag.png','A-menu.png','A-base.png','A-result.png','A-sweep.png') {
        $frame = Get-Item -LiteralPath (Join-Path $RunDirectory $name) -ErrorAction Stop
        $header = New-Object byte[] 24
        $stream = [IO.File]::OpenRead($frame.FullName)
        try { $bytesRead = $stream.Read($header,0,$header.Length) } finally { $stream.Dispose() }
        Require ($bytesRead -eq 24 -and [BitConverter]::ToString($header,0,8) -ceq '89-50-4E-47-0D-0A-1A-0A' -and
            [Text.Encoding]::ASCII.GetString($header,12,4) -ceq 'IHDR' -and
            [BitConverter]::ToUInt32($header,16) -ne 0 -and [BitConverter]::ToUInt32($header,20) -ne 0) "Invalid camera capture $name"
    }
    Mark "Camera measurements included both shoulder views ($distances)"
    Mark "Compared the world before and after opening the field inventory ($dock)."
    Mark "Facing away from the partition retracted the third-person camera to $retracted m on the runner's side."
}

function Inventory-Key($snapshot) {
    $pairs = for ($i=0; $i -lt $snapshot.items.Count; $i++) {
        [pscustomobject]@{ id = $snapshot.items[$i]; type = $snapshot.types[$i] }
    }
    return (@($pairs | Sort-Object id | ForEach-Object { "$($_.id)/$($_.type)" }) -join ',')
}
function Assert-Discovery($views,[string]$role) {
    $initialCount = $views[0].shapes.Count
    Require ($initialCount -in 11,12 -and $views[0].items.Count -eq 0) "$role did not begin with anonymous footprints only"
    $grid = @{}
    foreach ($shape in $views[0].shapes) {
        $sx,$sy,$sw,$sh = [int[]]$shape.Split('/')
        for ($dy=0; $dy -lt $sh; $dy++) {
            for ($dx=0; $dx -lt $sw; $dx++) { $grid[($sy+$dy)*6+$sx+$dx] = $shape }
        }
    }
    foreach ($shape in $views[0].shapes) {
        $sx,$sy,$sw,$sh = [int[]]$shape.Split('/')
        for ($earlier=0; $earlier -lt $sy*6+$sx; $earlier++) {
            $ex = $earlier % 6
            $ey = [int][Math]::Floor($earlier/6)
            if ($ex+$sw -gt 6 -or $ey+$sh -gt 6) { continue }
            $occupied = $false
            for ($dy=0; $dy -lt $sh -and !$occupied; $dy++) {
                for ($dx=0; $dx -lt $sw; $dx++) {
                    $other = $grid[($ey+$dy)*6+$ex+$dx]
                    if ($other -and $other -ne $shape) { $occupied = $true; break }
                }
            }
            Require $occupied "$role scattered the initial crate: $shape fits earlier at $ex/$ey"
        }
    }
    for ($i=1; $i -lt $views.Count; $i++) {
        $prior = $views[$i-1]
        $next = $views[$i]
        $growth = $next.items.Count - $prior.items.Count
        Require ($growth -in 0,1 -and !$prior.complete) "$role revealed too many items at once or finished prematurely"
        Require ($next.items.Count+$next.shapes.Count -eq $initialCount) "$role lost or gained a footprint during search"
        if ($growth -eq 1) {
            $new = @(0..($next.items.Count-1) | Where-Object { $next.items[$_] -notin $prior.items })
            Require ($new.Count -eq 1) "$role did not reveal one actual instance"
            $index = $new[0]
            $x,$y,$rot = $next.positions[$index].Split('/')
            $type = $next.types[$index]
            $w = if ($rot -eq '1') { $heights[$type] } else { $widths[$type] }
            $h = if ($rot -eq '1') { $widths[$type] } else { $heights[$type] }
            Require ("$x/$y/$w/$h" -ceq $prior.shapes[0]) "$role revealed an item in the wrong footprint"
            $remainder = @($prior.shapes | Select-Object -Skip 1)
            Require (($next.shapes -join ',') -ceq ($remainder -join ',')) "$role moved the remaining footprints"
        } else {
            Require ($next.layout -ceq $prior.layout) "$role changed layout while paused"
        }
        for ($j=0; $j -lt $prior.items.Count; $j++) {
            $k = [array]::IndexOf($next.items,$prior.items[$j])
            Require ($k -ge 0 -and $next.types[$k] -eq $prior.types[$j] -and $next.positions[$k] -ceq $prior.positions[$j]) "$role lost or changed an already discovered item"
        }
    }
    $last = $views[-1]
    Require ($last.complete -and $last.items.Count -eq $initialCount) "$role final crate did not meet the loot table bounds"
    Require (@($last.types | Sort-Object -Unique).Count -eq $last.types.Count) "$role duplicate randomized type"
    foreach ($type in 15..20) {
        Require ($type -in $last.types) "$role missing test backpack or pouch type $type"
    }
}

$transfers = @($commands | Where-Object { $_.code -in 4,5 })
foreach ($request in @($commands | Where-Object { $_.code -in 4,5,6 })) {
    $reply = @($events | Where-Object { $_.peer -eq $request.peer -and $_.code -eq 6 -and $_.operation -eq $request.operation })
    Require ($reply.Count -eq 1 -and $reply[0].sequence -gt $request.sequence) 'missing or duplicated private transfer acknowledgement'
}
Mark 'Each accepted HUD item stayed at its drop target from mouse release through every private response and split inventory snapshot. Every transfer had a single matching outcome for its sender.'
Require ($transfers.Count -eq 6) 'expected two competing takes, a return, a claim and two further takes'
$places = @($commands | Where-Object code -eq 6)
Require ($places.Count -eq 7) 'expected six activity requests from grid A and one after closing the crate'
$discoveryEnd = $places[0].sequence
$raceStart = $transfers[0].sequence
$closeA = @($commands | Where-Object { $_.peer -eq $id.A -and $_.code -eq 3 })
Require ($closeA.Count -eq 2) 'A must close the partial search and close again before evacuation'
$paused = @($events | Where-Object { $_.peer -eq $id.A -and $_.code -eq 3 -and $_.sequence -gt $closeA[0].sequence -and $_.sequence -lt $aOpens[2].sequence })
Require ($paused.Count -eq 0) 'closed A search continued to send snapshots'
$viewsA = @($events | Where-Object { $_.peer -eq $id.A -and $_.code -eq 3 -and $_.sequence -lt $discoveryEnd })
$viewsB = @($events | Where-Object { $_.peer -eq $id.B -and $_.code -eq 3 -and $_.sequence -lt $discoveryEnd })
Assert-Discovery $viewsA 'A'
Assert-Discovery $viewsB 'B'
$preClose = @($viewsA | Where-Object { $_.sequence -lt $closeA[0].sequence })[-1]
$resumed = @($viewsA | Where-Object { $_.sequence -gt $aOpens[2].sequence })[0]
Require ($preClose.items.Count -ge 2 -and !$preClose.complete -and $resumed.layout -ceq $preClose.layout) 'reopening A lost, rerolled or advanced previously discovered items'
Require ($resumed.remainingMs -gt 0 -and $resumed.remainingMs -lt 750 -and $resumed.remainingMs -le $preClose.remainingMs) 'reopening did not resume the partially searched item'
Mark "The search resumed the partially searched item with $($resumed.remainingMs) ms remaining and the same footprint and item layout."
Require ($viewsA[-1].sequence -lt $bOpens[0].sequence -and $viewsB[0].sequence -gt $bOpens[0].sequence) 'B received crate information before opening the crate'
Require ($viewsA[-1].payload -eq $viewsB[-1].payload) 'players discovered different final crate instances'
$searchMs = [long](Read-Marker 'B.search-ms')
$count = $viewsB[-1].items.Count
Require ($searchMs -ge 620*$count) 'items did not take time to identify'
Mark "Both openers received $count anonymous rectangles. Each identification replaced exactly one rectangle with its item; no empty-cell scans, item identifiers or type bytes in the hidden records. B searched for $searchMs ms."
Mark 'Closing paused all crate snapshots. Reopening immediately restored only the discoveries from before closure. Nearby B received no crate frame before its own search.'

$gridId = $places[0].instance
$initialLayout = $viewsA[-1]
$gridIndex = [array]::IndexOf($initialLayout.items,$gridId)
Require ($gridIndex -ge 0 -and $widths[$initialLayout.types[$gridIndex]] -ne $heights[$initialLayout.types[$gridIndex]]) 'expected a rectangular test item'
$initialPlace = $initialLayout.positions[$gridIndex]
$lastSource = $initialPlace
for ($i=0; $i -lt 6; $i++) {
    $request = $places[$i]
    $end = if ($i -lt 5) { $places[$i+1].sequence } else { $raceStart }
    Require ($request.instance -eq $gridId -and $request.peer -eq $id.A -and $request.sequence -lt $raceStart) 'unexpected grid sender or instance'
    $slice = @($events | Where-Object { $_.sequence -gt $request.sequence -and $_.sequence -lt $end })
    $outcome = @($slice | Where-Object { $_.peer -eq $id.A -and $_.code -eq 6 })
    $status = @(4,12,12,13,13,10)[$i]
    Require ($outcome.Count -eq 1 -and $outcome[0].status -eq $status) "grid step $i did not produce expected status $status"
    $fromBag = ($request.flags -band 1) -ne 0
    $toBag = ($request.flags -band 2) -ne 0
    $rotation = [int](($request.flags -band 4) -ne 0)
    $originalRotation = [int](($request.flags -band 8) -ne 0)
    $source = "$($request.originalX)/$($request.originalY)/$originalRotation"
    $destination = "$($request.targetX)/$($request.targetY)/$rotation"
    Require ($source -ceq $lastSource) "grid step $i did not start at the last confirmed placement"
    if ($i -in 0,1) { Require ($rotation -ne $originalRotation) "grid step $i failed to rotate" }
    if ($i -eq 1) { Require ($request.targetX -eq $request.originalX -and $request.targetY -eq $request.originalY) 'second rotation did not retain the top-left anchor' }
    if ($i -eq 2) { Require ($destination -cne $source) 'rearrangement failed to move item' }
    if ($i -in 3,4) {
        Require (@($slice | Where-Object { $_.code -in 3,4 }).Count -eq 0) "denied grid step $i changed a container"
        continue
    }
    $kind = if ($toBag) { 4 } else { 3 }
    $view = @($slice | Where-Object { $_.peer -eq $id.A -and $_.code -eq $kind })[-1]
    Require ($null -ne $view) "grid step $i missing authoritative snapshot"
    $index = [array]::IndexOf($view.items,$gridId)
    Require ($index -ge 0 -and $view.positions[$index] -ceq $destination) "grid step $i placed item at the wrong target"
    $lastSource = $destination
}
Require ($lastSource -ceq $initialPlace) 'round trip changed the original slot or rotation'
$privateLeak = @($events | Where-Object { $_.peer -eq $id.B -and $_.code -eq 4 -and $_.sequence -gt $discoveryEnd -and $_.sequence -lt $raceStart })
Require ($privateLeak.Count -eq 0) "B received bag frames while A rearranged their items"
Mark 'The floating item followed sub-cell pointer motion and kept its center when rotated from an off-center grip. Releasing it snapped to the requested cells. The server rejected overlap and edge overflow without changing either inventory.'

$contested = $transfers[0].instance
Require ($transfers[0].code -eq 4 -and $transfers[1].code -eq 4 -and $transfers[0].peer -ne $transfers[1].peer -and $transfers[1].instance -eq $contested) 'distinct racers must request the same item'
$spreadMs = [math]::Abs(([long](Read-Marker 'A.take') - [long](Read-Marker 'B.take')) / 10000.0)
Require ($spreadMs -le 150) "take synchronization spread was ${spreadMs}ms (limit 150ms)"
$raceReplies = @($events | Where-Object { $_.sequence -gt $raceStart -and $_.sequence -lt $transfers[2].sequence })
$wins = @($raceReplies | Where-Object status -eq 4)
$misses = @($raceReplies | Where-Object status -eq 5)
Require ($wins.Count -eq 1 -and $misses.Count -eq 1 -and $wins[0].peer -ne $misses[0].peer) 'server did not send opposite race outcomes to the competing players'
$winner = $wins[0].peer
$other = $misses[0].peer
Require ($transfers[2].code -eq 5 -and $transfers[2].instance -eq $contested -and $transfers[2].peer -eq $winner) 'original winner must return the contested instance'
Require ($transfers[3].code -eq 4 -and $transfers[3].instance -eq $contested -and $transfers[3].peer -eq $other) 'other client must take the returned instance'
$loserRole = if ($id.A -eq $other) { 'A' } else { 'B' }
$pointerMarker = Read-Marker "$loserRole.pointer-tap"
Require ($pointerMarker -match '^\d+,\d+$') 'missing single-F target and independent keyboard selection'
$pointerIds = @($pointerMarker.Split(',') | ForEach-Object { [uint32]::Parse($_,[cultureinfo]::InvariantCulture) })
Require ($pointerIds[0] -in $viewsA[-1].items -and $pointerIds[0] -ne $contested -and $pointerIds[1] -eq $transfers[3].instance) 'single F used the keyboard selection instead of the hovered item'
Mark 'A single press of F reclaimed the item under the pointer while a different identified item was selected by keyboard. The server received the hovered instance ID.'
Require ($transfers[4].code -eq 4 -and $transfers[5].code -eq 4 -and $transfers[4].peer -eq $winner -and $transfers[5].peer -eq $winner) 'winner must take two other crate items'
Require (@($contested,$transfers[4].instance,$transfers[5].instance | Sort-Object -Unique).Count -eq 3) 'the three distributed instances must be distinct'
Mark ("Concurrent takes arrived from separate clients (send spread {0:F1} ms). Exactly one got the item. It was returned and taken by the other player." -f $spreadMs)

$original = $viewsB[-1]
foreach ($role in 'A','B') {
    $client = @($clientLogs[$role] | Where-Object activeRound -eq 1)
    $received = @($client | Where-Object { $_.direction -eq 'rx' -and $_.channel -eq 'event' })
    $sent = @($events | Where-Object { $_.peer -eq $id[$role] })
    $clientCritical = @($received | Where-Object { $_.code -in 3,4,5,6 } | ForEach-Object { $_.payload })
    $serverCritical = @($sent | Where-Object { $_.code -in 3,4,5,6 } | ForEach-Object { $_.payload })
    Require ($clientCritical.Count -gt 0 -and ($clientCritical -join ',') -ceq ($serverCritical -join ',')) "$role received different private/status bytes than the server submitted"
    $openTx = @($client | Where-Object { $_.direction -eq 'tx' -and $_.channel -eq 'command' -and $_.code -eq 2 })
    $valid = if ($role -eq 'A') { $openTx[1] } else { $openTx[0] }
    $early = @($received | Where-Object { $_.sequence -lt $valid.sequence -and ($_.code -eq 3 -or $_.items.Count -gt 0) })
    Require ($early.Count -eq 0) "$role application received loot before an allowed open"
    $expected = if ($id[$role] -eq $winner) { @($transfers[4].instance,$transfers[5].instance) } else { @($contested) }
    $bags = @($sent | Where-Object { $_.code -eq 4 -and $_.sequence -gt $raceStart -and $_.sequence -lt $places[6].sequence })
    $receipts = @($sent | Where-Object code -eq 5)
    Require ($receipts.Count -eq 1 -and $receipts[0].items.Count -eq @($expected).Count) "$role extracted the wrong number of items"
        $receipt = $receipts[0]
    $finalBag = @($sent | Where-Object code -eq 4)[-1]
    Require ($finalBag.items.Count -eq $receipt.items.Count -and ($finalBag.positions -join ',') -ceq ($receipt.positions -join ',')) "$role lost grid placement at extraction"
    Require ((Inventory-Key $finalBag) -ceq (Inventory-Key $receipt) -and (Inventory-Key $receipt) -ceq (Read-Marker "$role.result")) "$role result did not match the final bag"
    foreach ($uid in $expected) {
        $initial = [array]::IndexOf($original.items,$uid)
        $final = [array]::IndexOf($receipt.items,$uid)
        Require ($initial -ge 0 -and $final -ge 0 -and $original.types[$initial] -eq $receipt.types[$final]) "$role got a changed or foreign item in the extraction result"
    }
    # Reconstruct each private bag from that player's successful transfers.
    $history = if ($id[$role] -eq $winner) { @(1,0,1,2) } else { @(1) }
    Require ($bags.Count -eq $history.Count) "$role received a bag update from another player's transfer"
    for ($i=0; $i -lt $bags.Count; $i++) { Require ($bags[$i].items.Count -eq $history[$i]) "$role incorrect bag history at $i" }
    Require ($bags[0].items[0] -eq $contested) "$role did not receive the contested instance"
    $views = @($sent | Where-Object code -eq 3)
    $remaining = $views[-1].items
    Require ($remaining.Count -eq $count-3) "$role final crate size does not match the transfer history"
    foreach ($uid in $original.items) {
        $distributed = $uid -in @($contested,$transfers[4].instance,$transfers[5].instance)
        Require (($uid -in $remaining) -ne $distributed) "$role final crate duplicated or lost an instance"
    }
    $observedMs = [long](Read-Marker "$role.exit-ms")
    Require ($observedMs -ge 2600) "$role insufficient time in extraction zone"
    Mark ("{0} received {1} matching private/status packets and extracted {2} distinct personally owned items. The crate retained all {3} other instances." -f $role,$clientCritical.Count,$receipt.items.Count,$remaining.Count)
}
$personal = $places[6]
Require ($personal.peer -eq $other -and ($personal.flags -band 3) -eq 3) 'expected the runner with a single item to move it inside their own bag'
$released = @($events | Where-Object { $_.peer -eq $other -and $_.status -eq 3 -and $_.sequence -lt $personal.sequence })[-1]
Require ($released.sequence -gt $transfers[-1].sequence) 'personal bag move must follow closing the shared crate'
$view = @($events | Where-Object { $_.peer -eq $other -and $_.code -eq 4 -and $_.sequence -gt $personal.sequence })[0]
$after = "$($personal.targetX)/$($personal.targetY)/$([int](($personal.flags -band 4) -ne 0))"
Require ($view.items.Count -eq 1 -and $view.positions[0] -ceq $after) 'personal bag move failed away from closed crate'
Mark 'Ctrl-click transferred discovered loot using first-fit placement. After closing the crate and walking away, dragging inside the personal bag changed the saved cell and the extraction receipt preserved it.'

foreach ($marker in 'A.menu-ok','A.waiting-next','A.next-looted','A.new-raid','B.new-raid','A.done','B.done') {
    Require (![string]::IsNullOrWhiteSpace((Read-Marker $marker))) "missing probe milestone $marker"
}
Require (@($commands | Where-Object { $_.peer -eq $id.B -and $_.code -eq 3 }).Count -eq 1) 'opening the personal inventory generated a crate command'
Mark 'Escape opened the menu, the personal bag returned to that menu, and Resume restored control. The menu and bag added no server inventory commands.'

$nextEvents = @($allEvents | Where-Object activeRound -eq 2)
$nextCommands = @($allCommands | Where-Object activeRound -eq 2)
$starts = @{}
$receipts = @{}
foreach ($role in 'A','B') {
    $peer = $id[$role]
    $stream = @($server | Where-Object peer -eq $peer)
    $client = $clientLogs[$role]
    foreach ($trace in @(@{ rows = $stream; label = "server for $role" }, @{ rows = $client; label = $role })) {
        $rows = $trace.rows
        $label = $trace.label
        $start = @($rows | Where-Object { $_.channel -eq 'event' -and $_.code -eq 8 })
        Require ($start.Count -eq 2 -and ($start.round -join ',') -ceq '1,2') "$label must enter each raid once"
        foreach ($round in 1,2) {
            $epoch = @($rows | Where-Object activeRound -eq $round)
            $entry = @($epoch | Where-Object channel -eq 'event' | Select-Object -First 6)
            $initialIds = @($entry[1].mechanism, $entry[2].mechanism | Sort-Object)
            Require (($entry.code -join ',') -ceq '8,12,12,1,10,4' -and ($initialIds -join ',') -ceq '0,1' -and
                !$entry[1].doorOpen -and !$entry[2].doorOpen -and
                $entry[3].actor -eq $peer -and $entry[4].active -eq 1 -and $entry[5].items.Count -eq 0) "$label incorrect start sequence, door state, equipment or initial bag"
            $ack = @($epoch | Where-Object { $_.channel -eq 'command' -and $_.code -eq 8 })
            Require ($ack.Count -eq 1 -and $ack[0].round -eq $round -and $ack[0].sequence -gt $start[$round-1].sequence) "$label missing round acknowledgement"
            $beforeAck = @($epoch | Where-Object { $_.channel -eq 'command' -and $_.sequence -lt $ack[0].sequence })
            Require ($beforeAck.Count -eq 0) "$label issued commands before acknowledging the new raid"
        }
    }
    # Compare each ordered direction separately. The simulation may still be sending movement
    # snapshots during process shutdown, so compare all discrete events and actions here.
    foreach ($channel in 'event','command') {
        $included = if ($channel -eq 'event') { @(3,4,5,6,8,9,10,11,12,15) } else { @(2,3,4,5,6,7,8,9,10,11,12,13) }
        $serverBytes = @($stream | Where-Object { $_.channel -eq $channel -and $_.code -in $included })
        $clientBytes = @($client | Where-Object { $_.channel -eq $channel -and $_.code -in $included })
        Require (($serverBytes.payload -join ',') -ceq ($clientBytes.payload -join ',')) "$role $channel bytes differ across round boundary"
    }
    $starts[$role] = @($nextEvents | Where-Object { $_.peer -eq $peer -and $_.code -eq 8 })[0].sequence
    $receipt = @($events | Where-Object { $_.peer -eq $peer -and $_.code -eq 5 })[0]
    $receipts[$role] = $receipt.sequence
    $afterResult = @($events | Where-Object { $_.peer -eq $peer -and $_.sequence -gt $receipt.sequence -and $_.code -notin 9,11 })
    Require ($afterResult.Count -eq 0) "$role received gameplay state after extraction and before the next start"
}
$aReady = @($commands | Where-Object { $_.code -eq 7 -and $_.peer -eq $id.A })
$bReady = @($commands | Where-Object { $_.code -eq 7 -and $_.peer -eq $id.B })
$queue = @($events | Where-Object code -eq 9)
Require ($aReady.Count -eq 3 -and ($aReady.queued -join ',') -ceq '1,0,1' -and
    $bReady.Count -eq 1 -and $bReady[0].queued -eq 1 -and
    @(@($aReady + $bReady) | Where-Object round -ne 1).Count -eq 0) 'unexpected readiness requests'
Require ($queue.Count -eq 3 -and ($queue.queued -join ',') -ceq '1,0,1') 'expected entry, cancellation and re-entry for A'
for ($i=0; $i -lt 3; $i++) {
    $nextBoundary = if ($i -lt 2) { $aReady[$i+1].sequence } else { $receipts.B }
    Require ($aReady[$i].sequence -gt $receipts.A -and $queue[$i].round -eq 1 -and $queue[$i].peer -eq $id.A -and
        $queue[$i].sequence -gt $aReady[$i].sequence -and $queue[$i].sequence -lt $nextBoundary) 'queue change applied outside the active first raid'
}
Require ($starts.A -gt $receipts.B -and $bReady[0].sequence -gt $starts.A -and $starts.B -gt $bReady[0].sequence) 'wrong admission order'
Require (@($nextCommands | Where-Object code -eq 7).Count -eq 0 -and @($nextEvents | Where-Object { $_.code -in 5,9 }).Count -eq 0) 'unexpected second-raid extraction or queue'
Mark 'A queued, cancelled and queued again while B retained their first-raid loot. Only after B extracted did A enter the next raid. Each client acknowledged both starts and received an empty personal bag on entry.'

$opens = @($nextCommands | Where-Object code -eq 2)
Require ($opens.Count -eq 1 -and $opens[0].peer -eq $id.A) 'expected a single authorized opening of the second crate'
$opening = $opens[0]
$newViews = @($nextEvents | Where-Object { $_.peer -eq $id.A -and $_.code -eq 3 })
Require ($newViews.Count -gt 0 -and $newViews[0].sequence -gt $opening.sequence) 'crate revealed before the second-raid opening'
$allowed = @($nextEvents | Where-Object { $_.peer -eq $id.A -and $_.code -eq 6 -and $_.status -eq 1 })
Require ($allowed.Count -eq 1 -and $allowed[0].sequence -gt $opening.sequence -and $allowed[0].sequence -lt $newViews[0].sequence) 'second-raid crate lacked an authorized opening'
$sweep = @($nextCommands | Where-Object { $_.code -in 4,5,6 })
Require ($sweep.Count -eq 4 -and ($sweep.code -join ',') -ceq '4,4,5,5' -and
    @($sweep | Where-Object peer -ne $id.A).Count -eq 0) 'expected precisely two takes and two returns by A'
$uidText = Read-Marker 'A.sweep'
Require ($uidText -match '^\d+,\d+$') 'invalid sweep IDs'
$picked = @($uidText.Split(',') | ForEach-Object { [uint32]::Parse($_,[cultureinfo]::InvariantCulture) })
Require ($picked[0] -gt 0 -and $picked[1] -gt 0 -and $picked[0] -ne $picked[1] -and
    $sweep[0].instance -eq $picked[0] -and $sweep[1].instance -eq $picked[1] -and
    $sweep[2].instance -in $picked -and $sweep[3].instance -in $picked -and $sweep[2].instance -ne $sweep[3].instance) 'wrong sweep items or duplicate transfer'
$search = @($newViews | Where-Object { $_.sequence -lt $sweep[0].sequence })
Require (@($nextCommands | Where-Object { $_.code -eq 3 -and $_.sequence -gt $opening.sequence -and $_.sequence -lt $sweep[-1].sequence }).Count -eq 0) 'F on an empty area interrupted the open crate'
Assert-Discovery $search 'A second raid'
$fresh = $search[-1]
Require ((Inventory-Key $fresh) -cne (Inventory-Key $original) -and $picked[0] -in $fresh.items -and $picked[1] -in $fresh.items) 'new raid repeated the old ledger or swept unidentified items'
$carried = [Collections.Generic.HashSet[long]]::new()
$lastOperation = @($commands | Where-Object { $_.peer -eq $id.A -and $_.code -in 4,5,6 } | Measure-Object operation -Maximum)[0].Maximum
for ($i=0; $i -lt 4; $i++) {
    $request = $sweep[$i]
    $limit = if ($i -lt 3) { $sweep[$i+1].sequence } else { @($nextCommands | Where-Object { $_.code -in 9,10 } | Select-Object -First 1)[0].sequence }
    Require ($request.operation -gt $lastOperation -and $limit -gt $request.sequence) 'sweep request repeated or occurred after B joined'
    $lastOperation = $request.operation
    $taking = $request.code -eq 4
    if ($taking) { Require ($carried.Add($request.instance)) 'duplicate take' }
    else { Require ($carried.Remove($request.instance)) 'return of unowned item' }
    $slice = @($nextEvents | Where-Object { $_.peer -eq $id.A -and $_.sequence -gt $request.sequence -and $_.sequence -lt $limit })
    $acks = @($nextEvents | Where-Object { $_.peer -eq $id.A -and $_.code -eq 6 -and $_.operation -eq $request.operation })
    $status = if ($taking) { 4 } else { 10 }
    Require ($acks.Count -eq 1 -and $acks[0].status -eq $status -and $acks[0].sequence -gt $request.sequence -and $acks[0].sequence -lt $limit) 'wrong authoritative sweep acknowledgement'
    $bags = @($slice | Where-Object code -eq 4)
    $crates = @($slice | Where-Object code -eq 3)
    Require ($bags.Count -eq 1 -and $crates.Count -eq 1 -and $bags[0].items.Count -eq $carried.Count -and $crates[0].complete -and
        $crates[0].items.Count -eq $fresh.items.Count - $carried.Count) 'sweep did not update both containers exactly once'
    foreach ($uid in $fresh.items) {
        $inBag = $carried.Contains([long]$uid)
        $container = if ($inBag) { $bags[0] } else { $crates[0] }
        $index = [array]::IndexOf($container.items,$uid)
        Require ($index -ge 0 -and $fresh.types[[array]::IndexOf($fresh.items,$uid)] -eq $container.types[$index]) 'sweep lost, duplicated or changed an item'
    }
}
$bPrivate = @($nextEvents | Where-Object { $_.peer -eq $id.B -and $_.code -in 3,4,5,6 })
Require ($bPrivate.Count -eq 1 -and $bPrivate[0].code -eq 4 -and $bPrivate[0].items.Count -eq 0 -and
    $bReady[0].sequence -gt $newViews[-1].sequence) 'late join received A loot or interrupted the second raid'
Mark 'Holding F over an empty area sent no item requests. Crossing two identified items sent exactly two takes. Revisiting them sent no duplicates. One hold over the bag returned both items, with one matching private acknowledgement and two authoritative container snapshots per transfer.'
$walk = Read-Marker 'A.pack-walk'
Require (![string]::IsNullOrWhiteSpace($walk)) 'missing movement with the field backpack open'
$awayFromCrate = @($nextEvents | Where-Object { $_.peer -eq $id.A -and $_.sequence -gt $sweep[-1].sequence -and $_.sequence -lt $bReady[0].sequence })
$lost = @($awayFromCrate | Where-Object { $_.code -eq 6 -and $_.status -eq 9 -and $_.operation -eq 0 })
$steps = @($awayFromCrate | Where-Object { $_.code -eq 1 -and $_.actor -eq $id.A })
$walkRange = $steps.worldX | Measure-Object -Minimum -Maximum
Require ($lost.Count -eq 1 -and $steps.Count -ge 2 -and $walkRange.Maximum - $walkRange.Minimum -gt .5 -and
    @($awayFromCrate | Where-Object { $_.sequence -gt $lost[0].sequence -and $_.code -eq 3 }).Count -eq 0) 'walking with the backpack did not release the distant crate'
Mark "The player moved with the field backpack open. The server closed the out-of-range crate and sent no further crate data. $walk"
Mark 'B joined the second raid after the sweep with an empty backpack. Its connection received no private crate or transfer data from A.'
Require (![string]::IsNullOrWhiteSpace((Read-Marker 'A.gear'))) 'missing equipment gestures'
$aGear = @($nextEvents | Where-Object { $_.peer -eq $id.A -and $_.code -eq 10 })
$bGear = @($nextEvents | Where-Object { $_.peer -eq $id.B -and $_.code -eq 10 })
$moves = @($nextCommands | Where-Object code -eq 9)
$select = @($nextCommands | Where-Object code -eq 10)
Require ($aGear.Count -ge 13 -and $bGear.Count -eq 1) 'equipment was not private to its owner'
foreach ($idx in 1..5) {
    Require ($aGear[0].gearIds[$idx] -gt 0 -and $bGear[0].gearIds[$idx] -gt 0 -and
        $aGear[0].gearIds[$idx] -notin $bGear[0].gearIds) 'missing or shared instance in starting equipment'
}
$choices = @($commands | Where-Object { $_.peer -eq $id.A -and $_.code -eq 11 })
$levels = @($events | Where-Object { $_.peer -eq $id.A -and $_.code -eq 11 -and $_.sequence -gt $choices[0].sequence })
Require (($choices.pouchLevel -join ',') -ceq '2,3,4,1,2' -and ($levels.pouchLevel -join ',') -ceq '2,3,4,1,2') 'base pouch levels did not confirm'
Require ($choices[0].sequence -gt $receipts.A -and $choices[-1].sequence -lt $aReady[0].sequence -and
    $aGear[0].gearTypes[5] -eq 17 -and $bGear[0].gearTypes[5] -eq 16) 'selected pouch leaked or failed to persist into next raid'
Require (($moves.sourceSlot -join ',') -ceq '10,5,10,10,1,2,10,2,10,0,10,4,0,3,0' -and
    ($moves.destSlot -join ',') -ceq '8,0,6,7,2,1,4,0,4,2,4,0,4,0,3') 'wrong equipment drag/drop history'
Require (($select.destSlot -join ',') -ceq '2,1,6,7,2,6') 'wrong quick-access selection history'
foreach ($cmd in @($moves + $select)) {
    $ack = @($nextEvents | Where-Object { $_.peer -eq $id.A -and $_.code -eq 6 -and $_.operation -eq $cmd.operation })
    $expected = if ($cmd.operation -in $moves[0,1].operation) { 6 } elseif ($cmd.operation -eq $moves[8].operation) { 13 } else { 12 }
    Require ($ack.Count -eq 1 -and $ack[0].status -eq $expected -and $ack[0].sequence -gt $cmd.sequence) 'missing or wrong equipment acknowledgement'
}
$final = $aGear[-1]
Require (($final.gearIds[1..5] -join ',') -ceq ($aGear[0].gearIds[1..5] -join ',') -and
    $final.gearIds[6] -eq $moves[2].instance -and $final.gearIds[7] -eq $moves[3].instance -and
    $final.gearIds[8] -eq 0 -and $final.gearIds[9] -eq 0 -and $final.active -eq 6) 'gear ownership or final quick selection diverged'
$localMoves = @($clientLogs.A | Where-Object { $_.activeRound -eq 2 -and $_.channel -eq 'command' -and $_.code -eq 9 })
$firstReply = @($clientLogs.A | Where-Object { $_.activeRound -eq 2 -and $_.channel -eq 'event' -and
    $_.code -eq 6 -and $_.operation -eq $localMoves[2].operation })
Require ($firstReply.Count -eq 1 -and $localMoves[3].sequence -lt $firstReply[0].sequence) 'second independent drop was not sent before the first acknowledgement'
Mark 'Two independent drops reached the first two pouch pockets before the first acknowledgement. A locked pocket and an in-raid pouch change were refused.'
Mark 'Five private equipment positions, weapon swapping, armor unequip/equip, a 24/36/54-cell shared inventory and quick keys 1-4 survived real drag and selection actions. An occupied ninth row prevented backpack downsizing. Both backpacks and all four pouch tiers appeared in the test crate.'
Mark 'All four pouch levels were selectable at base and the chosen loadout survived entry into the next raid. The second player kept their own kit.'
foreach ($role in 'A','B') {
    Require ((Read-Marker "$role.motion-ok") -eq 'ok') "$role did not finish crouch, jump and sprint"
    $peer = $id[$role]
    $remote = @($clientLogs[$role] | Where-Object { $_.channel -eq 'event' -and $_.code -eq 1 -and $_.actor -ne $peer -and $_.activeRound -eq 2 })
    Require (@($remote | Where-Object crouch -gt 220).Count -gt 0 -and
        @($remote | Where-Object height -gt .4).Count -gt 0 -and
        @($remote | Where-Object sprint -eq 1).Count -gt 0) "$role did not see the other player's crouch, jump and sprint"
    Require (@($remote | Where-Object slide -eq 1).Count -gt 0) "$role did not see the other player's slide"
}
Mark 'Both players crouched, stood up, jumped, landed and sprinted. Each received the remote stance, airborne height and sprint in normal pose snapshots.'
$poseFile = Join-Path $RunDirectory 'motion/poses.csv'
if (Test-Path -LiteralPath $poseFile) {
    $poses = Import-Csv -LiteralPath $poseFile
    $run = @($poses | Where-Object { [double]$_.time -ge 2.12 -and [double]$_.time -le 2.82 })
    $stance = Read-Marker 'A.motion-contact'
    $flight = Read-Marker 'A.motion-flight'
    Require ($null -ne $stance -and $null -ne $flight -and
        [double]$stance -ge -.045 -and [double]$stance -le .04 -and
        [double]$flight -ge .045 -and [double]$flight -le .35) 'sprint did not alternate ground contact and flight'
    $back = @($poses | Where-Object { [double]$_.time -ge 3.75 -and [double]$_.time -le 4.65 })
    $side = @($poses | Where-Object { [double]$_.time -ge 5.25 -and [double]$_.time -le 5.85 })
    $air = @($poses | Where-Object { [double]$_.rootHeight -gt .55 })
    foreach ($section in @($run.Count, $back.Count, $side.Count, $air.Count)) {
        Require ($section -ge 6) 'recorded motion sequence missed a locomotion phase'
    }
    $runWidth = ($run | ForEach-Object { [Math]::Abs([double]$_.ankleWidth) } | Measure-Object -Maximum).Maximum
    Require ($runWidth -gt .06 -and $runWidth -lt .35) 'sprint feet spread outside the running lane'
    Require (@($poses | Where-Object { [int]$_.sliding -eq 1 }).Count -ge 6) 'recorded runner missed the slide'
    # Actual clavicle/hip axes include the authored idle pose; measure the *change* over the glance.
    $neutral = @($poses | Where-Object { [double]$_.time -ge 9.72 -and [double]$_.time -le 9.85 })
    $glance = @($poses | Where-Object { [double]$_.time -gt 9.85 -and [double]$_.time -le 10.55 })
    Require ($neutral.Count -gt 0 -and $glance.Count -ge 6) 'missing the glance or its reference pose'
    $neutralTwist = ($neutral | ForEach-Object {
        [Math]::IEEERemainder([double]$_.chestHeading - [double]$_.pelvisHeading, 360)
    } | Measure-Object -Average).Average
    $lead = ($glance | ForEach-Object {
        [Math]::IEEERemainder([double]$_.chestHeading - [double]$_.pelvisHeading - $neutralTwist, 360)
    } | Measure-Object -Maximum).Maximum
    Require ($lead -gt 4 -and $lead -lt 22) 'small camera glance did not lead with the upper body'
    Mark "The Starter Assets runner alternated support and flight (sole clearance $stance to $flight m). The gameplay camera recorded travel in multiple directions, the stationary glance and the slide."
}
Require ((Read-Marker 'A.animation-preview-ok') -eq 'ok') 'animation preview did not switch clips and reopen'
Mark 'Esc opened the animation preview. A walk clip could be selected, and reopening the preview started on idle.'
[IO.File]::WriteAllLines((Join-Path $RunDirectory 'acceptance.txt'), [string[]]$checks)
