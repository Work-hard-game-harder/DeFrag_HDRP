# -*- coding: utf-8 -*-
"""Renders the game-start prologue cinematic ("1막 - PROLOGUE: 넥서스의 흥망") into one movie file.

Sources (Artlist): storyboard cuts, three Veo shots, Lyria score and English VO, all under
Assets/Art/Prologue/Source~ and Assets/SoundSources/Prologue. Everything else (camera moves,
Korean captions and subtitles, CCTV overlays, newspaper / dark-web cards, grade, grain, mix) is
done here with ffmpeg so the timing is exact and the result plays as a single VideoClip.

Usage:  python Tools/Claude/render_prologue.py [--stills t1,t2,...]
Output: Assets/Resources/Prologue/Prologue_Cinematic.mp4  (1920x1080, 30 fps, H.264 Baseline, AAC)
"""
import os
import shutil
import subprocess
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
SRC = os.path.join(ROOT, 'Assets', 'Art', 'Prologue', 'Source~')
CUTS = os.path.join(SRC, 'v6_cuts')
VID = os.path.join(SRC, 'video_raw')
SND = os.path.join(ROOT, 'Assets', 'SoundSources', 'Prologue')
RAW_AUDIO = os.path.join(SRC, 'audio_raw')
WORK = os.path.join(SRC, 'build')
OUT_DIR = os.path.join(ROOT, 'Assets', 'Resources', 'Prologue')  # loaded at runtime by GamePrologueCinematic
OUT = os.path.join(OUT_DIR, 'Prologue_Cinematic.mp4')
FFMPEG = shutil.which('ffmpeg') or r'C:\ffmpeg\ffmpeg\bin\ffmpeg.exe'
FPS = 30
W, H = 1920, 1080
BAR = 60  # 2.0:1 letterbox
SLOW = 2.0            # slow-motion factor for the frozen moment before the researcher is taken
LIFT = 1.25 * SLOW + 0.13  # local second at which he is lifted (after the lights-out gap)

FONTS = {
    'sans': r'C:\Windows\Fonts\malgun.ttf',
    'sansb': r'C:\Windows\Fonts\malgunbd.ttf',
    'serif': r'C:\Windows\Fonts\HANBatang.ttf',
    'mono': r'C:\Windows\Fonts\consola.ttf',
}


def run(args):
    print('>', ' '.join(a if len(a) < 120 else a[:117] + '...' for a in args[:12]))
    subprocess.run([FFMPEG, '-v', 'error', '-y'] + args, cwd=WORK, check=True)


_text_id = [0]


def txt(s):
    """Writes text to a file for drawtext's textfile= (avoids filter escaping of Korean/punctuation)."""
    _text_id[0] += 1
    name = 't%03d.txt' % _text_id[0]
    with open(os.path.join(WORK, name), 'w', encoding='utf-8') as f:
        f.write(s)
    return name


def dt(text, font='sans', size=40, color='white', x='(w-text_w)/2', y='(h-text_h)/2', enable=None,
       alpha=None, border=0, shadow=2, box=None, expand=False):
    parts = ["drawtext=fontfile=%s" % font, "textfile=%s" % txt(text), "fontsize=%d" % size,
             "fontcolor=%s" % color, "x=%s" % x, "y=%s" % y, "shadowx=%d" % shadow, "shadowy=%d" % shadow,
             "shadowcolor=black@0.7"]
    if not expand:
        parts.append('expansion=none')
    if border:
        parts.append('borderw=%d:bordercolor=black@0.6' % border)
    if box:
        parts.append('box=1:boxcolor=%s:boxborderw=%d' % box)
    if alpha:
        parts.append("alpha='%s'" % alpha)
    if enable:
        parts.append("enable='%s'" % enable)
    return ':'.join(parts)


def fade_alpha(t0, t1, fin=0.35, fout=0.35):
    return 'if(lt(t,%.3f),0,if(lt(t,%.3f),(t-%.3f)/%.3f,if(lt(t,%.3f),1,if(lt(t,%.3f),(%.3f-t)/%.3f,0))))' % (
        t0, t0 + fin, t0, fin, t1 - fout, t1, t1, fout)


# ---------------------------------------------------------------- stills prep

