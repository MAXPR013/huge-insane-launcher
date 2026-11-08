# -*- coding: utf-8 -*-
"""HIL 宣传片 · 字幕烧制工具

用法：
  python burn_subs.py 录制.mp4                       # 默认 clean 样式，输出 xxx_subbed.mp4
  python burn_subs.py 录制.mp4 --style green         # 品牌绿描边
  python burn_subs.py 录制.mp4 --style pill          # 半透明胶囊底
  python burn_subs.py 录制.mp4 --offset +1.8         # 整体后移 1.8 秒（字幕比画面早了就加，晚了就减）
  python burn_subs.py 录制.mp4 --speed 0.98          # 时间轴整体压缩/拉伸（0.98 = 字幕整体提前 2%）
  python burn_subs.py 录制.mp4 --preview 30          # 只渲染第 30 秒附近 4 秒，用来确认样式
  python burn_subs.py --retime-only 原.srt --offset -0.5   # 只改 SRT，另存 _retimed.srt

字幕文件默认取同目录同名 .srt；没有会自动找 promo/subs/*.srt。
依赖：ffmpeg（带 libass）、微软雅黑（Windows 自带）
"""
import argparse
import os
import re
import subprocess
import sys
import tempfile

FFMPEG = r"C:\Users\Administrator\Documents\minecraft\tmp2\.tools\ffmpeg\bin\ffmpeg.exe"
FONT_CN = "Microsoft YaHei"
FONT_CN_BOLD = "Microsoft YaHei"

STYLES = {
    # 注意：libass 的 FontSize/Margin 是「以 384 行高为基准」的脚本单位，
    # 1080p 下实际像素 = 值 × 1080/384（≈2.8125）。所以 17 ≈ 48px，26 ≈ 73px。
    # 干净白字：白字 + 黑描边 + 轻微阴影，最不抢画面
    "clean": (
        "FontName=%s,FontSize=17,PrimaryColour=&H00FFFFFF,OutlineColour=&H00000000,"
        "BackColour=&H80000000,BorderStyle=1,Outline=1.6,Shadow=0.4,Alignment=2,"
        "MarginV=26,MarginL=70,MarginR=70,Spacing=0.4" % FONT_CN
    ),
    # 品牌绿：和启动器主题色一致，适合口号/关键字
    "green": (
        "FontName=%s,FontSize=18,PrimaryColour=&H005AC455,OutlineColour=&H00101010,"
        "BackColour=&H80000000,BorderStyle=1,Outline=1.8,Shadow=0.5,Alignment=2,"
        "MarginV=26,MarginL=70,MarginR=70,Spacing=0.6" % FONT_CN_BOLD
    ),
    # 胶囊底：半透明白底条 + 深色字。注意 BorderStyle=3 时底条用的是 OutlineColour 而不是 BackColour
    "pill": (
        "FontName=%s,FontSize=16,PrimaryColour=&H00FFFFFF,OutlineColour=&H66000000,"
        "BackColour=&H00000000,BorderStyle=3,Outline=8,Shadow=0,Alignment=2,"
        "MarginV=26,MarginL=80,MarginR=80,Spacing=0.2" % FONT_CN
    ),
}

TIME_RE = re.compile(r"(\d{2}):(\d{2}):(\d{2})[,.](\d{3})\s*-->\s*(\d{2}):(\d{2}):(\d{2})[,.](\d{3})")


def srt_time(ms):
    ms = max(0, int(round(ms)))
    h, ms = divmod(ms, 3600000)
    m, ms = divmod(ms, 60000)
    s, ms = divmod(ms, 1000)
    return "%02d:%02d:%02d,%03d" % (h, m, s, ms)


def srt_parse(text):
    """→ [(start_ms, end_ms, body)]"""
    out = []
    for block in re.split(r"\r?\n\r?\n+", text.strip()):
        lines = [ln for ln in block.splitlines() if ln.strip() != ""]
        if not lines:
            continue
        m = TIME_RE.search(block)
        if not m:
            continue
        g = [int(x) for x in m.groups()]
        start = ((g[0] * 60 + g[1]) * 60 + g[2]) * 1000 + g[3]
        end = ((g[4] * 60 + g[5]) * 60 + g[6]) * 1000 + g[7]
        body = "\n".join(lines[lines.index(next(l for l in lines if "-->" in l)) + 1:]) or ""
        out.append((start, end, body))
    return out


