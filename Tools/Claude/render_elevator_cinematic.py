# -*- coding: utf-8 -*-
"""B1F 진입 엘리베이터 시네마틱을 영상 한 편으로 굽는다 (LobbyF -> B1F 씬 로딩 중에 재생).

python Tools/Claude/render_elevator_cinematic.py [--stills=t1,t2,...]

예전에는 B1F 씬의 LobbyIntroCinematic이 클립 10개를 실시간으로 엮어 재생했다. 씬 로딩 중에는
VideoPlayer 10개와 AudioSource 출력이 로딩 끊김을 견디지 못하므로, 같은 타임라인(샷 시작/끝,
페이드, 확대, 캡션, 자막, B1 타이틀 카드)을 그대로 옮겨 그림 한 편 + 사운드트랙 한 개로 만든다.

원본: Assets/Movies/B1F/ElevatorIntro/Elev_01~10.mp4, Assets/SoundSources/B1F/ElevatorCinematic/
결과: Assets/Movies/B1F/ElevatorIntro/B1F_Elevator_Cinematic.mp4 (1920x1080, 24fps, H.264 Baseline, 무음)
      Assets/Resources/SceneFlow/B1F_Elevator_Cinematic_Audio.wav (사운드트랙 = 재생 시계)
"""
import os
import subprocess
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
CLIPS = os.path.join(ROOT, 'Assets', 'Movies', 'B1F', 'ElevatorIntro')
VO = os.path.join(ROOT, 'Assets', 'SoundSources', 'B1F', 'ElevatorCinematic')
TITLE_HIT = os.path.join(ROOT, 'Assets', 'SoundSources', 'LobbyF', 'Intro', 'Intro_TitleHit.wav')
WORK = os.path.join(ROOT, 'Temp', 'ElevatorCinematicBuild')
OUT = os.path.join(CLIPS, 'B1F_Elevator_Cinematic.mp4')
OUT_AUDIO = os.path.join(ROOT, 'Assets', 'Resources', 'SceneFlow', 'B1F_Elevator_Cinematic_Audio.wav')

W, H, FPS = 1920, 1080, 24
BAR = round((H - W / 2.39) / 2)  # 2.39:1 letterbox, as in LobbyIntroCinematic
FONT = os.path.join(ROOT, 'Assets', 'UISource', 'NanumSquareB.ttf')

# Same timeline as the B1F scene's "B1F Elevator Intro Cinematic" (LobbyIntroCinematic).
# (clip, start, end, fadeIn, scale0, scale1, clipAudioVolume)
SHOTS = [
    ('Elev_01_Approach', 0.5, 4.5, 1.0, 1.00, 1.03, 0.8),
    ('Elev_02_Keypad', 4.4, 7.4, 0.25, 1.00, 1.03, 0.8),
    ('Elev_03_Touch', 7.3, 10.3, 0.25, 1.00, 1.04, 0.8),
    ('Elev_04_Granted', 10.2, 13.2, 0.25, 1.00, 1.04, 0.9),
    ('Elev_05_DoorsOpen', 13.1, 17.1, 0.3, 1.00, 1.03, 0.9),
    ('Elev_06_CCTV', 17.0, 20.0, 0.2, 1.00, 1.00, 0.7),
    ('Elev_07_PressB1', 19.9, 23.9, 0.25, 1.00, 1.04, 0.8),
    ('Elev_08_DoorsClose', 23.8, 26.8, 0.25, 1.00, 1.02, 0.9),
    ('Elev_09_TalkA', 26.7, 31.7, 0.4, 1.00, 1.04, 0.45),
    ('Elev_10_TalkB', 31.6, 36.4, 0.3, 1.00, 1.06, 0.45),
]
SOUNDS = [  # (path, at, volume)
    (os.path.join(VO, 'Elevator_VO_AgentA.mp3'), 27.4, 1.0),
    (os.path.join(VO, 'Elevator_VO_AgentB.mp3'), 31.9, 1.0),
    (TITLE_HIT, 37.2, 0.7),
]
SUBS = [  # (start, end, speaker, line)
    (27.4, 31.3, '요원 A', '의외로 가까운 곳에 지하로 내려갈 수단이 있었군.'),
    (31.9, 36.3, '요원 B', '지금부터가 시작이겠지. 긴장을 풀지 마.'),
]
CAPTION = '02:31 AM  ·  NEXUS HQ  ·  SERVICE ELEVATOR'
CAPTION_TIME = (0.8, 4.2)
TITLE_TIME = (36.4, 40.0)
TITLE, TITLE_SUB = 'B1', 'NEXUS  ·  SUBLEVEL 1  ·  RESTRICTED'
CARD, INK, SUB_INK, RULE = '0x050506', '0xE6EBF0', '0x8C949E', '0xD91F1F'
TOTAL = TITLE_TIME[1] + 0.6  # the card has dissolved to black; the overlay then fades into gameplay