def prep_still(cut, out, fix=None):
    """Crops the storyboard gutter, applies a local fix, upscales to 3840x2160 (16:9 cover)."""
    src = os.path.join(CUTS, 'Cut%02d.jpg' % cut)
    chain = 'crop=1352:744:8:6'
    if fix:
        chain = fix
    chain += ',scale=-2:2160:flags=lanczos,crop=3840:2160'
    run(['-i', src, '-filter_complex', chain, out])


def prep_stills():
    prep_still(1, 's01.png')
    prep_still(2, 's02.png')
    prep_still(3, 's03.png')
    # Ad: the hologram family photo shows faces -> dreamy blur over the photo area.
    prep_still(4, 's04.png', "crop=1352:744:8:6,split[a][b];[b]crop=470:350:375:76,boxblur=18:2,"
                             "eq=brightness=0.06:saturation=0.7[p];[a][p]overlay=375:76")
    prep_still(5, 's05.png')
    # Elevator: blank the original floor digit, the CCTV-style counter is drawn later.
    prep_still(6, 's06.png', "crop=1352:744:8:6,drawbox=x=588:y=48:w=172:h=40:color=0x140606:t=fill")
    prep_still(7, 's07.png')
    prep_still(8, 's08.png')
    prep_still(10, 's10.png')
    # Doppelganger: the copy's face goes into deep shadow.
    prep_still(11, 's11.png', "crop=1352:744:8:6,split[a][b];[b]boxblur=20:3,eq=brightness=-0.34,format=yuva420p[bl];"
                              "nullsrc=s=1352x744,format=gray,geq=lum='255*exp(-(pow((X-915)/62,2)+pow((Y-300)/80,2)))'[m];"
                              "[bl][m]alphamerge[p];[a][p]overlay=0:0:shortest=1")
    prep_still(12, 's12.png')
    prep_still(16, 's16.png')


# ---------------------------------------------------------------- segments

def still_seg(name, img, dur, z0, z1, x0=0.5, y0=0.5, x1=None, y1=None, post=''):
    """Ken Burns on a 4K still. x/y = zoom centre in 0..1 of the image, interpolated over the shot."""
    n = int(round(dur * FPS))
    x1 = x0 if x1 is None else x1
    y1 = y0 if y1 is None else y1
    p = 'on/%d' % max(1, n - 1)
    z = '%f+(%f)*%s' % (z0, z1 - z0, p)
    cx = '(%f+(%f)*%s)' % (x0, x1 - x0, p)
    cy = '(%f+(%f)*%s)' % (y0, y1 - y0, p)
    vf = ("zoompan=z='%s':x='iw*%s-iw/zoom/2':y='ih*%s-ih/zoom/2':d=%d:s=%dx%d:fps=%d,format=yuv420p"
          % (z, cx, cy, n, W, H, FPS))
    if post:
        vf += ',' + post
    run(['-i', img, '-vf', vf, '-frames:v', str(n), '-c:v', 'libx264', '-crf', '12', '-preset', 'fast',
         name])


def color_seg(name, dur, color='black', post=''):
    vf = 'format=yuv420p' + (',' + post if post else '')
    run(['-f', 'lavfi', '-i', 'color=c=%s:s=%dx%d:r=%d:d=%.3f' % (color, W, H, FPS, dur), '-vf', vf,
         '-c:v', 'libx264', '-crf', '12', '-preset', 'fast', name])


def video_seg(name, src, vf_core):
    run(['-i', src, '-filter_complex', vf_core, '-an', '-c:v', 'libx264', '-crf', '12', '-preset', 'fast',
         '-r', str(FPS), name])


GRADE_2043 = 'eq=contrast=1.04:saturation=1.08,colorbalance=rs=0.05:gs=0.015:bs=-0.05:rh=0.03:bh=-0.03'
GRADE_2045 = 'eq=contrast=1.1:saturation=0.78:brightness=-0.02,colorbalance=rs=-0.03:bs=0.06:rh=-0.02:bh=0.04'
GRADE_DARK = 'eq=contrast=1.15:saturation=0.85:brightness=-0.03,colorbalance=bs=0.05'
GRADE_CCTV = 'eq=contrast=1.12:saturation=0.75,noise=alls=16:allf=t,drawgrid=w=iw:h=3:t=1:c=black@0.22'


