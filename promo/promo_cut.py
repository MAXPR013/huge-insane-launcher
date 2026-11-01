# HIL 宣传片正式剪辑：按剪点表逐段解码→运镜/覆盖层/转场→管道给 ffmpeg
import zlib, struct, math, random, subprocess, sys
import numpy as np

W, H = 1920, 1080
FPS = 30
FF = r"HugeInsaneLauncher\tools\ffmpeg\ffmpeg.exe"
OUT = "promo_v.mp4"

# ---------- PNG（支持 RGBA） ----------
def read_png(path):
    d = open(path, 'rb').read()
    pos = 8; idat = b''
    while pos < len(d):
        ln, = struct.unpack('>I', d[pos:pos+4]); typ = d[pos+4:pos+8]
        if typ == b'IHDR': w, h, bd, ct = struct.unpack('>IIBB', d[pos+8:pos+18])
        elif typ == b'IDAT': idat += d[pos+8:pos+8+ln]
        pos += 12 + ln
    raw = zlib.decompress(idat)
    ch = 4 if ct == 6 else 3
    stride = w * ch
    px = bytearray(w * h * ch); prev = bytearray(stride); ppos = 0
    for y in range(h):
        f = raw[ppos]; ppos += 1
        line = bytearray(raw[ppos:ppos+stride]); ppos += stride
        if f == 1:
            for i in range(ch, stride): line[i] = (line[i] + line[i-ch]) & 255
        elif f == 2:
            for i in range(stride): line[i] = (line[i] + prev[i]) & 255
        elif f == 3:
            for i in range(stride):
                a = line[i-ch] if i >= ch else 0
                line[i] = (line[i] + ((a + prev[i]) >> 1)) & 255
        elif f == 4:
            for i in range(stride):
                a = line[i-ch] if i >= ch else 0; b = prev[i]; c = prev[i-ch] if i >= ch else 0
                p = a + b - c; pa, pb, pc = abs(p-a), abs(p-b), abs(p-c)
                line[i] = (line[i] + (a if (pa <= pb and pa <= pc) else (b if pb <= pc else c))) & 255
        px[y*stride:(y+1)*stride] = line; prev = line
    return np.frombuffer(bytes(px), dtype=np.uint8).reshape(h, w, ch)

# ---------- 采样/运镜 ----------
Y, X = np.mgrid[0:H, 0:W].astype(np.float32)

def sample(img, xs, ys):
    h, w = img.shape[:2]
    x0 = np.clip(xs.astype(np.int32), 0, w - 2); y0 = np.clip(ys.astype(np.int32), 0, h - 2)
    fx = (xs - x0)[..., None]; fy = (ys - y0)[..., None]
    c00 = img[y0, x0].astype(np.float32);   c10 = img[y0, x0 + 1].astype(np.float32)
    c01 = img[y0 + 1, x0].astype(np.float32); c11 = img[y0 + 1, x0 + 1].astype(np.float32)
    return c00*(1-fx)*(1-fy) + c10*fx*(1-fy) + c01*(1-fx)*fy + c11*fx*fy

def zoom(img, scale, cx, cy):
    return sample(img, cx + (X - cx) / scale, cy + (Y - cy) / scale)

