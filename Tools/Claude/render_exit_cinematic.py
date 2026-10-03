# -*- coding: utf-8 -*-
"""#2 탈출구 열림 시네마틱 편집 (B1F, 다운로드 완료 직후, B1FEscapeSequence Exit 슬롯).

python Tools/Claude/render_exit_cinematic.py [--stills=t1,t2,...]

원본: Assets/Art/B1F/ExitCinematic/Source~/ (video_raw, audio_raw, ref/B1F_Blueprint_1024.png)
결과: Assets/Movies/B1F/ExitCinematic/B1F_Exit_Cinematic.mp4 (1920x1080, 24fps, H.264 Baseline + AAC)
"""
import os
import shutil
import subprocess
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
SRC = os.path.join(ROOT, 'Assets', 'Art', 'B1F', 'ExitCinematic', 'Source~')
WORK = os.path.join(SRC, 'build')
OUT_DIR = os.path.join(ROOT, 'Assets', 'Movies', 'B1F', 'ExitCinematic')
OUT = os.path.join(OUT_DIR, 'B1F_Exit_Cinematic.mp4')
FOOT = os.path.join(ROOT, 'Assets', 'Footsteps - Essentials', 'Footsteps_Tile', 'Footsteps_Tile_Run')

W, H, FPS = 1920, 1080, 24
BAR = 60  # 2.0:1 letterbox

FONTS = {
    'sans': r'C:\Windows\Fonts\malgun.ttf',
    'sansb': r'C:\Windows\Fonts\malgunbd.ttf',
    'mono': r'C:\Windows\Fonts\consola.ttf',
    'monob': r'C:\Windows\Fonts\consolab.ttf',
}

# ---------------- timeline (seconds in the finished film) ----------------
A_LEN = 9.0            # Take A 0-9s (the last second morphs, so it is cut)
MAP_AT, MAP_LEN = A_LEN, 5.0
B1_AT, B1_LEN = MAP_AT + MAP_LEN, 1.8   # Take B 0-1.8s: look at map, nod
B2_AT, B2_LEN = B1_AT + B1_LEN, 4.6     # Take B-2 0-4.6s: side tracking run (4.5s+ bleaches)
TAIL = 0.6
TOTAL = B2_AT + B2_LEN + TAIL

VO12_AT = 3.3          # Agent B presses the radio around 3s
VO3_AT = B1_AT + 0.35

# Blueprint placement (ref/B1F_Blueprint_1024.png, world rect x -107.77..37.56, z -173.86..-28.53)
BP_CROP = (740, 950, 230, 40)                    # w, h, x, y in the 1024 image
BP_H = 790
BP_W = round(BP_CROP[0] * BP_H / BP_CROP[1])
BP_X, BP_Y = (W - BP_W) // 2, 70  # high enough that the exit marker clears the subtitle line
EXIT_WORLD = (2.73, -165.19)                      # Exit23 room (B2 stairs)
_u = (EXIT_WORLD[0] + 107.77) / 145.33
_v = (EXIT_WORLD[1] + 173.86) / 145.33
EXIT_X = round(BP_X + (_u * 1024 - BP_CROP[2]) * BP_H / BP_CROP[1])
EXIT_Y = round(BP_Y + ((1 - _v) * 1024 - BP_CROP[3]) * BP_H / BP_CROP[1])

SUBS = [  # (start, end, speaker, line)
    (VO12_AT + 0.0, VO12_AT + 2.9, '조력자', '다운로드 완료. 둘 다 수고했다.'),
    (VO12_AT + 3.6, VO12_AT + 8.1, '조력자', '지하 2층으로 내려가는 비상구를 찾았다. 지금 맵으로 전송하지.'),
    (VO3_AT + 0.0, VO3_AT + 2.7, '조력자', '움직여. 놈이 다시 찾아내기 전에.'),
]


