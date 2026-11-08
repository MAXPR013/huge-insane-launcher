# -*- coding: utf-8 -*-
"""HIL 宣传片 · 逐句字幕生成（本地语音识别，音频不出本机）

为什么需要它：字幕要让「静音观看 / 听不清」的人也看得懂，所以必须是说了什么就显示什么，
不能只写关键词。我没法听你的录音，于是用 whisper 在本机把语音转成文字 + 时间轴，
再由人（我）校一遍术语和数字。

用法：
  python asr_srt.py 录制.mp4                    # 默认 medium，输出同名 .srt
  python asr_srt.py 录制.mp4 --model small      # 先跑一遍看时间轴（快）
  python asr_srt.py 录制.mp4 --model large-v3   # 更准，CPU 上约 8-15 倍时长
  python asr_srt.py 录制.mp4 --max-chars 16     # 一行更短（竖屏/大字幕用）
  python asr_srt.py 录制.mp4 --out 字幕.srt

首次运行会下载模型（small ≈ 480MB / medium ≈ 1.5GB）。国内网络拉不动 HuggingFace 时：
  set HF_ENDPOINT=https://hf-mirror.com
"""
import argparse
import os
import re
import subprocess
import sys
import tempfile

FFMPEG = r"C:\Users\Administrator\Documents\minecraft\tmp2\.tools\ffmpeg\bin\ffmpeg.exe"

# 术语纠错：whisper 写专有名词容易飘，转写后统一替换
FIXES = [
    (r"(?i)huge\s*insane\s*launcher", "HIL"),
    (r"(?i)\bhil\b", "HIL"),
    (r"(?i)(三点三|3\s*\.\s*3)\s*兆", "3.3 MB"),
    (r"(?i)兆字节", "MB"),
    (r"(?i)shift\s*(加|\+|＋)?\s*s", "Shift+S"),
    (r"(?i)三\s*[乘x×]\s*三", "3×3"),
    (r"(?i)页速|佩奇数|配奇数", "pages.dev"),
    (r"(?i)找乐子|找乐", "找乐子"),
]


def extract_audio(video, wav):
    cmd = [FFMPEG, "-hide_banner", "-loglevel", "error", "-y", "-i", video,
           "-vn", "-ac", "1", "-ar", "16000", "-c:a", "pcm_s16le", wav]
    subprocess.check_call(cmd)
    return wav


def fmt(t):
    ms = max(0, int(round(t * 1000)))
    h, ms = divmod(ms, 3600000)
    m, ms = divmod(ms, 60000)
    s, ms = divmod(ms, 1000)
    return "%02d:%02d:%02d,%03d" % (h, m, s, ms)


CN = "\u4e00-\u9fa5"


def clean(text):
    """统一术语、全角标点，并去掉中文之间的空格"""
    t = (text or "").strip()
    for pat, rep in FIXES:
        t = re.sub(pat, rep, t)
    t = re.sub(r"\s+", " ", t)
    t = re.sub(r"(?<=[" + CN + r"])\s+(?=[" + CN + r"])", "", t)
    # 中文语境里的半角标点 → 全角（whisper 常输出 "," 导致不能分句）
    t = re.sub(r"(?<=[" + CN + r"])\s*,\s*", "，", t)
    t = re.sub(r"(?<=[" + CN + r"])\s*\.\s*", "。", t)
    t = re.sub(r"(?<=[" + CN + r"])\s*!\s*", "！", t)
    t = re.sub(r"(?<=[" + CN + r"])\s*\?\s*", "？", t)
    t = re.sub(r"(?<=[" + CN + r"])\s*;\s*", "；", t)
    t = re.sub(r"(?<=[" + CN + r"])\s*:\s*", "：", t)
    t = re.sub(r"(?<=[A-Za-z0-9])\s*,\s*(?=[" + CN + r"])", "，", t)
    if t and t[-1] not in "。！？，、；：…—.!?":
        t += "。"
    return t


def split_long(seg_start, seg_end, text, max_chars=20):
    """没有词级时间戳时的兜底：按逗号贪心打包，绝不在句子中间硬切"""
    text = (text or "").strip()
    if len(text) <= max_chars:
        return [(seg_start, seg_end, text)]
    parts = [p for p in re.split(r"(?<=[，。！？；、])", text) if p.strip()]
    if not parts:
        return [(seg_start, seg_end, text)]
    groups, cur = [], ""
    for p in parts:
        if cur and len(cur) + len(p) > max_chars:
            groups.append(cur)
            cur = p
        else:
            cur += p
    if cur:
        groups.append(cur)
    total = sum(len(g) for g in groups) or 1
    out, t = [], seg_start
    for g in groups:
        dur = (seg_end - seg_start) * len(g) / total
        out.append((t, t + dur, g))
        t += dur
    return out


def write_srt(cues, out):
    with open(out, "w", encoding="utf-8") as f:
        for i, (st, en, body) in enumerate(cues, 1):
            f.write("%d\n%s --> %s\n%s\n\n" % (i, fmt(st), fmt(en), body))
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("video")
    ap.add_argument("--model", default="medium")
    ap.add_argument("--out")
    ap.add_argument("--prompt", default="")
    ap.add_argument("--max-chars", type=int, default=20)
    ap.add_argument("--no-split", action="store_true")
    a = ap.parse_args()

    if not os.path.exists(a.video):
        sys.exit("找不到视频: " + a.video)
    if not os.path.exists(FFMPEG):
        sys.exit("找不到 ffmpeg: " + FFMPEG)

    from faster_whisper import WhisperModel

    wav = os.path.join(tempfile.gettempdir(), "hil_asr.wav")
    print("[1/3] 抽音轨 …")
    extract_audio(a.video, wav)

    # 自己读 PCM 传给 whisper：绕开 faster-whisper 对 av 版本的依赖（av 19 的 open() 签名变了）
    import wave
    import numpy as np
    with wave.open(wav, "rb") as wf:
        raw = wf.readframes(wf.getnframes())
    audio = np.frombuffer(raw, dtype=np.int16).astype(np.float32) / 32768.0

    print("[2/3] 加载模型 %s（首次会下载）…" % a.model)
    model = WhisperModel(a.model, device="cpu", compute_type="int8")

    print("[3/3] 识别中 …")
    kw = dict(language="zh", beam_size=5, vad_filter=True,
              vad_parameters=dict(min_silence_duration_ms=350),
              condition_on_previous_text=False)
    if a.prompt:
        kw["initial_prompt"] = a.prompt
    segments, info = model.transcribe(audio, **kw)
    print("    语言=%s 时长=%.1fs" % (info.language, info.duration))

    # 中文用「整句文本 + 逗号贪心切分」效果最好：
    # 词级时间戳会把中文切成单字 token，拼起来变成「这个。启。动。器。」，反而更差。
    cues = []
    for s in segments:
        text = clean(s.text)
        if not text:
            continue
        if a.no_split:
            cues.append((s.start, s.end, text))
        else:
            cues.extend(split_long(s.start, s.end, text, a.max_chars))

    out = a.out or (os.path.splitext(a.video)[0] + ".srt")
    write_srt(cues, out)
    print("wrote", out, "(%d 条)" % len(cues))
    print("下一步：人工校术语/数字，然后 python promo\\burn_subs.py 视频 --srt", out)


if __name__ == "__main__":
    main()