def run(args):
    print('>', ' '.join(a if len(a) < 120 else a[:117] + '...' for a in args))
    subprocess.run(['ffmpeg', '-hide_banner', '-loglevel', 'error', '-y'] + args, cwd=WORK, check=True)


_txt = [0]


def textfile(text):
    _txt[0] += 1
    name = 'txt_%02d.txt' % _txt[0]
    with open(os.path.join(WORK, name), 'w', encoding='utf-8') as f:
        f.write(text)
    return name


def drawtext(text, size, color, x, y, alpha=None, enable=None, shadow=2):
    font = FONT.replace('\\', '/').replace(':', '\\:')
    parts = ["drawtext=fontfile='%s'" % font, 'textfile=%s' % textfile(text), 'fontsize=%d' % size,
             'fontcolor=%s' % color, "x='%s'" % x, "y='%s'" % y, 'shadowx=%d' % shadow, 'shadowy=%d' % shadow,
             'shadowcolor=black@0.75', 'expansion=none']
    if alpha:
        parts.append("alpha='%s'" % alpha)
    if enable:
        parts.append("enable='%s'" % enable)
    return ':'.join(parts)


def window(t0, t1, t2, t3):
    """Alpha 0 -> 1 over [t0, t1], 1 -> 0 over [t2, t3] (LobbyIntroCinematic.Window)."""
    return 'clip(min((t-%.3f)/%.3f\\,(%.3f-t)/%.3f)\\,0\\,1)' % (t0, t1 - t0, t3, t3 - t2)


def smooth(expr):
    return '(%s)*(%s)*(3-2*(%s))' % (expr, expr, expr)


