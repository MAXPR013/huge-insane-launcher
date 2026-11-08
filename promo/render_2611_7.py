# -*- coding: utf-8 -*-
"""HIL 26.11.7 宣传片渲染：字卡 1s → 左演示 / 右讲解（白底留白，硬切）

结构（总约 43s）：
  0.0 开场 0.png + 标题 1s
  1.0 更Huge 1s
  2.0 也更Insane 1s
  之后六段：每段 = 1s 大字（全屏白底黑字）+ 左侧演示视频 / 右侧标题+正文
  片尾：黑底 Huge. Insane. 1s → 结尾.png 5s（末 0.3s 淡出）
"""
import os
import subprocess
import sys
import tempfile

from PIL import Image, ImageDraw, ImageFont

W, H, FPS = 1920, 1080, 30
ROOT = r"C:\Users\Administrator\Documents\minecraft"
MAT = os.path.join(ROOT, "VIDEO_素材")
BUILD = os.path.join(ROOT, "_promo_build")
FFMPEG = os.path.join(ROOT, r"tmp2\.tools\ffmpeg\bin\ffmpeg.exe")
OUT = os.path.join(ROOT, "HIL_宣传片_26.11.7.mp4")
MUSIC = os.path.join(BUILD, "music_v8.wav")

F_CN_B = r"C:\Windows\Fonts\msyhbd.ttc"
F_CN = r"C:\Windows\Fonts\msyh.ttc"

WHITE = (255, 255, 255)
INK = (11, 12, 16)
BODY = (91, 97, 110)

# 左侧演示区
LX, LY, LW, LH = 140, 240, 940, 605
# 右侧文案区
TX = 1180
TW = 600

SHOTS = [
    # (素材, 演示时长, 大字, 右标题, 右正文)
    ("1.mp4", 3.5, "打开就是家", "一屏看完今天的 Minecraft",
     "签到、今日灵感、服务器动态、官网新闻，\n开机第一眼就知道今天玩什么。"),
    ("2.mp4", 5.6, "搬家，不搬文件", "认得出别的启动器",
     "扫描 PCL、HMCL、官方启动器的版本和存档，\n勾一下就能搬过来，不用重装。"),
    ("3.mp4", 6.7, "一个种子，一整个世界", "先看见，再出发",
     "地形、村庄、要塞、出生点一次画出来，\n点哪儿就复制哪儿的传送指令。"),
    ("4.mp4", 5.0, "账号，好好放着", "离线、外置、正版都在这",
     "添加账号只要一个名字，\n头像、皮肤、登录状态一页看清。"),
    ("5.mp4", 4.0, "你换皮肤，他也换", "左边的史蒂夫就是你",
     "切换账号，看板上的史蒂夫\n立刻换上新皮肤。"),
    ("6", 2.8, "连按钮都想靠近你", "整个界面都跟手",
     "鼠标靠近，按钮会轻轻被吸过去，\n松开手再弹回原位。"),
]


def font(path, size):
    return ImageFont.truetype(path, size)


def draw_center(draw, text, f, color, cy):
    box = draw.textbbox((0, 0), text, font=f)
    tw, th = box[2] - box[0], box[3] - box[1]
    draw.text(((W - tw) / 2 - box[0], cy - th / 2 - box[1]), text, font=f, fill=color)


def card_big(text, size):
    im = Image.new("RGB", (W, H), WHITE)
    d = ImageDraw.Draw(im)
    f = font(F_CN_B, size)
    draw_center(d, text, f, INK, H / 2)
    return im


def title_card():
    bg = Image.open(os.path.join(MAT, "0.png")).convert("RGB").resize((W, H), Image.LANCZOS)
    im = bg.copy()
    d = ImageDraw.Draw(im)
    d.text((0, 0), "", font=font(F_CN, 10))
    f1 = font(F_CN_B, 96)
    f2 = font(F_CN, 54)
    # 半透明黑压一层，保证白字可读
    veil = Image.new("RGBA", (W, H), (0, 0, 0, 70))
    im = Image.alpha_composite(im.convert("RGBA"), veil).convert("RGB")
    d = ImageDraw.Draw(im)
    draw_center(d, "Huge Insane Launcher", f1, WHITE, H / 2 - 44)
    draw_center(d, "26.11.7", f2, WHITE, H / 2 + 70)
    return im


def split_bg(head, body):
    im = Image.new("RGB", (W, H), WHITE)
    d = ImageDraw.Draw(im)
    fh = font(F_CN_B, 52)
    fb = font(F_CN, 30)
    y = H / 2 - 120
    d.text((TX, y), head, font=fh, fill=INK)
    y += 100
    for line in body.split("\n"):
        d.text((TX, y), line, font=fb, fill=BODY)
        y += 56
    return im


