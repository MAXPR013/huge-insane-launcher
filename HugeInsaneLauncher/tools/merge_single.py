# -*- coding: utf-8 -*-
# HIL 正式版单文件整合工具：把 HugeInsaneLauncher/src/ 下全部 .cs 合并成一个可编译的单文件。
# 做法：每个文件的顶层 using 收进它自己的 namespace 块内，这样任意顺序拼接都合法。
# 用法：python merge_single.py <版本号>     例：python merge_single.py 26.8.8
import io, os, re, sys

SRC = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src")
OUT_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")

USING_RE = re.compile(r"^\s*using\s+[\w.]+(\s*=\s*[\w.,\s<>\[\]]+)?\s*;\s*$")
NS_RE = re.compile(r"^\s*namespace\s+[\w.]+\s*(\{)?\s*$")

def transform(path):
    with io.open(path, "r", encoding="utf-8") as f:
        lines = f.read().splitlines()
    # 找 namespace 行
    ns_i = -1
    for i, ln in enumerate(lines):
        if NS_RE.match(ln):
            ns_i = i
            break
    if ns_i < 0:
        raise RuntimeError(path + " 没有 namespace 声明，需要手工处理")
    head, rest = lines[:ns_i], lines[ns_i:]
    usings = [ln.strip() for ln in head if USING_RE.match(ln)]
    head = [ln for ln in head if not USING_RE.match(ln)]
    # 剥掉头尾多余空行
    while head and not head[0].strip(): head.pop(0)
    while head and not head[-1].strip(): head.pop()
    # namespace 行可能不带 { ，找到 { 所在行
    j = 0
    while j < len(rest) and "{" not in rest[j]:
        j += 1
    if j >= len(rest):
        raise RuntimeError(path + " namespace 找不到开括号")
    block = rest[:j + 1]
    if usings:
        block.append("")
        block += ["    " + u for u in usings]
    block += rest[j + 1:]
    return head + [""] + block

def main():
    ver = sys.argv[1]
    files = []
    for root, _, names in os.walk(SRC):
        for n in sorted(names):
            if n.endswith(".cs"):
                files.append(os.path.join(root, n))
    files.sort()
    out = os.path.join(OUT_DIR, "HugeInsaneLauncher_%s.cs" % ver)
    with io.open(out, "w", encoding="utf-8", newline="\r\n") as w:
        w.write("// ============================================================================\n")
        w.write("// Huge Insane Launcher (HIL) %s - 正式版单文件整合存档（By MAX）\n" % ver)
        w.write("// 由 HugeInsaneLauncher/src/ 下 %d 个源文件合并而成\n" % len(files))
        w.write("// 编译：与 build.bat 相同参数，把 -recurse 换成本文件即可\n")
        w.write("// ============================================================================\n")
        for p in files:
            rel = os.path.relpath(p, os.path.join(OUT_DIR)).replace("\\", "/")
            w.write("// ==================== %s ====================\n" % rel)
            w.write("\n".join(transform(p)))
            w.write("\n\n")
    print("OK %s (%d files)" % (out, len(files)))

if __name__ == "__main__":
    main()