def run(args, cwd=WORK):
    print('>', ' '.join(a if len(a) < 120 else a[:117] + '...' for a in args))
    subprocess.run(['ffmpeg', '-hide_banner', '-loglevel', 'error', '-y'] + args, cwd=cwd, check=True)


_txt_count = [0]


def txt(text):
    _txt_count[0] += 1
    name = 'txt_%02d.txt' % _txt_count[0]
    with open(os.path.join(WORK, name), 'w', encoding='utf-8') as f:
        f.write(text)
    return name


def dt(text, font='sans', size=40, color='white', x='(w-text_w)/2', y='(h-text_h)/2', enable=None,
       shadow=2, alpha=None):
    font_path = FONTS[font].replace('\\', '/').replace(':', '\\:')
    parts = ["drawtext=fontfile='%s'" % font_path, 'textfile=%s' % txt(text), 'fontsize=%d' % size,
             'fontcolor=%s' % color, "x='%s'" % x, "y='%s'" % y, 'shadowx=%d' % shadow, 'shadowy=%d' % shadow,
             'shadowcolor=black@0.8', 'expansion=none']
    if alpha:
        parts.append("alpha='%s'" % alpha)
    if enable:
        parts.append("enable='%s'" % enable)
    return ':'.join(parts)


def fade_alpha(t0, t1, f=0.25):
    return 'if(lt(t,%.2f),0,if(lt(t,%.2f),(t-%.2f)/%.2f,if(lt(t,%.2f),1,if(lt(t,%.2f),(%.2f-t)/%.2f,0))))' % (
        t0, t0 + f, t0, f, t1 - f, t1, t1, f)


