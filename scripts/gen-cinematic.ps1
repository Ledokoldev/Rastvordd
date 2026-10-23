# Референсы для заставки спуска в «Объект 42» (видео по кадрам): реалистичные картинки через Codex (gpt-image), не пиксель-арт.
# Этап 1 — опорные (бойцы, будка лифта), этап 2 — стартовые кадры 4 сцен с опорными как образцом.
# Запуск: powershell -File scripts/gen-cinematic.ps1 [-Stage 1|2|0] (0 — оба)   → docs/cinematic/*.png
param([int]$Stage = 0, [int]$Parallel = 4, [string[]]$Only = @(), [int]$Variants = 1, [string]$Tag = 'v', [switch]$NoWait)   # -NoWait: кадры не ждут опорных (берут уже готовые)   # -Only id1,id2 -Variants 2 → id_v1, id_v2

$root = Split-Path -Parent $PSScriptRoot
$codex = Get-ChildItem "C:\Program Files\WindowsApps" -Directory -Filter "OpenAI.Codex_*" -ErrorAction SilentlyContinue |
    Sort-Object Name -Descending | ForEach-Object { Join-Path $_.FullName "app\resources\codex.exe" } |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $codex) { throw "codex.exe не найден" }
$out = Join-Path $root "docs\cinematic"
New-Item -ItemType Directory -Force $out, (Join-Path $out "_logs") | Out-Null

$STYLE = "Photorealistic cinematic film still, 35mm anamorphic lens, desaturated cold color grade with deep blacks, film grain, gritty, realistic materials, no text, no logos, no watermark."
$SOBR = "Soldier A: Russian SOBR special forces operator in full heavy armor - dark grey-black tactical uniform, black plate carrier with ceramic plates and magazine pouches, black balaclava, titanium Altyn-style ballistic helmet with the armored visor raised, knee pads, gloves; carries an AK-74M rifle with a suppressor and an underbarrel tactical flashlight."
$OZK = "Soldier B: Soviet hazmat trooper in an olive-green rubberized OZK L-1 protective suit with hood, GP-5 gas mask with round glass eyepieces and a side filter canister, rubber gloves and boots, a canvas chest rig over the suit, a dosimeter on the chest; carries an AKM rifle with an underbarrel tactical flashlight."
$PLACE = "Setting: abandoned Soviet top-secret underground command bunker 'Object 42', 1980s, raw concrete, peeling green-grey paint, rust, cable trays, hazard stripes, dim red emergency lights, dust particles hanging in the air."
$CAB = "The elevator cabin interior: a HUGE industrial Soviet freight elevator cage, like a mine-shaft cage, about 6 m wide, 5 m deep and 4 m high, big enough for a truck, two soldiers look small inside it; heavy riveted steel girders and diagonal bracing, steel mesh side walls through which the rough concrete shaft wall slides past, yellow-black hazard stripes on the beams and the floor edge, several red emergency lamps in cages hanging from the high ceiling beams, thick steel cables above, a big control panel with round buttons and a dial on a pillar, massive double sliding steel doors in front."
$CAB2 = "The elevator cabin interior: a big enclosed Soviet freight elevator, about 5 m wide, 4 m deep and 3.5 m high, room for 12 people, clearly much taller and wider than the soldiers; riveted grey steel walls with mesh panels and vertical girders, yellow-black hazard stripes on the wide door frame, three caged red emergency lamps on the ceiling, a control panel with round buttons, wide double sliding steel doors in front."