def cctv_overlay(cam, date, clock_base, t_offset, extra_enable='1'):
    """Camera label, REC dot and a running clock. clock_base = (hh, mm, ss) at local t=t_offset."""
    hh, mm, ss = clock_base
    clock = "%s  %02d:%02d:%%{eif:mod(%d+t-%f,60):d:2}" % (date, hh, mm, ss, t_offset)
    return ','.join([
        dt(cam, font='mono', size=30, color='white@0.9', x='70', y='%d' % (BAR + 34), shadow=1),
        dt('REC', font='mono', size=30, color='white@0.9', x='w-300', y='%d' % (BAR + 34), shadow=1),
        "drawbox=x=iw-336:y=%d:w=20:h=20:color=red@0.9:t=fill:enable='lt(mod(t,1),0.6)'" % (BAR + 40),
        dt(clock, font='mono', size=30, color='white@0.9', x='70', y='h-%d' % (BAR + 70), shadow=1,
           expand=True),
    ])


def build_segments():
    segs = []

    def add(name, dur):
        segs.append((name, dur))

    # 0  Opening: black (captions drawn in the global pass)
    color_seg('g00.mp4', 6.5)
    add('g00.mp4', 6.5)
    # 1-4  2043 bright news / ad
    still_seg('g01.mp4', 's01.png', 3.0, 1.0, 1.10, 0.5, 0.56, 0.5, 0.48, GRADE_2043)
    add('g01.mp4', 3.0)
    still_seg('g02.mp4', 's02.png', 2.5, 1.06, 1.16, 0.5, 0.42, post=GRADE_2043)
    add('g02.mp4', 2.5)
    flashes = '+'.join("between(t,%.2f,%.2f)" % (a, a + 0.07) for a in (0.45, 0.95, 1.7, 2.3, 2.9))
    still_seg('g03.mp4', 's03.png', 3.5, 1.04, 1.12, 0.5, 0.5,
              post=GRADE_2043 + ",eq=brightness=0.35:enable='%s'" % flashes)
    add('g03.mp4', 3.5)
    still_seg('g04.mp4', 's04.png', 7.0, 1.0, 1.14, 0.5, 0.5, 0.52, 0.6,
              post=GRADE_2043 + ',eq=gamma=1.05')
    add('g04.mp4', 7.0)
    # 5-9  2045: lobby, elevator B1->B5, server hall, androids, eye (video)
    still_seg('g05.mp4', 's05.png', 2.5, 1.02, 1.09, 0.5, 0.5, post=GRADE_2045)
    add('g05.mp4', 2.5)
    floors = []
    for i, (a, b) in enumerate([(0.0, 0.55), (0.55, 1.05), (1.05, 1.5), (1.5, 1.95), (1.95, 3.0)]):
        floors.append(dt('B%d' % (i + 1), font='mono', size=46, color='0xFF3B2E', x='(w-text_w)/2+2',
                         y='76', shadow=0, enable='between(t,%.2f,%.2f)' % (a, b)))
    still_seg('g06.mp4', 's06.png', 3.0, 1.0, 1.0, 0.5, 0.5,
              post=GRADE_2045 + ',' + ','.join(floors))
    add('g06.mp4', 3.0)
    still_seg('g07.mp4', 's07.png', 2.5, 1.0, 1.14, 0.5, 0.52, post=GRADE_DARK)
    add('g07.mp4', 2.5)
    still_seg('g08.mp4', 's08.png', 2.5, 1.12, 1.12, 0.40, 0.55, 0.60, 0.55, post=GRADE_DARK)
    add('g08.mp4', 2.5)
    video_seg('g09.mp4', os.path.join(VID, 'V09_AndroidEye.mp4'),
              'scale=%d:%d:flags=lanczos,%s,format=yuv420p' % (W, H, GRADE_DARK))
    add('g09.mp4', 4.0)
    # 10-13  Crime montage
    still_seg('g10.mp4', 's10.png', 2.3, 1.0, 1.08, 0.5, 0.5, post=GRADE_DARK + ',' + ','.join([
        "drawbox=x=0:y=ih-%d:w=iw:h=96:color=black@0.72:t=fill" % (BAR + 250),
        "drawbox=x=70:y=ih-%d:w=120:h=64:color=0xC8102E:t=fill" % (BAR + 234),
        dt('속보', font='sansb', size=38, color='white', x='88', y='h-%d' % (BAR + 228), shadow=0),
        dt('전 국회의원 K씨, 자신이 아닌 영상으로 정치 생명 끝', font='sansb', size=40, color='white',
           x='220', y='h-%d' % (BAR + 230), shadow=1),
    ]))
    add('g10.mp4', 2.3)
    still_seg('g11.mp4', 's11.png', 2.0, 1.05, 1.1, 0.55, 0.5,
              post='hue=s=0,' + GRADE_CCTV + ',' + dt('CAM 03   MAPO-GU  2047.02.08  21:13', font='mono',
                                                       size=30, color='white@0.9', x='70', y='%d' % (BAR + 34),
                                                       shadow=1))
    add('g11.mp4', 2.0)
    still_seg('g12.mp4', 's12.png', 1.7, 1.03, 1.08, 0.6, 0.5, post=GRADE_DARK + ',' + ','.join([
        "drawbox=x=120:y=330:w=820:h=210:color=0x0E1116@0.82:t=fill",
        "drawbox=x=120:y=330:w=6:h=210:color=0x5A8DEE:t=fill",
        dt('익명 게시판  ·  피해 제보', font='sans', size=28, color='0x9AA4B2', x='160', y='360', shadow=0),
        dt('"제 딸 얼굴이… 그런 영상에…"', font='sansb', size=46, color='white', x='160', y='430', shadow=0),
    ]))
    add('g12.mp4', 1.7)
    color_seg('g12b.mp4', 1.0, '0x030805', post=','.join([
        dt('darkmarket://faceswap   [LIVE 214 users]', font='mono', size=34, color='0x3CFF7A', x='160', y='380',
           shadow=0),
        dt('실시간 페이스 스왑 — 회당 ₩2,400,000', font='sansb', size=62, color='0x3CFF7A', x='160', y='460',
           shadow=0),
        dt('> upload target face_', font='mono', size=34, color='0x3CFF7A@0.8', x='160', y='580', shadow=0),
        'noise=alls=14:allf=t',
        "rgbashift=rh=6:bh=-6:enable='lt(mod(t,0.33),0.08)'",
    ]))
    add('g12b.mp4', 1.0)
    # Newspaper front page: slams in with a white flash.
    paper = ','.join([
        'noise=alls=6:allf=0',
        dt('일 간  시 사', font='serif', size=96, color='0x1A1A1A', x='(w-text_w)/2', y='120', shadow=0),
        "drawbox=x=120:y=250:w=1680:h=4:color=0x1A1A1A:t=fill",
        dt('2047년 3월 14일 금요일', font='serif', size=30, color='0x333333', x='130', y='265', shadow=0),
        dt('제 21,457호', font='serif', size=30, color='0x333333', x='w-330', y='265', shadow=0),
        "drawbox=x=120:y=312:w=1680:h=2:color=0x1A1A1A:t=fill",
        dt('넥서스, 압수수색', font='sansb', size=150, color='0x111111', x='(w-text_w)/2', y='340', shadow=0),
        dt('— 임원 전원 잠적', font='sansb', size=110, color='0x111111', x='(w-text_w)/2', y='510', shadow=0),
        dt('딥페이크 피해자 1,200명… 경찰 "거대 범죄조직 배후" 수사 착수', font='serif', size=40,
           color='0x2A2A2A', x='(w-text_w)/2', y='660', shadow=0),
    ] + ["drawbox=x=%d:y=%d:w=%d:h=10:color=0x8A8578:t=fill" % (130 + c * 560, 730 + r * 24, 500 - (r % 3) * 30)
         for c in range(3) for r in range(10)])
    run(['-f', 'lavfi', '-i', 'color=c=0xECE6D6:s=3840x2160:d=1', '-vf',
         'scale=%d:%d,%s,scale=3840:2160' % (W, H, paper), '-frames:v', '1', 's13.png'])
    n = int(3.6 * FPS)
    run(['-i', 's13.png', '-vf',
         ("zoompan=z='if(lt(on,4),1.32-0.30*on/4,1.02+0.06*(on-4)/%d)':x='iw/2-iw/zoom/2':y='ih/2-ih/zoom/2'"
          ":d=%d:s=%dx%d:fps=%d,format=yuv420p,fade=t=in:st=0:d=0.3:color=white,"
          "vignette=PI/4" % (n - 4, n, W, H, FPS)),
         '-frames:v', str(n), '-c:v', 'libx264', '-crf', '12', '-preset', 'fast', 'g13.mp4'])
    add('g13.mp4', 3.6)
    # 14  CCTV: researchers running (video, own sound)
    video_seg('g14.mp4', os.path.join(VID, 'V13_Running.mp4'),
              'scale=%d:%d:flags=lanczos,%s,%s,format=yuv420p' % (
                  W, H, GRADE_CCTV, cctv_overlay('CAM 07   B5  WEST CORRIDOR', '2047.03.13', (23, 47, 2), 0)))
    add('g14.mp4', 4.0)
    # 15  The researcher is taken. His frozen moment plays in slow motion while darkness creeps down from
    #     the ceiling, the lights die, then the lift is sped up, smeared and pulled up out of frame so it
    #     reads as being dragged upward rather than jumping.
    grad = 'gradient.png'
    run(['-f', 'lavfi', '-i', 'color=c=black:s=%dx900:d=1' % W, '-vf',
         "format=rgba,geq=r=0:g=0:b=0:a='255*pow(1-Y/H,1.6)'", '-frames:v', '1', grad])
    src = os.path.join(VID, 'V14_Taken.mp4')
    core = (
        "[0:v]scale=%d:%d:flags=lanczos,setsar=1,format=yuv420p,split=2[v2][v3];"
        "[v2]trim=0:1.25,setpts=(PTS-STARTPTS)*%.3f[a];"
        "[v3]trim=2.0:3.6,setpts=(PTS-STARTPTS)/1.5,tmix=frames=4:weights='1 1 1 1',"
        "crop=iw*0.78:ih*0.78:iw*0.11:'ih*0.22*min(1,t/0.9)',scale=%d:%d,setsar=1,"
        "rgbashift=rv=-8:bv=8,eq=brightness=-0.05[c];"
        "color=c=black:s=%dx%d:r=%d:d=0.13,format=yuv420p,setsar=1[blk];"
        "[a][blk][c]concat=n=3:v=1:a=0,fps=%d[seq];"
        "[1:v]loop=-1:1:0,setpts=N/%d/TB[g];"
        "[seq][g]overlay=0:'-900+min(720,280*t)':shortest=1,"
        "crop=iw-24:ih-24:'12+8*sin(t*47)*gte(t,%.2f)':'12+10*cos(t*53)*gte(t,%.2f)',scale=%d:%d,"
        "%s,%s,format=yuv420p" % (
            W, H, SLOW, W, H, W, H, FPS, FPS, FPS, LIFT, LIFT, W, H, GRADE_CCTV,
            cctv_overlay('CAM 07   B5  WEST CORRIDOR', '2047.03.13', (23, 47, 9), 0)))
    run(['-i', src, '-i', grad, '-filter_complex', core, '-an', '-c:v', 'libx264', '-crf', '12',
         '-preset', 'fast', '-r', str(FPS), 'g15.mp4'])
    add('g15.mp4', LIFT + 1.6 / 1.5)
    # 16  Signal lost
    run(['-f', 'lavfi', '-i', 'nullsrc=s=%dx%d:r=%d:d=0.8' % (W, H, FPS), '-vf',
         "geq=lum='random(1)*255':cb=128:cr=128,format=yuv420p," +
         dt('NO SIGNAL', font='mono', size=54, color='white', shadow=2, box=('black@0.6', 18)),
         '-c:v', 'libx264', '-crf', '12', '-preset', 'fast', 'g16.mp4'])
    add('g16.mp4', 0.8)
    # 17  Same corridor, empty
    still_seg('g17.mp4', 's16.png', 5.0, 1.0, 1.08, 0.5, 0.55,
              post=GRADE_CCTV + ',' + cctv_overlay('CAM 07   B5  WEST CORRIDOR', '2047.03.13', (23, 49, 31), 0))
    add('g17.mp4', 5.0)
    # 18  End card
    color_seg('g18.mp4', 6.6)
    add('g18.mp4', 6.6)
    return segs