def frame_dir(clip, seconds, outdir):
    os.makedirs(outdir, exist_ok=True)
    if clip == "6":
        parts = ["6s1.mp4", "6s2.mp4", "6s3.mp4", "6s4.mp4"]
    else:
        parts = [clip]
    idx = 0
    for p in parts:
        dur = seconds / len(parts) if clip == "6" else seconds
        src = os.path.join(MAT, p)
        # 素材原始时长：1.mp4 2.5s，需要拉慢到 3.5s；4.mp4 取前 5s
        src_dur = float(subprocess.check_output(
            [FFMPEG.replace("ffmpeg.exe", "ffprobe.exe"), "-v", "error",
             "-show_entries", "format=duration", "-of", "csv=p=0", src]).strip())
        vf = ("scale=%d:%d:force_original_aspect_ratio=decrease,"
              "pad=%d:%d:(ow-iw)/2:(oh-ih)/2:color=white,fps=%d") % (LW, LH, LW, LH, FPS)
        cmd = [FFMPEG, "-v", "error", "-y"]
        if clip == "1":
            cmd += ["-i", src, "-vf", "setpts=1.4*PTS," + vf, "-t", str(dur)]
        else:
            cmd += ["-i", src, "-vf", vf, "-t", str(dur)]
        cmd += ["-start_number", str(idx), os.path.join(outdir, "f%05d.png")]
        subprocess.check_call(cmd)
        idx = len([n for n in os.listdir(outdir) if n.startswith("f")])
    return idx


def main():
    tmp = os.path.join(tempfile.gettempdir(), "hil_promo_2611_7")
    os.makedirs(tmp, exist_ok=True)

    timeline = []   # (kind, payload, frames)

    # 开场 + 两句宣言
    timeline.append(("img", title_card(), 45))
    timeline.append(("img", card_big("更Huge", 190), 45))
    timeline.append(("img", card_big("也更 Insane", 230), 45))

    for clip, dur, big, head, body in SHOTS:
        timeline.append(("img", card_big(big, 190), 45))
        n = int(round(dur * FPS))
        d = os.path.join(tmp, clip.replace(".mp4", "").replace("6", "six"))
        got = frame_dir(clip, dur, d)
        timeline.append(("seq", (d, split_bg(head, body)), min(n, got)))

    timeline.append(("img", card_big_black("Huge. Insane."), 30))

    end = Image.open(os.path.join(MAT, "结尾.png")).convert("RGB")
    end.thumbnail((W, H), Image.LANCZOS)
    canvas = Image.new("RGB", (W, H), WHITE)
    canvas.paste(end, ((W - end.width) // 2, (H - end.height) // 2))
    timeline.append(("img", canvas, 150, "fadeout"))

    total = sum(t[2] for t in timeline)
    print("frames =", total, "seconds =", round(total / FPS, 2))

    cmd = [FFMPEG, "-v", "error", "-y",
           "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", "%dx%d" % (W, H), "-r", str(FPS), "-i", "-",
           "-stream_loop", "-1", "-i", MUSIC,
           "-map", "0:v", "-map", "1:a",
           "-c:v", "libx264", "-preset", "medium", "-crf", "18", "-pix_fmt", "yuv420p",
           "-c:a", "aac", "-b:a", "192k",
           "-af", "afade=t=out:st=%.2f:d=1.2" % (total / FPS - 1.2),
           "-t", str(total / FPS), "-shortest", OUT]
    proc = subprocess.Popen(cmd, stdin=subprocess.PIPE)

    for item in timeline:
        kind, payload, n = item[0], item[1], item[2]
        fade = len(item) > 3 and item[3] == "fadeout"
        if kind == "img":
            for i in range(n):
                frame = payload
                if fade and i > n - 9:          # 末 0.3s 淡出到黑
                    k = (n - i) / 9.0
                    frame = Image.eval(payload, lambda v: int(v * k))
                proc.stdin.write(frame.tobytes())
        else:
            d, bg = payload
            names = sorted(os.listdir(d))
            for i in range(n):
                frame = bg.copy()
                try:
                    shot = Image.open(os.path.join(d, names[min(i, len(names) - 1)])).convert("RGB")
                except Exception:
                    continue
                frame.paste(shot, (LX, LY))
                proc.stdin.write(frame.tobytes())

    proc.stdin.close()
    proc.wait()
    print("ok ->", OUT, os.path.getsize(OUT) // 1024, "KB")


def card_big_black(text):
    im = Image.new("RGB", (W, H), (0, 0, 0))
    d = ImageDraw.Draw(im)
    draw_center(d, text, font(F_CN_B, 150), WHITE, H / 2)
    return im


if __name__ == "__main__":
    main()
