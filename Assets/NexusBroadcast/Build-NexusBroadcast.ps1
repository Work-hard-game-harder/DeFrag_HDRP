param(
    [string]$OutputPath = (Join-Path $PSScriptRoot "Nexus_Public_Service_Broadcast.mp4")
)

$ErrorActionPreference = "Stop"
$ffmpeg = (Get-Command ffmpeg -ErrorAction Stop).Source
$background = Join-Path $PSScriptRoot "Nexus_Lab_Background.png"
$font = "C\:/Windows/Fonts/malgun.ttf"
$boldFont = "C\:/Windows/Fonts/malgunbd.ttf"
$work = Join-Path $env:TEMP ("nexus-broadcast-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $work | Out-Null

function Render-StillScene([string]$name, [double]$duration, [string]$filter) {
    $path = Join-Path $work "$name.mp4"
    & $ffmpeg -hide_banner -loglevel error -y -loop 1 -i $background -t $duration -vf $filter -r 24 -an -c:v libx264 -preset medium -crf 18 -pix_fmt yuv420p $path
    if ($LASTEXITCODE -ne 0) { throw "Failed rendering $name" }
    return $path
}

function Render-GeneratedScene([string]$name, [double]$duration, [string]$source, [string]$filter) {
    $path = Join-Path $work "$name.mp4"
    & $ffmpeg -hide_banner -loglevel error -y -f lavfi -i $source -t $duration -vf $filter -r 24 -an -c:v libx264 -preset medium -crf 18 -pix_fmt yuv420p $path
    if ($LASTEXITCODE -ne 0) { throw "Failed rendering $name" }
    return $path
}

$base = "scale=1344:756,crop=1280:720:x='32+10*sin(t*0.22)':y='18+6*cos(t*0.19)',eq=contrast=1.03:saturation=0.72"
$common = "drawbox=x=0:y=0:w=iw:h=46:color=0xEFF8FB@0.92:t=fill,drawtext=fontfile='$font':text='NEXUS 공익 정보망':x=42:y=12:fontsize=20:fontcolor=0x16313A,drawtext=fontfile='$font':text='AI · BIG DATA · PUBLIC TRUST':x=w-tw-42:y=13:fontsize=15:fontcolor=0x56727A"

$clips = @()
$clips += Render-StillScene "01_bait" 3.2 "$base,drawbox=x=72:y=445:w=780:h=190:color=white@0.88:t=fill,drawbox=x=72:y=445:w=7:h=190:color=0xF2B84B:t=fill,drawtext=fontfile='$boldFont':text='시스템은 안전합니다':x=110:y=482:fontsize=48:fontcolor=0x18353E,drawtext=fontfile='$font':text='모든 이상 신호는 정상 범위로 확인되었습니다':x=112:y=552:fontsize=25:fontcolor=0x46646C,drawtext=fontfile='$font':text='별도의 대피 조치는 필요하지 않습니다':x=112:y=592:fontsize=20:fontcolor=0x789096,$common"

$noiseSource = "color=c=0x7F7F7F:s=1280x720:r=24"
$noiseFilter = "noise=alls=100:allf=t+u,eq=contrast=2.4:brightness=-0.08:saturation=0,drawbox=x=0:y='mod(t*620\,720)':w=1280:h=10:color=white@0.62:t=fill,drawbox=x=0:y='mod(t*947+310\,720)':w=1280:h=4:color=black@0.9:t=fill"
$clips += Render-GeneratedScene "02_noise" 0.8 $noiseSource $noiseFilter

$clips += Render-StillScene "03_normal" 4.8 "$base,drawbox=x=115:y=420:w=1050:h=220:color=0xF8FCFD@0.91:t=fill,drawtext=fontfile='$boldFont':text='더 나은 내일을 계산합니다':x=(w-tw)/2:y=455:fontsize=51:fontcolor=0x163A45,drawtext=fontfile='$font':text='넥서스는 모두를 위한 안전한 지능을 연구합니다':x=(w-tw)/2:y=530:fontsize=27:fontcolor=0x3E6872,drawbox=x=450:y=590:w=380:h=3:color=0x67D7E5:t=fill,drawtext=fontfile='$font':text='NEXUS  /  공공 AI 빅데이터 연구소':x=(w-tw)/2:y=605:fontsize=18:fontcolor=0x75969D,$common"

$clips += Render-GeneratedScene "04_noise" 0.8 $noiseSource $noiseFilter

$clips += Render-StillScene "05_bait" 3.0 "$base,drawbox=x=735:y=112:w=475:h=510:color=white@0.9:t=fill,drawtext=fontfile='$font':text='시민 안정 지수':x=775:y=155:fontsize=22:fontcolor=0x58747C,drawtext=fontfile='$boldFont':text='99.98%':expansion=none:x=775:y=205:fontsize=76:fontcolor=0x2A6974,drawbox=x=780:y=315:w=340:h=12:color=0xDCEAED:t=fill,drawbox=x=780:y=315:w=336:h=12:color=0x69D2DD:t=fill,drawtext=fontfile='$font':text='위험 요소 감지':x=775:y=372:fontsize=20:fontcolor=0x607C83,drawtext=fontfile='$boldFont':text='0건':x=1090-tw:y=364:fontsize=34:fontcolor=0x2A6974,drawtext=fontfile='$font':text='NEXUS가 당신의 일상을':x=775:y=485:fontsize=26:fontcolor=0x294B53,drawtext=fontfile='$boldFont':text='항상 보호합니다':x=775:y=527:fontsize=34:fontcolor=0x294B53,$common"

$disconnectSource = "color=c=0x111719:s=1280x720:r=24"
$disconnectFilter = "drawbox=x=0:y=0:w=iw:h=ih:color=0xEAF0F1@0.05:t=fill,drawbox=x=470:y=212:w=340:h=226:color=0x1B2528:t=3,drawbox=x=620:y=270:w=40:h=40:color=0xD9E5E7:t=3,drawtext=fontfile='$boldFont':text='신호 연결 끊김':x=(w-tw)/2:y=475:fontsize=40:fontcolor=0xE3ECEE,drawtext=fontfile='$font':text='NEXUS RELAY 07  /  재연결 시도 중':x=(w-tw)/2:y=535:fontsize=18:fontcolor=0x7C9298,drawtext=fontfile='$font':text='NO SIGNAL':x=36:y=34:fontsize=16:fontcolor=0xB7C6CA"
$clips += Render-GeneratedScene "06_disconnect" 2.5 $disconnectSource $disconnectFilter
$clips += Render-GeneratedScene "07_noise" 1.0 $noiseSource "$noiseFilter,fade=t=out:st=0.72:d=0.28"

$concat = Join-Path $work "concat.txt"
$clips | ForEach-Object { "file '$($_.Replace("'", "''"))'" } | Set-Content -Encoding ascii $concat
& $ffmpeg -hide_banner -loglevel error -y -f concat -safe 0 -i $concat -c copy -movflags +faststart $OutputPath
if ($LASTEXITCODE -ne 0) { throw "Failed concatenating broadcast" }

Remove-Item -LiteralPath $work -Recurse -Force
Write-Output $OutputPath