def smooth(a, b, t):
    t = np.clip((t - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)

# ---------- 素材 ----------
title = read_png("card_title.png")[:, :, :3].astype(np.float32)
feat  = read_png("card_feat.png")[:, :, :3].astype(np.float32)
end   = read_png("card_end.png")[:, :, :3].astype(np.float32)
# 字卡是 1530x870：拉伸适配到 1920x1080 底（输出中心≠源中心，不能直接用 zoom）
def to1080(img1530):
    h, w = img1530.shape[:2]
    xs = w / 2.0 + (X - W / 2.0) * (w / float(W))
    ys = h / 2.0 + (Y - H / 2.0) * (h / float(H))
    return sample(img1530, xs, ys)
title1080, feat1080, end1080 = to1080(title), to1080(feat), to1080(end)

caps = [read_png("ov_cap%d.png" % i) for i in range(9)]
digits = read_png("ov_digits.png")          # "0123456789.s" 40x64/格
DGLYPH = "0123456789.s"
# 顶部常驻横幅（1920x64 不透明绿底）：每帧直接盖顶
BANNER = read_png("ov_banner.png")[:, :, :3].astype(np.float32)
BH = BANNER.shape[0]

# ---------- 源段解码 ----------
LAUNCHER_VF = "crop=1531:870:362:281,pad=1546:870:8:0:0x14161D,scale=1920:1080"
GAME_VF = "crop=860:484:695:380,scale=1920:1080"
GAME9_VF = "crop=842:474:706:523,scale=1920:1080"   # clip9 窗口位置不同（二次启动），且游戏画面 56s 才稳定
DESK_VF = "crop=2256:1269:0:117,scale=1920:1080"     # 全桌面（悬浮球最小化/恢复动画）
BALL_VF = "crop=856:482:1400:1000,scale=1920:1080"   # 右下角：球(2156,1334) 与展开面板(1456-2231,1324-1409)

def decode(clip, ss, dur, vf):
    n = int(round(dur * FPS))
    cmd = [FF, "-y", "-ss", "%.2f" % ss, "-i", clip, "-t", "%.3f" % dur,
           "-vf", vf, "-f", "rawvideo", "-pix_fmt", "rgb24", "-loglevel", "error", "-"]
    p = subprocess.Popen(cmd, stdout=subprocess.PIPE)
    fsz = W * H * 3
    for _ in range(n):
        buf = p.stdout.read(fsz)
        if len(buf) < fsz: break
        yield np.frombuffer(buf, dtype=np.uint8).reshape(H, W, 3).astype(np.float32)
    p.stdout.close(); p.wait()

def still(clip, ss, vf):
    for f in decode(clip, ss, 0.05, vf):
        return f
    return np.zeros((H, W, 3), dtype=np.float32)

CLIPS = {i: p for i, p in enumerate(sorted(__import__("glob").glob("video/屏幕录制*.mp4")), 1)}

# ---------- 覆盖层 ----------
def alpha_put(base, rgba, x, y, a=1.0):
    h, w = rgba.shape[:2]
    if x >= W or y >= H or x + w <= 0 or y + h <= 0: return
    x0, y0 = max(0, x), max(0, y)
    x1, y1 = min(W, x + w), min(H, y + h)
    sx, sy = x0 - x, y0 - y
    sub = rgba[sy:sy + (y1 - y0), sx:sx + (x1 - x0)]
    al = (sub[:, :, 3:4].astype(np.float32) / 255.0) * a
    rgb = sub[:, :, :3].astype(np.float32)
    base[y0:y1, x0:x1] = base[y0:y1, x0:x1] * (1 - al) + rgb * al

def caption(base, idx, t):   # t: 0..1，弹簧上升+淡入，末段淡出
    img = caps[idx]
    h, w = img.shape[:2]
    rise = 26 * (1 - smooth(0, 0.25, t))          # 从 26px 下方弹簧升起
    a = smooth(0, 0.18, t) * (1 - smooth(0.85, 1.0, t))
    alpha_put(base, img, 60, H - 150 + int(rise), a)

# 秒表 pill（200x76 圆角，预计算 mask）
TW, TH = 200, 76
_yy, _xx = np.mgrid[0:TH, 0:TW].astype(np.float32)
_r = TH / 2
def pill_mask():
    m = np.zeros((TH, TW), dtype=np.float32)
    m[_yy >= _r] = 1; m[_yy < TH - _r] = m[_yy < TH - _r] + 1   # 中间矩形
    m = np.zeros((TH, TW), dtype=np.float32)
    cx = np.clip(_xx, _r, TW - _r); cy = np.clip(_yy, _r, TH - _r)
    m[(_xx - cx) ** 2 + (_yy - cy) ** 2 <= _r * _r] = 1.0
    return m
PILL = pill_mask()[..., None]

def draw_timer(base, seconds, a=1.0):
    x0, y0 = W - TW - 40, BH + 24   # 避开顶部横幅
    base[y0:y0+TH, x0:x0+TW] = base[y0:y0+TH, x0:x0+TW] * (1 - PILL * 0.82 * a) + np.array([16, 18, 24], dtype=np.float32) * (PILL * 0.82 * a)
    txt = ("%.1fs" % seconds)
    total = len(txt) * 40
    dx = x0 + (TW - total) // 2
    for ch in txt:
        gi = DGLYPH.index(ch)
        glyph = digits[:, gi*40:(gi+1)*40]
        alpha_put(base, glyph, dx, y0 + (TH - 64) // 2, a)
        dx += 40

# ---------- 转场 ----------
def crumble_gen(fa, fb, n=18):
    BN = 40; BS = W // BN; rows = H // BS + 1
    rnd = random.Random(7)
    blocks = [(bx*BS, by*BS, rnd.uniform(0, 0.32), rnd.uniform(-70, 70))
              for by in range(rows) for bx in range(BN)]
    for f in range(n):
        t = f / float(n)
        base = fb.copy()
        for bx, by, delay, vx in blocks:
            fall = t - delay
            if fall <= 0:
                base[by:by+BS, bx:bx+BS] = fa[by:by+BS, bx:bx+BS]
                continue
            yo = int(3400 * fall * fall); xo = int(vx * fall)
            alpha = max(0.0, 1.0 - max(0.0, fall - 0.18) * 2.2)
            if alpha <= 0 or by + yo >= H: continue
            sy0 = max(0, by + yo); sy1 = min(H, by + yo + BS)
            sx0 = max(0, bx + xo); sx1 = min(W, bx + xo + BS)
            if sy1 <= sy0 or sx1 <= sx0: continue
            gy0 = sy0 - (by + yo); gx0 = sx0 - (bx + xo)
            blk = fa[by+gy0:by+gy0+(sy1-sy0), bx+gx0:bx+gx0+(sx1-sx0)]
            base[sy0:sy1, sx0:sx1] = base[sy0:sy1, sx0:sx1] * (1 - alpha) + blk * alpha
        yield base

def liquid_gen(fa, fb, n=19):
    for f in range(n):
        t = f / float(n)
        amp = 34 * math.sin(math.pi * t)
        dx = amp * np.sin(Y * 0.045 + t * 9.0)
        dy = amp * 0.7 * np.cos(X * 0.035 + t * 7.0)
        wa = sample(fa, X + dx, Y + dy)
        wb = sample(fb, X - dx * 0.6, Y - dy * 0.6)
        m = smooth(0.32, 0.68, t)
        yield wa * (1 - m) + wb * m

def glitch_gen(fa, fb, n=16):
    for f in range(n):
        t = f / float(n)
        src = fa if f < n // 2 else fb
        g = src.copy()
        rng = random.Random(f * 131 + 7)
        s = 1.0 - t * 0.55
        dxr = int((12 + rng.uniform(0, 9)) * s)
        g[:, :, 0] = np.roll(g[:, :, 0], dxr, axis=1)
        g[:, :, 2] = np.roll(g[:, :, 2], -dxr, axis=1)
        for _ in range(8):
            y0 = rng.randrange(0, H - 40); bh = rng.randrange(8, 48)
            g[y0:y0+bh] = np.roll(g[y0:y0+bh], int(rng.uniform(-110, 110) * s), axis=1)
        for _ in range(12):
            x0 = rng.randrange(0, W - 80); y0 = rng.randrange(0, H - 40)
            bw = rng.randrange(20, 90); bh = rng.randrange(8, 36)
            if rng.random() < 0.4:
                g[y0:y0+bh, x0:x0+bw] = np.array([85, 196, 90], dtype=np.float32) * rng.uniform(0.4, 1.0)
            else:
                g[y0:y0+bh, x0:x0+bw] = np.random.rand(bh, bw, 3).astype(np.float32) * 90
        yield np.roll(g, rng.randrange(-4, 5), axis=0)

# ---------- 时间线 ----------
# 每段: (帧数, 帧生成函数(i)->frame)；i 为段内帧号
def seg_still_zoom(img, n, z0, z1, cx=W/2, cy=H/2, fade_in=0, fade_out=0):
    def gen(i):
        t = i / max(1.0, n - 1.0)
        f = zoom(img, z0 + (z1 - z0) * t, cx, cy)
        if fade_in and i < fade_in: f = f * (i / float(fade_in))
        if fade_out and i >= n - fade_out: f = f * ((n - i) / float(fade_out))
        return f
    return gen

def seg_clip(clip, ss, dur, vf, z0=1.0, z1=1.0, cx=W/2, cy=H/2, cap=None, cap_win=None, timer=False, fade_out=0):
    n = int(round(dur * FPS))
    frames = decode(clip, ss, dur, vf)
    def gen(i, it=iter(frames)):
        f = next(it)
        if z0 != 1.0 or z1 != 1.0:
            t = i / max(1.0, n - 1.0)
            f = zoom(f, z0 + (z1 - z0) * t, cx, cy)
        if timer:
            src_t = ss + i / FPS
            dt = src_t - 8.3
            if dt >= 0:
                draw_timer(f, min(dt, 7.3))
        if cap is not None and cap_win:
            t = (i - cap_win[0]) / float(cap_win[1] - cap_win[0])
            if 0 <= t <= 1: caption(f, cap, t)
        if fade_out and i >= n - fade_out: f = f * ((n - i) / float(fade_out))
        return f
    return gen

def seg_clip_parts(clip, parts, vf, cap=None, cap_win=None):
    # 同一段素材跳剪拼接：[(ss,dur), ...] 依次解码
    import itertools
    frames = itertools.chain(*[decode(clip, ss, dur, vf) for ss, dur in parts])
    def gen(i, it=iter(frames)):
        f = next(it)
        if cap is not None and cap_win:
            t = (i - cap_win[0]) / float(cap_win[1] - cap_win[0])
            if 0 <= t <= 1: caption(f, cap, t)
        return f
    return gen

def main():
    enc = subprocess.Popen([FF, "-y", "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", "%dx%d" % (W, H),
                            "-r", str(FPS), "-i", "-", "-an", "-c:v", "libx264", "-preset", "medium",
                            "-crf", "18", "-pix_fmt", "yuv420p", OUT], stdin=subprocess.PIPE)
    count = 0
    def emit(f):
        nonlocal count
        f[0:BH] = BANNER          # 顶部常驻绿底横幅
        enc.stdin.write(np.clip(f, 0, 255).astype(np.uint8).tobytes())
        count += 1
        if count % 150 == 0: print("frame", count, flush=True)

    def run(gen, n):
        for i in range(n): emit(gen(i))

    # S0 标题卡 3.0s（静默 break 段，淡入）
    run(seg_still_zoom(title1080, 90, 1.0, 1.0, fade_in=24), 90)
    # T1 方块崩解：标题卡 → clip1 首帧
    s1_head = still(CLIPS[1], 9.2, LAUNCHER_VF)
    for f in crumble_gen(title1080, s1_head): emit(f)
    # S1 窗口弹出 2.3s
    run(seg_clip(CLIPS[1], 9.2, 2.33, LAUNCHER_VF, cap=0, cap_win=(20, 65)), 70)
    # S2 标签切换 4s（含史蒂夫挥手/头部跟踪动画）
    run(seg_clip(CLIPS[2], 6.5, 4.0, LAUNCHER_VF), 120)
    # S3 H史 问答全程 6.5s（打字"hi"→思考中→气泡回答，输入过程可见）
    run(seg_clip(CLIPS[8], 3.5, 6.5, LAUNCHER_VF, cap=1, cap_win=(105, 190)), 195)
    # S4 下载全程 10.5s + 秒表
    run(seg_clip(CLIPS[3], 6.3, 10.5, LAUNCHER_VF, timer=True, cap=2, cap_win=(235, 310)), 315)
    # T2 液态：下载完成 → 启动点击
    s5_head = still(CLIPS[4], 2.8, LAUNCHER_VF)
    for f in liquid_gen(still(CLIPS[3], 16.7, LAUNCHER_VF), s5_head): emit(f)
    # S5 启动点击+气泡 2.7s
    run(seg_clip(CLIPS[4], 2.8, 2.7, LAUNCHER_VF), 81)
    # T3 液态：盖掉 13 秒等待 → 主菜单
    s6_head = still(CLIPS[4], 26.8, GAME_VF)
    for f in liquid_gen(still(CLIPS[4], 5.4, LAUNCHER_VF), s6_head, 21): emit(f)
    # S6 主菜单 4.7s（纯画面）
    run(seg_clip(CLIPS[4], 26.8, 4.7, GAME_VF), 141)
    # S7a 液态过渡：主菜单 → 悬浮球（桌面全景含资源管理器隐私，不再直拍）
    for f in liquid_gen(still(CLIPS[4], 31.4, GAME_VF), still(CLIPS[5], 7.2, BALL_VF), 21): emit(f)
    # S7b 球→点击→展开面板 5.4s（右下角特写）
    run(seg_clip(CLIPS[5], 7.2, 5.4, BALL_VF, cap=4, cap_win=(30, 155)), 162)
    # S7c 面板快速启动页 3s
    run(seg_clip(CLIPS[5], 15.5, 3.0, BALL_VF), 90)
    # S8 液态过渡：悬浮球面板 → 开服界面（同上，避开桌面隐私区域）
    for f in liquid_gen(still(CLIPS[5], 18.4, BALL_VF), still(CLIPS[6], 15.5, LAUNCHER_VF), 21): emit(f)
    # S9a 开服：填好表单+点「开服！！」 2s
    run(seg_clip(CLIPS[6], 15.5, 2.0, LAUNCHER_VF), 60)
    # S9b 控制台跑起到「运行中！」 3.5s
    run(seg_clip(CLIPS[6], 20.0, 3.5, LAUNCHER_VF, cap=5, cap_win=(10, 100)), 105)
    # S10 明暗切换 4s
    run(seg_clip(CLIPS[7], 18.5, 4.0, LAUNCHER_VF, cap=6, cap_win=(25, 115)), 120)
    # S11 H史帮我装模组：打字跳剪（开头+结尾+发送，中间截掉） 4.5s
    run(seg_clip_parts(CLIPS[8], [(11.0, 1.5), (18.5, 3.0)], LAUNCHER_VF, cap=7, cap_win=(5, 130)), 135)
    # S11c H史操作全程实录 24.5s（思考→开模组页→搜sodium→确认→详情页→进下载管理）
    run(seg_clip(CLIPS[8], 21.5, 24.5, LAUNCHER_VF), 735)
    # S12 游戏实拍 6s
    run(seg_clip(CLIPS[9], 56.0, 6.0, GAME9_VF), 180)
    # S13 卖点卡 5.8s（渐强段）
    run(seg_still_zoom(feat1080, 174, 1.0, 1.0), 174)
    # T4 Glitch → 片尾卡（压在 BGM 爆发点）
    for f in glitch_gen(feat1080, end1080): emit(f)
    # S14 片尾卡 6s（高潮）
    run(seg_still_zoom(end1080, 180, 1.0, 1.0), 180)
    # S15 收尾 3.6s（复用 S12 的游戏画面源，末 1.5s 渐隐入黑）
    run(seg_clip(CLIPS[9], 56.0, 3.6, GAME9_VF, fade_out=45), 108)

    enc.stdin.close(); enc.wait()
    print("total frames:", count, "=>", OUT)

main()