def render_picture():
    t0 = SHOTS[0][1]
    graph, last = [], None
    for i, (name, start, end, fade, s0, s1, _) in enumerate(SHOTS):
        nxt = SHOTS[i + 1] if i + 1 < len(SHOTS) else None
        length = (nxt[1] + nxt[3] - start) if nxt else (TOTAL - start)
        life = max(0.01, end - start)
        zoom = '%.4f+%.4f*clip(on/%d/%.3f\\,0\\,1)' % (s0, s1 - s0, FPS, life)
        # Upscale first so the slow push-in does not step on whole pixels.
        chain = ('[%d:v]setpts=PTS-STARTPTS,fps=%d,scale=%d:%d:flags=lanczos,setsar=1,'
                 'tpad=stop_mode=clone:stop_duration=%.3f,trim=duration=%.3f,'
                 "zoompan=z='%s':x='iw/2-iw/zoom/2':y='ih/2-ih/zoom/2':d=1:s=%dx%d:fps=%d,setsar=1"
                 % (i, FPS, W * 2, H * 2, length + 1, length, zoom, W, H, FPS))
        if i == 0:
            chain += ',fade=t=in:st=0:d=%.2f' % fade
        graph.append(chain + '[s%d]' % i)
        if last is None:
            last = 's0'
            continue
        out = 'x%d' % i
        graph.append('[%s][s%d]xfade=transition=fade:duration=%.3f:offset=%.3f[%s]' % (last, i, fade, start - t0, out))
        last = out
    graph.append('color=c=black:s=%dx%d:r=%d:d=%.2f,setsar=1[lead]' % (W, H, FPS, t0))
    graph.append('[lead][%s]concat=n=2:v=1:a=0,trim=duration=%.3f,format=yuv420p[cat]' % (last, TOTAL))

    bars = '%d*%s' % (BAR, smooth('clip(t/1.2\\,0\\,1)'))
    fx = [
        # film grade (Hidden/DeFrag/CinematicGrade: contrast 1.12, saturation 0.85, vignette, grain)
        'eq=contrast=1.12:saturation=0.85',
        'colorbalance=rs=-0.03:bs=0.035:rh=0.02:bh=-0.015',
        'vignette=angle=PI/5',
        'noise=alls=6:allf=t',
        "drawbox=x=0:y=0:w=iw:h='%s':color=black:t=fill" % bars,
        "drawbox=x=0:y='ih-(%s)':w=iw:h='%s':color=black:t=fill" % (bars, bars),
    ]
    # opening caption, typed at 26 characters per second
    c0, c1 = CAPTION_TIME
    cap_alpha = window(c0, c0 + 0.4, c1 - 0.6, c1)
    for n in range(1, len(CAPTION) + 1):
        if CAPTION[n - 1] == ' ' and n < len(CAPTION):
            continue
        a = c0 + (n - 1) / 26.0
        b = c0 + n / 26.0 if n < len(CAPTION) else c1
        fx.append(drawtext(CAPTION[:n], 26, '0xD9EDFF', '96', '%d' % (BAR + 40), cap_alpha,
                           'between(t,%.3f,%.3f)' % (a, b)))
    for s, e, who, line in SUBS:
        en = 'between(t,%.2f,%.2f)' % (s, e)
        alpha = window(s, s + 0.25, e - 0.3, e)
        fx.append(drawtext(who, 22, '0x8FD8FF', '(w-text_w)/2', 'h-%d' % (BAR - 14), alpha, en))
        fx.append(drawtext(line, 34, 'white', '(w-text_w)/2', 'h-%d' % (BAR - 46), alpha, en))
    # B1 title card: dark card fades in, title text window, then it dissolves to black
    a, b = TITLE_TIME
    title_alpha = window(a + 0.9, a + 1.6, b - 0.5, b)
    title_fx = [
        drawtext(TITLE, 150, INK, '(w-text_w)/2', '%d-text_h/2' % round(H * (1 - 0.58)), title_alpha,
                 'between(t,%.2f,%.2f)' % (a, b), shadow=0),
        drawtext(TITLE_SUB, 34, SUB_INK, '(w-text_w)/2', '%d-text_h/2' % round(H * (1 - 0.42)), title_alpha,
                 'between(t,%.2f,%.2f)' % (a, b), shadow=0),
        "drawbox=x=%d:y=%d:w=520:h=3:color=%s:t=fill:enable='between(t,%.2f,%.2f)'" % (
            (W - 520) // 2, round(H * (1 - 0.47)) - 1, RULE, a + 1.2, b - 0.3),
    ]
    graph.append('[cat]' + ','.join(fx) + '[graded]')
    # the card then dissolves to black (TOTAL is already past the dissolve, so the tail is plain black)
    graph.append('color=c=%s:s=%dx%d:r=%d:d=%.2f,format=rgba,fade=t=in:st=%.2f:d=0.9:alpha=1[card]'
                 % (CARD, W, H, FPS, TOTAL, a))
    graph.append('[graded][card]overlay=0:0:format=auto,' + ','.join(title_fx) +
                 ',fade=t=out:st=%.2f:d=0.6,format=yuv420p[v]' % b)

    with open(os.path.join(WORK, 'graph.txt'), 'w', encoding='utf-8') as f:
        f.write(';\n'.join(graph))
    inputs = []
    for shot in SHOTS:
        inputs += ['-i', os.path.join(CLIPS, shot[0] + '.mp4')]
    run(inputs + ['-filter_complex_script', 'graph.txt', '-map', '[v]', '-t', '%.3f' % TOTAL,
                  '-c:v', 'libx264', '-crf', '15', '-pix_fmt', 'yuv420p', 'base.mp4'])


def render_audio():
    inputs, chains, labels = [], [], []

    def add(path, at, volume, extra=''):
        idx = len(labels)
        inputs.extend(['-i', path])
        ms = int(round(at * 1000))
        chains.append('[%d:a]aresample=48000,aformat=channel_layouts=stereo,volume=%.3f%s,adelay=%d|%d[a%d]'
                      % (idx, volume, extra, ms, ms, idx))
        labels.append('a%d' % idx)

    for name, start, _, fade, _, _, volume in SHOTS:
        # Each clip's own sound plays from its start to its natural end (it overlaps the next shot).
        add(os.path.join(CLIPS, name + '.mp4'), start, volume, ',afade=t=in:d=%.2f' % min(fade, 0.3))
    for path, at, volume in SOUNDS:
        add(path, at, volume)
    mix = ';'.join(chains) + ';' + ''.join('[%s]' % l for l in labels) + \
        ('amix=inputs=%d:normalize=0:duration=longest,atrim=0:%.3f,'
         'afade=t=out:st=%.3f:d=0.6,alimiter=limit=0.9[out]' % (len(labels), TOTAL, TOTAL - 0.6))
    with open(os.path.join(WORK, 'audio_graph.txt'), 'w', encoding='utf-8') as f:
        f.write(mix)
    os.makedirs(os.path.dirname(OUT_AUDIO), exist_ok=True)
    run(inputs + ['-filter_complex_script', 'audio_graph.txt', '-map', '[out]', '-c:a', 'pcm_s16le',
                  '-ar', '48000', OUT_AUDIO])


def main():
    os.makedirs(WORK, exist_ok=True)
    print('total %.2fs, letterbox %dpx' % (TOTAL, BAR))
    render_picture()
    render_audio()
    run(['-i', 'base.mp4', '-an', '-c:v', 'libx264', '-profile:v', 'baseline', '-level', '4.1', '-crf', '20',
         '-maxrate', '6M', '-bufsize', '12M', '-g', '48', '-pix_fmt', 'yuv420p', '-movflags', '+faststart', OUT])
    for arg in sys.argv[1:]:
        if arg.startswith('--stills='):
            for t in arg.split('=', 1)[1].split(','):
                run(['-ss', t, '-i', OUT, '-frames:v', '1', 'still_%s.jpg' % t])
    print('done:', OUT, OUT_AUDIO)


if __name__ == '__main__':
    main()
