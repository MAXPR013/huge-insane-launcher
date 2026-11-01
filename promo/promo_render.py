# HIL 宣传片转场 demo：方块崩解 / 液态折射 / 故障 —— 逐帧 numpy 渲染，管道给 ffmpeg 出 mp4
import zlib, struct, math, random, subprocess, sys
import numpy as np

W, H = 1530, 870
FPS = 30
FFMPEG = sys.argv[1] if len(sys.argv) > 1 else r"HugeInsaneLauncher\tools\ffmpeg\ffmpeg.exe"
OUT = "promo_demo.mp4"

# ---------- PNG 解码（纯标准库） ----------
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
    arr = np.frombuffer(bytes(px), dtype=np.uint8).reshape(h, w, ch)
    return arr[:, :, :3].copy()

# ---------- 采样/变换 ----------
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
title = read_png("card_title.png").astype(np.float32)
shot  = read_png("website/screenshot.png").astype(np.float32)
feat  = read_png("card_feat.png").astype(np.float32)
end   = read_png("card_end.png").astype(np.float32)
assert title.shape == shot.shape == feat.shape == end.shape == (H, W, 3)

# ---------- 转场 1：方块崩解 ----------
BN = 30; BS = W // BN           # 30 列 × 17 行，51px 方块
rows = H // BS
blocks = []
rnd = random.Random(7)
for by in range(rows):
    for bx in range(BN):
        blocks.append((bx*BS, by*BS, rnd.uniform(0, 0.32), rnd.uniform(-70, 70)))
T1N = 18
def crumble(f):
    t = f / float(T1N)
    base = zoom(shot, 1.06, W*0.5, H*0.62)   # 底图：下一幕静帧
    for bx, by, delay, vx in blocks:
        fall = t - delay
        if fall <= 0:
            base[by:by+BS, bx:bx+BS] = title[by:by+BS, bx:bx+BS]
            continue
        yo = int(3400 * fall * fall); xo = int(vx * fall)
        alpha = max(0.0, 1.0 - max(0.0, fall - 0.18) * 2.2)
        if alpha <= 0 or by + yo >= H: continue
        sy0 = max(0, by + yo); sy1 = min(H, by + yo + BS)
        sx0 = max(0, bx + xo); sx1 = min(W, bx + xo + BS)
        if sy1 <= sy0 or sx1 <= sx0: continue
        gy0 = sy0 - (by + yo); gx0 = sx0 - (bx + xo)
        blk = title[by+gy0:by+gy0+(sy1-sy0), bx+gx0:bx+gx0+(sx1-sx0)]
        base[sy0:sy1, sx0:sx1] = base[sy0:sy1, sx0:sx1] * (1-alpha) + blk * alpha
    return base

# ---------- 转场 2：液态折射 ----------
T2N = 19
def liquid(f):
    t = f / float(T2N)
    amp = 30 * math.sin(math.pi * t)         # 0→峰→0
    dx = amp * np.sin(Y * 0.045 + t * 9.0)
    dy = amp * 0.7 * np.cos(X * 0.035 + t * 7.0)
    a = zoom(shot, 1.10, W*0.5, H*0.62)
    b = zoom(feat, 1.02, W*0.5, H*0.5)
    wa = sample(a, X + dx, Y + dy)
    wb = sample(b, X - dx*0.6, Y - dy*0.6)
    m = smooth(0.32, 0.68, t)
    return wa * (1 - m) + wb * m

