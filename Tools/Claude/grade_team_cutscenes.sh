#!/bin/bash
# 팀원 제작 탈출 시퀀스 영상(문 파손 연출 1-1 crop, CutScene_김영주)을 B1F 톤으로 색보정한다.
# 사용법: bash Tools/Claude/grade_team_cutscenes.sh "<원본 폴더>" <출력 폴더>
SRC="$1"; OUT="$2"; mkdir -p "$OUT"
# 공통: 그림자는 청록으로, 하이라이트는 차갑게, 대비를 올리고 비네트와 필름 그레인을 넣는다.
COMMON="vignette=PI/4.5,noise=alls=5:allf=t"
# 문 파손 연출: 회녹색 -> 청록 쪽으로. 붉은 경고등은 유지.
ffmpeg -v error -y -i "$SRC/문 파손 연출 1-1 (crop).mp4" -vf "\
eq=contrast=1.06:brightness=0.01:saturation=1.0:gamma=1.12,\
colorbalance=rs=-0.03:gs=0.01:bs=0.07:rm=-0.02:bm=0.03:rh=-0.01:bh=0.02,\
curves=all='0/0.01 0.25/0.25 0.7/0.72 1/0.98',\
$COMMON" -af "alimiter=limit=0.8" -c:v libx264 -crf 20 -maxrate 9M -bufsize 18M -preset medium -pix_fmt yuv420p -c:a aac -b:a 192k "$OUT/Breach1_graded.mp4"
# 김영주: 순색 초록을 청록으로, 빨강은 채도를 살짝 낮춰 비상등 톤으로.
ffmpeg -v error -y -i "$SRC/CutScene_김영주.mp4" -vf "\
colorchannelmixer=bg=0.28:gg=0.95,\
eq=contrast=1.04:brightness=0.02:saturation=0.9:gamma=1.1,\
\
$COMMON" -af "alimiter=limit=0.8" -c:v libx264 -crf 20 -maxrate 9M -bufsize 18M -preset medium -pix_fmt yuv420p -c:a aac -b:a 192k "$OUT/Approach_graded.mp4"

# 장면 전환: 접근 영상은 TV가 사라지는 14.2~15.0초에 글리치를 넣고 암전 후 15.8초에서 끝낸다(원본 뒤 약 3초는 순수한 검은 화면이라 잘라낸다).
# 충격 영상은 0.35초 페이드인 직후 글리치. 사용법: 이 스크립트 3번째 인자로 출력 폴더(Assets/Movies/B1F/EscapeSequence)를 준다.
if [ -n "$3" ]; then
  mkdir -p "$3"
  GL="rgbashift=rh=-16:bh=16:gv=3,noise=alls=70:allf=t,eq=contrast=1.4:brightness=0.04"
  NZ="anoisesrc=d=0.6:c=white:a=0.5"
  ffmpeg -v error -y -t 15.8 -i "$OUT/Approach_graded.mp4" -f lavfi -i "$NZ" -filter_complex "[0:v]split[a][b];[b]$GL[g];[a][g]overlay=enable='between(t,14.2,14.95)*gt(mod(floor(t*30),3),0)':shortest=1,fade=t=out:st=14.7:d=0.5[v];[0:a]afade=t=out:st=14.7:d=0.5[a0];[1:a]afade=t=in:d=0.1,afade=t=out:st=0.4:d=0.2,adelay=14200|14200,volume=0.5[n];[a0][n]amix=inputs=2:duration=first:normalize=0,alimiter=limit=0.8[aout]"     -map "[v]" -map "[aout]" -c:v libx264 -crf 20 -maxrate 9M -bufsize 18M -pix_fmt yuv420p -c:a aac -b:a 192k "$3/Escape_Approach.mp4"
  ffmpeg -v error -y -i "$OUT/Breach1_graded.mp4" -f lavfi -i "$NZ" -filter_complex "[0:v]split[a][b];[b]$GL[g];[a][g]overlay=enable='between(t,0.3,0.85)*gt(mod(floor(t*30),3),0)':shortest=1,fade=t=in:st=0:d=0.35[v];[0:a]afade=t=in:st=0:d=0.35[a0];[1:a]afade=t=in:d=0.1,afade=t=out:st=0.4:d=0.2,adelay=300|300,volume=0.5[n];[a0][n]amix=inputs=2:duration=first:normalize=0,alimiter=limit=0.8[aout]"     -map "[v]" -map "[aout]" -c:v libx264 -crf 20 -maxrate 9M -bufsize 18M -pix_fmt yuv420p -c:a aac -b:a 192k "$3/Escape_Impact.mp4"
fi