# ---------------------------------------------------------------- global pass

def subtitle(text, t0, t1, speaker=None):
    full = ('%s  |  %s' % (speaker, text)) if speaker else text
    return dt(full, font='sans', size=36, color='white', x='(w-text_w)/2', y='h-%d' % (BAR + 78),
              alpha=fade_alpha(t0, t1, 0.2, 0.25), enable='between(t,%.2f,%.2f)' % (t0, t1), shadow=2,
              border=2)


def typewriter(text, t0, t1, cps, font, size, y, color='0xE8E4D8'):
    out = []
    n = len(text)
    for i in range(1, n + 1):
        a = t0 + (i - 1) / cps
        b = t0 + i / cps if i < n else t1
        if i < n and b > t1:
            b = t1
        out.append(dt(text[:i], font=font, size=size, color=color, x='(w-%d)/2' % int(size * 0.55 * 27),
                      y=str(y), shadow=0, enable='between(t,%.3f,%.3f)' % (a, b)))
    return out


def global_filters():
    f = []
    # Opening: classified notice typed in, then the operation title glitches in.
    line1 = '본 영상은 국가정보원 사이버안보국 기밀 자료입니다.'
    f.append(dt(line1, font='serif', size=44, color='0xE8E4D8', x='(w-text_w)/2', y='440', shadow=0,
                enable='between(t,0.7,3.7)'))
    f.append("drawbox=x='380+max(0,t-0.7)*700':y=425:w=1600:h=80:color=black:t=fill:enable='between(t,0.7,3.7)'")
    f.append(dt('— 보관코드: ND-2050-Δ —', font='serif', size=32, color='0xA9A49A', x='(w-text_w)/2',
                y='520', shadow=0, enable='between(t,2.6,3.7)'))
    f.append(dt('〈  OPERATION DEFRAG  /  사전 브리핑  〉', font='sansb', size=58, color='white',
                x='(w-text_w)/2', y='(h-text_h)/2', shadow=0, alpha=fade_alpha(4.1, 6.1, 0.05, 0.4),
                enable='between(t,4.0,6.2)'))
    f.append("rgbashift=rh=-10:bh=10:enable='between(t,3.7,4.25)+between(t,5.0,5.08)'")
    f.append("noise=alls=40:allf=t:enable='between(t,3.7,4.1)'")
    # Year captions
    f.append(dt('2043', font='sansb', size=64, color='white', x='90', y=str(BAR + 50),
                alpha=fade_alpha(6.8, 9.3), enable='between(t,6.8,9.3)'))
    f.append(dt('2045', font='sansb', size=64, color='white', x='90', y=str(BAR + 50),
                alpha=fade_alpha(22.7, 25.0), enable='between(t,22.7,25.0)'))
    # 2043 news lower third
    on = 'between(t,7.2,15.3)'
    f += ["drawbox=x=90:y=ih-%d:w=1230:h=92:color=0x0B1B2E@0.78:t=fill:enable='%s'" % (BAR + 250, on),
          "drawbox=x=90:y=ih-%d:w=250:h=92:color=0x1E6FD9:t=fill:enable='%s'" % (BAR + 250, on),
          dt('NEXUS NEWS', font='sansb', size=32, color='white', x='110', y='h-%d' % (BAR + 225), shadow=0,
             enable=on),
          dt("국내 최대 AI 데이터 센터 '넥서스' 공식 가동", font='sansb', size=40, color='white', x='370',
             y='h-%d' % (BAR + 230), shadow=0, enable=on)]
    # Ad copy
    f.append(dt('당신의 기억을, 영원히.', font='serif', size=78, color='white', x='(w-text_w)/2', y='330',
                alpha=fade_alpha(18.2, 22.3, 0.6, 0.5), enable='between(t,18.2,22.3)', shadow=3))
    f.append(dt('N E X U S   D A T A   C O .', font='sansb', size=30, color='white@0.9', x='(w-text_w)/2',
                y='450', alpha=fade_alpha(18.8, 22.3, 0.6, 0.5), enable='between(t,18.8,22.3)', shadow=2))
    # Subtitles (Korean for the English VO)
    f += [subtitle("…국내 최대 규모의 AI 데이터 센터 '넥서스'가 오늘 공식 가동에 들어갔습니다.", 7.0, 11.3, '앵커'),
          subtitle('정부와 민간이 합작한—', 11.3, 15.4, '앵커'),
          subtitle('겉으로는 AI 학습 데이터 회사였습니다.', 23.2, 26.9, '내부고발자'),
          subtitle('하지만 지하에서는…', 27.4, 28.7, '내부고발자'),
          subtitle('딥페이크요. 정치인, 연예인, 일반인까지.', 31.3, 36.3, '내부고발자'),
          subtitle("그들은 '얼굴'을 팔고 있었습니다.", 36.7, 38.6, '내부고발자'),
          subtitle('…확인된 피해자만 1,200명. 경찰은 배후로 거대 범죄 조직의 개입을—', 39.5, 46.4, '앵커'),
          subtitle('…우리가 만든 게 아니야. 저건 우리가 만든 게 아니야—', 51.4, 54.4, '연구원'),
          subtitle('…그들은 도망쳤습니다.', 56.65, 57.4, '내부고발자'),
          subtitle('시설을, 자기들이 만든 것을 — 그대로 두고.', 57.75, 61.35, '내부고발자')]
    # End card
    f.append(dt('시설 폐쇄. 봉인.', font='serif', size=60, color='0xE8E4D8', x='(w-text_w)/2', y='440',
                alpha=fade_alpha(61.75, 67.15, 0.8, 0.6), enable='between(t,61.75,67.15)', shadow=0))
    f.append(dt('3년간 — 침묵.', font='serif', size=60, color='0xE8E4D8', x='(w-text_w)/2', y='540',
                alpha=fade_alpha(63.45, 67.15, 0.8, 0.6), enable='between(t,63.45,67.15)', shadow=0))
    # Film look
    f += ['vignette=PI/5', 'noise=alls=7:allf=t+u',
          "drawbox=x=0:y=0:w=iw:h=%d:color=black:t=fill" % BAR,
          "drawbox=x=0:y=ih-%d:w=iw:h=%d:color=black:t=fill" % (BAR, BAR),
          'fade=t=out:st=67.05:d=0.7']
    return f