# ---------- 转场 3：故障 ----------
T3N = 16
def glitch(f):
    t = f / float(T3N)
    src = feat if f < T3N // 2 else end
    g = src.copy()
    rng = random.Random(f * 131 + 7)
    s = 1.0 - t * 0.55                        # 强度缓慢衰减
    # RGB 通道错位
    dxr = int((10 + rng.uniform(0, 8)) * s)
    g[:, :, 0] = np.roll(g[:, :, 0], dxr, axis=1)
    g[:, :, 2] = np.roll(g[:, :, 2], -dxr, axis=1)
    # 横带位移
    for _ in range(7):
        y0 = rng.randrange(0, H - 40); bh = rng.randrange(8, 44)
        g[y0:y0+bh] = np.roll(g[y0:y0+bh], int(rng.uniform(-90, 90) * s), axis=1)
    # 噪点方块（一半概率主题绿）
    for _ in range(10):
        x0 = rng.randrange(0, W - 80); y0 = rng.randrange(0, H - 40)
        bw = rng.randrange(20, 80); bh = rng.randrange(8, 36)
        if rng.random() < 0.4:
            g[y0:y0+bh, x0:x0+bw] = np.array([85, 196, 90], dtype=np.float32) * rng.uniform(0.4, 1.0)
        else:
            g[y0:y0+bh, x0:x0+bw] = np.random.rand(bh, bw, 3).astype(np.float32) * 90
    # 整体垂直抖动
    return np.roll(g, rng.randrange(-4, 5), axis=0)

# ---------- 时间线 ----------
def frame_at(i):
    if i < 45:                                # S1 标题卡 1.5s
        t = i / 44.0
        f = zoom(title, 1.0 + 0.045 * t, W/2, H/2)
        if i < 12: f *= (i / 12.0)
        return f
    if i < 63:  return crumble(i - 45)        # T1 0.6s
    if i < 123:                               # S2 截图 Ken Burns 2s
        t = (i - 63) / 59.0
        tt = t*t*(3-2*t)
        return zoom(shot, 1.0 + 0.12 * tt, W*(0.5 - 0.14*tt), H*(0.5 + 0.24*tt))
    if i < 142: return liquid(i - 123)        # T2 0.63s
    if i < 182:                               # S3 卖点卡 1.33s
        t = (i - 142) / 39.0
        f = zoom(feat, 1.035 - 0.035 * t, W/2, H/2)
        if i < 150: f = f * 0.6 + f * 0.4 * ((i - 142) / 8.0)
        return f
    if i < 198: return glitch(i - 182)        # T3 0.53s
    # S4 片尾 1.4s
    t = (i - 198) / 41.0
    f = zoom(end, 1.0 + 0.02 * t, W/2, H/2)
    if i > 231: f *= (240 - i) / 8.0
    return f

# ---------- 试片模式：抽几帧存 PNG 目检 ----------
def write_png(path, arr):
    h, w = arr.shape[:2]
    raw = b''.join(b'\x00' + arr[y].astype(np.uint8).tobytes() for y in range(h))
    def chunk(t, d):
        c = struct.pack('>I', len(d)) + t + d
        return c + struct.pack('>I', zlib.crc32(t + d) & 0xffffffff)
    ihdr = struct.pack('>IIBBBBB', w, h, 8, 2, 0, 0, 0)
    open(path, 'wb').write(b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', ihdr) + chunk(b'IDAT', zlib.compress(raw)) + chunk(b'IEND', b''))

if "--test" in sys.argv:
    for i in (20, 50, 56, 60, 90, 127, 132, 138, 160, 186, 190, 194, 220):
        write_png("pf_%03d.png" % i, np.clip(frame_at(i), 0, 255).astype(np.uint8))
        print("pf_%03d.png" % i)
    sys.exit(0)

# ---------- 编码 ----------
cmd = [FFMPEG, "-y", "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", "%dx%d" % (W, H),
       "-r", str(FPS), "-i", "-", "-an", "-c:v", "libx264", "-preset", "medium",
       "-crf", "18", "-pix_fmt", "yuv420p", OUT]
proc = subprocess.Popen(cmd, stdin=subprocess.PIPE)
N = 240
for i in range(N):
    f = np.clip(frame_at(i), 0, 255).astype(np.uint8)
    proc.stdin.write(f.tobytes())
    if i % 30 == 0: print("frame %d/%d" % (i, N))
proc.stdin.close()
proc.wait()
print("done:", OUT)