$jobs = @(
    @{ id = 'ref_sobr'; stage = 1; ref = @(); img = "Character reference sheet on a plain neutral grey studio background, full body, two views side by side: front view and back view, even soft studio light. $SOBR $STYLE" },
    @{ id = 'ref_ozk'; stage = 1; ref = @(); img = "Character reference sheet on a plain neutral grey studio background, full body, two views side by side: front view and back view, even soft studio light. $OZK $STYLE" },
    @{ id = 'ref_lift_booth'; stage = 1; ref = @('game\art\concept\o42_lift_n2.png'); img = "Realistic architectural photo of the surface elevator head-house of a secret Soviet bunker, design based on the attached concept but MUCH bigger and more massive: a monumental two-storey reinforced concrete blockhouse about 12 m wide and 8 m tall, thick sloped concrete walls with deep recesses, a heavy steel headframe with a large cable winch and pulley wheel on top, a big faded red star on the facade above the entrance, loudspeaker horns and amber warning lamps on all four corners, hazard stripes, huge double steel blast doors with yellow-black striped jambs, ventilation grilles, stairs and a concrete ramp to the doors. Strictly level horizon, straight vertical lines, no lens distortion, eye-level three-quarter view from 25 m. Overcast dusk, wet ground, birch forest behind. $STYLE" },
    @{ id = 'shot1_lift_descent'; stage = 2; ref = @('ref_sobr', 'ref_ozk'); img = "Landscape 3:2 frame. FIRST FRAME of a video clip, the moment before the action: inside the elevator cabin at the very start of the descent, both soldiers stand still side by side facing the camera, rifles held low and relaxed across the chest, hands on the rifles, heads slightly lowered, calm and tense; nothing has happened yet. Medium shot from the front, red emergency lamp light from above. $CAB $SOBR $OZK $STYLE" },
    @{ id = 'shot2_doors_back'; stage = 2; ref = @('ref_sobr', 'ref_ozk'); img = "Landscape 3:2 frame. FIRST FRAME of a video clip, the moment before the action: wide shot from the back of the elevator cabin, camera low and far behind the two soldiers so both are visible FULL BODY from helmets to boots with floor space around them; they stand facing the double steel doors which are still CLOSED, only a thin crack of darkness between them, rifles held at low ready. The whole cabin interior is visible: walls, ceiling with red lamps, floor. $CAB2 $SOBR $OZK $PLACE $STYLE" },
    @{ id = 'shot3_side_signal'; stage = 2; ref = @('ref_sobr', 'ref_ozk'); img = "Landscape 3:2 frame. SIDE VIEW ONLY: the camera stands to the side of the soldiers at 90 degrees, like a 2D side-scroller shot; we see both soldiers in full PROFILE, both facing RIGHT, walking from left to right. On the left edge of the frame the huge open elevator doors with red light inside; on the right, a dark concrete corridor. Both underbarrel flashlights are switched on, two bright white beams point to the right through dusty air. Soldier A (front, closer to the right) raises his left arm, elbow bent at 90 degrees, hand forming a finger-gun pointing forward - the military 'move forward' signal; Soldier B behind him turns his head toward him. Do NOT show them from behind. $SOBR $OZK $PLACE $STYLE" },
    @{ id = 'shot4_into_dark'; stage = 2; ref = @('ref_sobr', 'ref_ozk'); img = "Landscape 3:2 frame. FIRST FRAME of a video clip, the moment before the action: wide shot from behind, almost pitch-black long concrete corridor of the bunker, only a few small dim red emergency lamps far away along the ceiling; the two soldiers stand close to the camera in the foreground (about 3-4 m away), seen from behind as dark silhouettes, flashlights on, two white beams pointing down the corridor, about to start walking. 75 percent of the frame is black. $SOBR $OZK $STYLE" }
)

$running = @()
$all = @()
foreach ($j in $jobs) {
    if ($Only.Count -gt 0 -and $Only -notcontains $j.id) { continue }
    if ($Variants -le 1) { $all += $j; continue }
    for ($v = 1; $v -le $Variants; $v++) { $c = $j.Clone(); $c.id = "$($j.id)_$Tag$v"; $all += $c }
}
foreach ($j in $all) {
    if ($Stage -ne 0 -and $j.stage -ne $Stage) { continue }
    $rel = "docs/cinematic/$($j.id).png"
    if (Test-Path (Join-Path $root $rel)) { Write-Host "есть: $($j.id)"; continue }
    if ($Stage -eq 0 -and $j.stage -eq 2 -and $Only.Count -eq 0 -and -not $NoWait) { $running | ForEach-Object { $_.WaitForExit() }; $running = @() }   # этап 2 — после опорных
    while (($running | Where-Object { -not $_.HasExited }).Count -ge $Parallel) { Start-Sleep -Milliseconds 500 }
    $refs = @()
    foreach ($r in $j.ref) { $p = if ($r -like '*\*') { Join-Path $root $r } else { Join-Path $out "$r.png" }; if (Test-Path $p) { $refs += $p } }
    $prompt = "Generate ONE image with your image generation tool and save it as a PNG file at $rel (relative to the current directory), overwriting if it exists. Do not write any code or edit any other files.$(if ($refs.Count) { ' Use the attached image(s) as the reference for the characters / design so they look the same.' })`n`nImage: $($j.img)`n`nWhen done, reply with just the saved file path."
    $pf = Join-Path $out "_logs\$($j.id).prompt.txt"
    Set-Content -Path $pf -Value $prompt -Encoding UTF8
    $args2 = @("exec", "-C", "`"$root`"", "-s", "workspace-write", "-c", "model_reasoning_effort=`"low`"", "-")
    if ($refs.Count) { $args2 += "-i"; $args2 += ($refs | ForEach-Object { "`"$_`"" }) }
    $p = Start-Process -FilePath $codex -ArgumentList $args2 -RedirectStandardInput $pf `
        -RedirectStandardOutput (Join-Path $out "_logs\$($j.id).log") -RedirectStandardError (Join-Path $out "_logs\$($j.id).err") -NoNewWindow -PassThru
    $running += $p
    Write-Host "→ $($j.id)"
}
$running | ForEach-Object { $_.WaitForExit() }
Get-ChildItem $out -Filter *.png | ForEach-Object { Write-Host "готово: $($_.Name) $([math]::Round($_.Length / 1KB)) КБ" }