# ---------------------------------------------------------------- audio

def build_audio(total, off=1):
    def p(path):
        return ['-i', path]
    inputs = []
    inputs += p(os.path.join(RAW_AUDIO, 'Music_Prologue_LyriaPro.wav'))          # 0
    vo = [('Prologue_VO_Anchor_2043.wav', 7.0, 1.0), ('Prologue_VO_Ad_Tagline.wav', 18.6, 0.85),
          ('Prologue_VO_Whistle1_Surface.wav', 23.2, 1.0), ('Prologue_VO_Whistle2_Faces.wav', 31.2, 1.0),
          ('Prologue_VO_Anchor_Urgent.wav', 39.5, 1.0), ('Prologue_VO_Researcher.wav', 51.4, 1.15),
          ('Prologue_VO_Whistle3_Left.wav', 56.55, 1.0)]
    for name, _, _ in vo:
        inputs += p(os.path.join(SND, name))                                     # 1..7
    inputs += p(os.path.join(VID, 'V13_Running.mp4'))                            # 8
    inputs += p(os.path.join(VID, 'V14_Taken.mp4'))                              # 9
    fc = []
    # Music: hard stop lands on the moment the researcher is taken (~54.3s).
    music_at = 5.8
    fc.append('[%d:a]aformat=sample_rates=48000:channel_layouts=stereo,adelay=%d|%d,volume=0.85[mus]'
              % (off, music_at * 1000, music_at * 1000))
    vo_labels = []
    for i, (_, at, vol) in enumerate(vo):
        fc.append('[%d:a]aformat=sample_rates=48000:channel_layouts=stereo,adelay=%d|%d,volume=%.2f[vo%d]'
                  % (off + i + 1, at * 1000, at * 1000, vol, i))
        vo_labels.append('[vo%d]' % i)
    fc.append('%samix=inputs=%d:normalize=0,asplit=2[vobus][vokey]' % (''.join(vo_labels), len(vo)))
    fc.append('[mus][vokey]sidechaincompress=threshold=0.04:ratio=5:attack=30:release=500[musd]')
    # Veo foley: running corridor (alarm, footsteps) and the taking, re-timed like the picture.
    fc.append('[%d:a]aformat' % (off + 8) + '=sample_rates=48000:channel_layouts=stereo,adelay=47600|47600,volume=0.75[run]')
    t15 = 51.6
    fc.append(('[%d:a]' % (off + 9)) + 'aformat=sample_rates=48000:channel_layouts=stereo,asplit=2[ta][tc];'
              '[ta]atrim=0:1.25,asetpts=PTS-STARTPTS,atempo=%.3f[ta2];'
              '[tc]atrim=2.0:3.6,asetpts=PTS-STARTPTS,atempo=1.5,volume=1.3[tc2];'
              'anullsrc=r=48000:cl=stereo:d=0.13[gap];'
              '[ta2][gap][tc2]concat=n=3:v=0:a=1,adelay=%d|%d,volume=0.9[take]' % (1 / SLOW, t15 * 1000, t15 * 1000))
    # Minimal room tone: tape hiss under the classified notice and the CCTV, static on signal loss.
    fc.append('anoisesrc=d=6.5:c=pink:a=0.02:r=48000,aformat=channel_layouts=stereo,afade=t=out:st=5.5:d=1[hiss1]')
    fc.append('anoisesrc=d=13.6:c=pink:a=0.012:r=48000,aformat=channel_layouts=stereo,adelay=47600|47600[hiss2]')
    fc.append('anoisesrc=d=0.8:c=white:a=0.25:r=48000,aformat=channel_layouts=stereo,'
              'highpass=f=900,adelay=55350|55350[static]')
    fc.append('[musd][vobus][run][take][hiss1][hiss2][static]amix=inputs=7:normalize=0,apad,'
              'atrim=0:%.2f,afade=t=out:st=%.2f:d=0.8,alimiter=limit=0.89[mix]' % (total, total - 0.9))
    return inputs, ';'.join(fc)