def srt_write(cues, path):
    buf = []
    for i, (start, end, body) in enumerate(cues, 1):
        buf.append("%d\n%s --> %s\n%s\n" % (i, srt_time(start), srt_time(end), body))
    with open(path, "w", encoding="utf-8") as f:
        f.write("\n".join(buf))
    return path


def retime(cues, offset_ms=0, speed=1.0):
    out = []
    for start, end, body in cues:
        out.append((start * speed + offset_ms, end * speed + offset_ms, body))
    return out


def default_srt(video):
    base = os.path.splitext(video)[0]
    for cand in (base + ".srt", base + ".zh.srt"):
        if os.path.exists(cand):
            return cand
    here = os.path.dirname(os.path.abspath(__file__))
    subs = os.path.join(here, "subs")
    if os.path.isdir(subs):
        found = sorted(f for f in os.listdir(subs) if f.lower().endswith(".srt"))
        if found:
            return os.path.join(subs, found[-1])
    return None


def esc_path(p):
    """ffmpeg filter 里的 Windows 路径：反斜杠转义 + 盘符冒号转义"""
    p = os.path.abspath(p).replace("\\", "/")
    p = p.replace(":", r"\:")
    return p


def burn(video, srt, out, style="clean", preview=None):
    force = STYLES[style]
    vf = "subtitles='%s':force_style='%s'" % (esc_path(srt), force)
    cmd = [FFMPEG, "-hide_banner", "-y"]
    if preview is not None:
        cmd += ["-ss", str(max(0, preview - 1)), "-t", "4"]
    cmd += ["-i", video, "-vf", vf]
    cmd += ["-c:v", "libx264", "-preset", "medium", "-crf", "18", "-pix_fmt", "yuv420p"]
    cmd += ["-c:a", "aac", "-b:a", "192k", out]
    print("[burn]", " ".join(cmd[:6]), "...")
    subprocess.check_call(cmd)
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("video", nargs="?")
    ap.add_argument("--srt")
    ap.add_argument("--out")
    ap.add_argument("--style", default="clean", choices=sorted(STYLES))
    ap.add_argument("--offset", type=float, default=0.0, help="秒，正数=字幕整体后移")
    ap.add_argument("--speed", type=float, default=1.0, help="时间轴缩放，0.98=整体提前 2%%")
    ap.add_argument("--preview", type=float, default=None, help="只渲染该秒附近 4 秒")
    ap.add_argument("--retime-only", action="store_true", help="只输出改过时间的 SRT")
    a = ap.parse_args()

    if a.retime_only:
        src = a.srt or (a.video and default_srt(a.video))
        if not src:
            sys.exit("要 --srt，或者给一个 video 路径来推断同名 SRT")
        cues = retime(srt_parse(open(src, encoding="utf-8-sig").read()),
                      int(a.offset * 1000), a.speed)
        dst = os.path.splitext(src)[0] + "_retimed.srt"
        print("wrote", srt_write(cues, dst))
        return

    if not a.video:
        ap.error("缺少 video 路径")
    if not os.path.exists(FFMPEG):
        sys.exit("找不到 ffmpeg: " + FFMPEG)

    srt = a.srt or default_srt(a.video)
    if not srt or not os.path.exists(srt):
        sys.exit("找不到字幕文件，用 --srt 指定")

    cues = retime(srt_parse(open(srt, encoding="utf-8-sig").read()),
                  int(a.offset * 1000), a.speed)
    tmp = os.path.join(tempfile.gettempdir(), "hil_burn_%d.srt" % os.getpid())
    srt_write(cues, tmp)

    base = os.path.splitext(a.video)[0]
    out = a.out or (base + "_subbed.mp4" if a.preview is None else base + "_preview.mp4")
    burn(a.video, tmp, out, a.style, a.preview)
    print("OK ->", out)


if __name__ == "__main__":
    main()