# ---------------- map insert ----------------
def render_map():
    """B1F blueprint receives the route: scan-line reveal, exit marker, slow push toward the exit."""
    rx, ry = 140, 140  # marker canvas
    reveal0, reveal1, mark = 0.25, 1.55, 1.95
    scan_y = '%d+%d*clip((t-%.2f)/%.2f\\,0\\,1)' % (BP_Y, BP_H, reveal0, reveal1 - reveal0)
    ring = ("geq=r='255':g='40':b='40':a='255*between(hypot(X-%d,Y-%d),46,54)'" % (rx // 2, ry // 2))
    dot = ("geq=r='255':g='60':b='60':a='255*lte(hypot(X-%d,Y-%d),14)+120*between(hypot(X-%d,Y-%d),14,24)'"
           % (rx // 2, ry // 2, rx // 2, ry // 2))
    mx, my = EXIT_X - rx // 2, EXIT_Y - ry // 2
    label_x = EXIT_X + 70 if EXIT_X + 70 + 520 < W else EXIT_X - 70 - 520
    graph = ';\n'.join([
        'color=c=0x03080c:s=%dx%d:r=%d:d=%.2f,format=rgba,drawgrid=w=48:h=48:t=1:c=0x0e2830@0.5[bg]' % (W, H, FPS, MAP_LEN),
        '[1:v]crop=%d:%d:%d:%d,scale=%d:%d,format=rgba,split[bp][bpg]' % (BP_CROP + (BP_W, BP_H)),
        '[bpg]gblur=sigma=10,colorchannelmixer=aa=0.7[glow]',
        '[bg][glow]overlay=%d:%d:enable=\'gte(t,%.2f)\'[g1]' % (BP_X, BP_Y, reveal0),
        '[g1][bp]overlay=%d:%d:enable=\'gte(t,%.2f)\'[g2]' % (BP_X, BP_Y, reveal0),
        # reveal: cover the not-yet-scanned part, then a bright scan line
        "[g2]drawbox=x=%d:y='%s':w=%d:h=%d:color=0x03080c:t=fill:enable='lt(t,%.2f)',"
        "drawbox=x=%d:y='%s':w=%d:h=3:color=0x8af6ff@0.95:t=fill:enable='between(t,%.2f,%.2f)'[g3]" % (
            BP_X - 30, scan_y, BP_W + 60, BP_H + 20, reveal1, BP_X - 40, scan_y, BP_W + 80, reveal0, reveal1),
        'color=c=black@0:s=%dx%d:r=%d:d=%.2f,format=rgba,%s[ring]' % (rx, ry, FPS, MAP_LEN, ring),
        'color=c=black@0:s=%dx%d:r=%d:d=%.2f,format=rgba,%s[dot]' % (rx, ry, FPS, MAP_LEN, dot),
        "[g3][dot]overlay=%d:%d:enable='gte(t,%.2f)'[g4]" % (mx, my, mark),
        "[g4][ring]overlay=%d:%d:enable='gte(t,%.2f)*lt(mod(t-%.2f,0.7),0.42)'[g5]" % (mx, my, mark, mark),
        '[g5]' + ','.join([
            dt('B1F // FACILITY MAP', 'monob', 40, '0x8af6ff', '120', '150', shadow=0),
            dt('UPLINK: HANDLER   ROUTE PACKET RECEIVED', 'mono', 24, '0x8af6ff@0.7', '120', '205', shadow=0),
            "drawbox=x=120:y=%d:w=420:h=10:color=0x8af6ff@0.3:t=fill" % (H - 190),
            "drawbox=x=120:y=%d:w='420*clip((t-%.2f)/%.2f\\,0\\,1)':h=10:color=0x8af6ff@0.95:t=fill" % (
                H - 190, reveal0, reveal1 - reveal0),
            dt('DATA TRANSFER', 'mono', 22, '0x8af6ff@0.7', '120', '%d' % (H - 225), shadow=0),
            dt('100%', 'monob', 22, '0x8af6ff', '560', '%d' % (H - 196), shadow=0, enable='gte(t,%.2f)' % reveal1),
            dt('EMERGENCY EXIT', 'monob', 34, '0xff4a4a', '%d' % label_x, '%d' % (EXIT_Y - 40), shadow=0,
               enable='gte(t,%.2f)' % (mark + 0.15)),
            dt('ACCESS  >>  B2', 'mono', 26, '0xff7a7a', '%d' % label_x, '%d' % (EXIT_Y + 4), shadow=0,
               enable='gte(t,%.2f)' % (mark + 0.35)),
        ]) + ',' +
        # slow push toward the exit after the marker lands
        "zoompan=z='1+0.2*clip((it-%.2f)/%.2f,0,1)':x='clip(%d+(%d-%d)*(zoom-1)/0.2*0.55-iw/zoom/2,0,iw-iw/zoom)'"
        ":y='clip(%d+(%d-%d)*(zoom-1)/0.2*0.55-ih/zoom/2,0,ih-ih/zoom)':d=1:s=%dx%d:fps=%d" % (
            mark + 0.3, MAP_LEN - mark - 0.3, W // 2, EXIT_X, W // 2, H // 2, EXIT_Y, H // 2, W, H, FPS) +
        ',noise=alls=10:allf=t,format=yuv420p[v]',
    ])
    with open(os.path.join(WORK, 'map_graph.txt'), 'w', encoding='utf-8') as f:
        f.write(graph)
    run(['-f', 'lavfi', '-i', 'anullsrc=r=48000:cl=stereo', '-loop', '1', '-framerate', str(FPS),
         '-i', os.path.join(SRC, 'ref', 'B1F_Blueprint_1024.png'),
         '-filter_complex_script', 'map_graph.txt', '-map', '[v]', '-t', '%.2f' % MAP_LEN,
         '-c:v', 'libx264', '-crf', '14', '-pix_fmt', 'yuv420p', 'map.mp4'])


# ---------------- picture ----------------
def render_picture():
    def take(idx, start, length, extra=''):
        return ('[%d:v]trim=start=%.3f:duration=%.3f,setpts=PTS-STARTPTS,fps=%d,scale=%d:%d:flags=lanczos,'
                'setsar=1%s' % (idx, start, length, FPS, W, H, extra))

    b2_fade = B2_LEN - 0.8
    graph = [
        take(0, 0, A_LEN, ',fade=t=in:st=0:d=0.6') + '[a]',
        '[1:v]setpts=PTS-STARTPTS,fps=%d,setsar=1[m]' % FPS,
        take(2, 0, B1_LEN) + '[b1]',
        take(3, 0, B2_LEN, ',fade=t=out:st=%.2f:d=0.8' % b2_fade) + '[b2]',
        'color=c=black:s=%dx%d:r=%d:d=%.2f,setsar=1[k]' % (W, H, FPS, TAIL),
        '[a][m][b1][b2][k]concat=n=5:v=1:a=0,format=yuv420p[cat]',
    ]
    fx = [
        # glitch into the map, whip into the run
        "rgbashift=rh=-14:bh=14:enable='between(t,%.2f,%.2f)'" % (MAP_AT - 0.12, MAP_AT + 0.12),
        "avgblur=sizeX=60:sizeY=1:enable='between(t,%.2f,%.2f)'" % (B2_AT - 0.10, B2_AT + 0.08),
        "eq=brightness=0.06:enable='between(t,%.2f,%.2f)'" % (B2_AT - 0.04, B2_AT + 0.04),
        'vignette=angle=PI/4.6',
        'noise=alls=7:allf=t',
        'drawbox=x=0:y=0:w=iw:h=%d:color=black:t=fill' % BAR,
        'drawbox=x=0:y=ih-%d:w=iw:h=%d:color=black:t=fill' % (BAR, BAR),
    ]
    for t0, t1, who, line in SUBS:
        en = 'between(t,%.2f,%.2f)' % (t0, t1)
        fx.append(dt(who, 'sansb', 26, '0x8af6ff', '(w-text_w)/2', 'h-%d' % (BAR + 118), enable=en,
                     alpha=fade_alpha(t0, t1)))
        fx.append(dt(line, 'sans', 40, 'white', '(w-text_w)/2', 'h-%d' % (BAR + 78), enable=en,
                     alpha=fade_alpha(t0, t1)))
    graph.append('[cat]' + ','.join(fx) + '[v]')
    with open(os.path.join(WORK, 'graph.txt'), 'w', encoding='utf-8') as f:
        f.write(';\n'.join(graph))
    run(['-i', os.path.join(SRC, 'video_raw', 'Exit_TakeA.mp4'), '-i', 'map.mp4',
         '-i', os.path.join(SRC, 'video_raw', 'Exit_TakeB.mp4'),
         '-i', os.path.join(SRC, 'video_raw', 'Exit_TakeB2.mp4'),
         '-filter_complex_script', 'graph.txt', '-map', '[v]', '-c:v', 'libx264', '-crf', '15',
         '-pix_fmt', 'yuv420p', 'base.mp4'])


# ---------------- sound ----------------
def render_audio():
    ins, chains, labels = [], [], []
    count = [0]

    def add(path_or_lavfi, at, gain_db, extra='', lavfi=False):
        idx = count[0]
        count[0] += 1
        ins.extend(['-f', 'lavfi', '-i', path_or_lavfi] if lavfi else ['-i', path_or_lavfi])
        lab = 's%d' % idx
        ms = int(at * 1000)
        chains.append('[%d:a]aresample=48000,aformat=channel_layouts=stereo%s,volume=%.1fdB,adelay=%d|%d[%s]' % (
            idx, extra, gain_db, ms, ms, lab))
        labels.append(lab)

    # bed: low room drone for the whole film
    add('anoisesrc=color=brown:amplitude=0.6:d=%.2f:r=48000' % TOTAL, 0, -20,
        ',lowpass=f=140,afade=t=in:d=1.5,afade=t=out:st=%.2f:d=1.2' % (TOTAL - 1.4), lavfi=True)

    def squelch(at, db=-14):
        add('anoisesrc=color=white:amplitude=0.5:d=0.16:r=48000', at, db,
            ',bandpass=f=2200:w=1800,afade=t=out:st=0.06:d=0.1', lavfi=True)

    def beep(at, freq, dur, db):
        add('sine=f=%d:d=%.2f:r=48000' % (freq, dur), at, db,
            ',afade=t=in:d=0.005,afade=t=out:st=%.3f:d=%.3f' % (dur * 0.4, dur * 0.6), lavfi=True)

    # radio
    squelch(VO12_AT - 0.22)
    add(os.path.join(SRC, 'audio_raw', 'Exit_VO_1_2.mp3'), VO12_AT, 1.0)
    squelch(VO12_AT + 8.2)
    squelch(VO3_AT - 0.22)
    add(os.path.join(SRC, 'audio_raw', 'Exit_VO_3.mp3'), VO3_AT, 1.5)
    squelch(VO3_AT + 2.75)

    # map: open, data chirps, marker pings
    add('anoisesrc=color=pink:amplitude=0.5:d=0.25:r=48000', MAP_AT - 0.1, -16,
        ',highpass=f=1500,afade=t=out:st=0.05:d=0.2', lavfi=True)
    beep(MAP_AT + 0.2, 1320, 0.07, -20)
    for i in range(10):
        beep(MAP_AT + 0.3 + i * 0.12, 1800 + (i * 370) % 900, 0.035, -27)
    for i in range(4):
        beep(MAP_AT + 1.95 + i * 0.7, 660, 0.22, -17)

    # whip into the run
    add('anoisesrc=color=white:amplitude=0.6:d=0.45:r=48000', B2_AT - 0.3, -15,
        ',highpass=f=600,lowpass=f=6000,afade=t=in:d=0.25,afade=t=out:st=0.25:d=0.2', lavfi=True)

    # two runners on tile, fading as they pull away
    steps = sorted(f for f in os.listdir(FOOT) if f.endswith('.wav'))
    k = 0
    for runner, offset in ((0, 0.0), (1, 0.095)):
        t = B2_AT + offset
        while t < B2_AT + B2_LEN - 0.4:
            fall = max(0.0, (t - (B2_AT + 2.6)) / 1.6)
            add(os.path.join(FOOT, steps[k % len(steps)]), t, -9 - 14 * fall - runner * 2)
            k += 1
            t += 0.19

    mix = ';'.join(chains) + ';' + ''.join('[%s]' % l for l in labels) + \
        'amix=inputs=%d:normalize=0:duration=longest,atrim=0:%.2f,alimiter=limit=0.9[aout]' % (len(labels), TOTAL)
    with open(os.path.join(WORK, 'audio_graph.txt'), 'w', encoding='utf-8') as f:
        f.write(mix)
    run(ins + ['-filter_complex_script', 'audio_graph.txt', '-map', '[aout]', '-c:a', 'pcm_s16le', 'mix.wav'])


def main():
    os.makedirs(WORK, exist_ok=True)
    os.makedirs(OUT_DIR, exist_ok=True)
    print('total %.2fs, exit marker at (%d, %d)' % (TOTAL, EXIT_X, EXIT_Y))
    render_map()
    render_picture()
    render_audio()
    run(['-i', 'base.mp4', '-i', 'mix.wav', '-map', '0:v', '-map', '1:a',
         '-c:v', 'libx264', '-profile:v', 'baseline', '-level', '4.1', '-crf', '20', '-maxrate', '6M',
         '-bufsize', '12M', '-g', '48', '-pix_fmt', 'yuv420p', '-c:a', 'aac', '-b:a', '192k', '-ar', '48000',
         '-shortest', '-movflags', '+faststart', OUT])
    for arg in sys.argv[1:]:
        if arg.startswith('--stills='):
            for t in arg.split('=', 1)[1].split(','):
                run(['-ss', t, '-i', OUT, '-frames:v', '1', os.path.join(WORK, 'still_%s.jpg' % t)])
    print('done:', OUT)


if __name__ == '__main__':
    main()