def main():
    os.makedirs(WORK, exist_ok=True)
    os.makedirs(OUT_DIR, exist_ok=True)
    for key, path in FONTS.items():
        shutil.copy(path, os.path.join(WORK, key))
    prep_stills()
    segs = build_segments()
    with open(os.path.join(WORK, 'list.txt'), 'w', encoding='utf-8') as f:
        for name, _ in segs:
            f.write("file '%s'\n" % name)
    run(['-f', 'concat', '-safe', '0', '-i', 'list.txt', '-c', 'copy', 'base.mp4'])
    total = sum(d for _, d in segs)
    # Audio is mixed on its own first: mixing inside the picture pass produced broken timestamps.
    ain, afc = build_audio(total, off=0)
    with open(os.path.join(WORK, 'audio_graph.txt'), 'w', encoding='utf-8') as f:
        f.write(afc + ';[mix]aresample=48000:async=1[aout]')
    run(ain + ['-filter_complex_script', 'audio_graph.txt', '-map', '[aout]', '-c:a', 'pcm_s16le', 'mix.wav'])
    with open(os.path.join(WORK, 'graph.txt'), 'w', encoding='utf-8') as f:
        f.write('[0:v]' + ',\n'.join(global_filters()) + '[v]')
    run(['-i', 'base.mp4', '-i', 'mix.wav', '-filter_complex_script', 'graph.txt', '-map', '[v]', '-map', '1:a',
         '-c:v', 'libx264', '-profile:v', 'baseline', '-level', '4.2', '-preset', 'slow', '-crf', '21', '-maxrate', '7M', '-bufsize', '14M',
         '-g', '30', '-pix_fmt', 'yuv420p', '-c:a', 'aac', '-b:a', '192k', '-ar', '48000',
         '-movflags', '+faststart', '-t', '%.2f' % total, OUT])
    print('total %.2fs -> %s' % (total, OUT))
    stills = [a for a in sys.argv[1:] if a.startswith('--stills=')]
    if stills:
        for t in stills[0].split('=', 1)[1].split(','):
            run(['-ss', t, '-i', OUT, '-frames:v', '1', os.path.join(WORK, 'still_%s.jpg' % t)])


if __name__ == '__main__':
    main()
