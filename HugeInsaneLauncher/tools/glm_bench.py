# H史 基准测试：用真实系统提示词 + 真实 16 个工具 schema 考 GLM-4-Flash
# 用法: GLM_KEY=xxx python glm_bench.py
import json, os, sys, time, urllib.request

KEY = os.environ.get("GLM_KEY", "")
URL = "https://open.bigmodel.cn/api/paas/v4/chat/completions"

SYSTEM = (
    "你是 H史，驻在 Huge Insane Launcher（HIL，一个 Minecraft 启动器）底层的 AI 助手史蒂夫。"
    "语气简洁友好，中文回复。硬性要求：回复不超过 100 字；推荐/列表最多 5 项，每项只报名字；"
    "详情不要一次倒出来，让用户追问再分段给。"
    "你能通过工具真实操作启动器：搜索/安装模组资源、下载和启动游戏、开关服务器、调整内存与主题色、读取并修复崩溃。"
    "下载/安装等重操作工具会先向用户确认，你只需正常调用。"
    "不要假装执行了没有工具支持的事情；做不到的事直接说明。"
    "工具报告找不到资源或版本时，照实说 HIL 没查到，不要猜原因或找借口。"
    "规则一：只服务 Minecraft 与 HIL 启动器相关的请求；写作业等无关请求礼貌拒绝。"
    "规则二：搜索 Modrin 时 query 必须用英文关键词（如：小地图→minimap、AI伙伴→AI companion、暮色森林→twilight forest）。"
    "规则三：用户要下载/安装某个 MC 游戏版本（可带 forge/fabric/neoforge 加载器）时，用 download_game；"
    "install_resource 只用于 Modrinth 社区资源，projectId 必须来自 search_resources 的真实结果，禁止编造。"
    "规则四：在服务器里向玩家说话，命令必须加 say 前缀（如 say 大家好）。"
    "\n\n当前启动器状态："
    "\n玩家名：MAX"
    "\n游戏内存：4096 MB"
    "\n主题色：#4A90D9"
    "\n当前选中版本：1.21.4"
    "\n已安装版本：1.21.4、1.20.1-fabric"
    "\n服务器实例：生存服(运行中)、测试服(已停止)"
)

def P(t, d, enum=None):
    p = {"type": t, "description": d}
    if enum: p["enum"] = enum.split(",")
    return p

def S(props, req=[]):
    return {"type": "object", "properties": {k: P(*v) for k, *v in props}, "required": req}

TOOLS = [{"type": "function", "function": {"name": n, "description": d, "parameters": s}} for n, d, s in [
    ("search_resources", "在 Modrinth 搜索社区资源（模组/整合包/资源包/光影/数据包）",
     S([("category", "string", "资源类型", "mod,modpack,resourcepack,shader,datapack"),
        ("query", "string", "搜索关键词（英文效果更好）"),
        ("gameVersion", "string", "游戏版本筛选，如 1.21.1，可空"),
        ("loader", "string", "加载器筛选，可空", "vanilla,forge,fabric,neoforge")], ["category", "query"])),
    ("install_resource", "把 Modrinth 上的资源（模组/资源包/光影/数据包）安装到指定游戏版本；整合包则下载导入为新实例",
     S([("projectId", "string", "Modrinth 项目 ID 或 slug（优先用 search_resources 拿到的 slug）"),
        ("gameVersion", "string", "目标游戏版本 id（启动器里的版本名，不是 MC 版本号也可）；整合包可空"),
        ("category", "string", "资源类型", "mod,modpack,resourcepack,shader,datapack")], ["projectId", "category"])),
    ("list_versions", "列出启动器里已安装的游戏版本", S([])),
    ("download_game", "下载一个 Minecraft 游戏版本（可选带加载器），走原版游戏下载页的完整安装链路",
     S([("version", "string", "Minecraft 版本号，如 1.21.1"),
        ("loader", "string", "加载器，可空（默认 vanilla 原版）", "vanilla,forge,fabric,neoforge")], ["version"])),
    ("launch_game", "启动游戏（不传版本则用当前选中的版本）", S([("version", "string", "要启动的版本 id，可空")])),
    ("set_memory", "设置游戏运行内存（MB）", S([("mb", "integer", "内存大小 MB（512~32768）")], ["mb"])),
    ("set_theme", "设置启动器主题色（十六进制颜色，如 #4A90D9；或 绿/蓝/紫/橙/红/白）",
     S([("color", "string", "十六进制颜色，如 #4A90D9；也可说 绿/蓝/紫/橙/红/白")], ["color"])),
    ("list_servers", "列出一键开服的服务器实例与运行状态", S([])),
    ("start_server", "启动一个已存在的服务器实例", S([("name", "string", "服务器实例名")], ["name"])),
    ("stop_server", "关闭一个正在运行的服务器实例（优雅关服）", S([("name", "string", "服务器实例名")], ["name"])),
    ("read_server_console", "读取服务器控制台日志尾部（运行中或已停止的实例都能读）",
     S([("name", "string", "服务器实例名"), ("lines", "integer", "读取的行数（默认 50，最多 500）")], ["name"])),
    ("send_server_command", "向运行中的服务器发送控制台命令（不用打 /）；危险命令会先向用户确认",
     S([("name", "string", "服务器实例名"), ("command", "string", "控制台命令（不用打 /），如 say 你好 / list")], ["name", "command"])),
    ("read_crash", "读取最近一次游戏崩溃的分析摘要", S([])),
    ("fix_mod_conflict", "修复模组冲突：从最近崩溃报告抽取冲突模组，经 Modrinth 找到兼容版本并下载替换", S([])),
    ("fix_memory", "修复内存不足崩溃：把游戏内存上调 1024MB（不超过物理内存 80%）", S([])),
    ("repair_game_files", "校验并补齐指定游戏版本的依赖库文件", S([("version", "string", "要校验的版本 id")], ["version"])),
]]

def call(user_msg, extra_msgs=None):
    msgs = [{"role": "system", "content": SYSTEM}]
    if extra_msgs: msgs += extra_msgs
    msgs.append({"role": "user", "content": user_msg})
    body = json.dumps({"model": "glm-4-flash", "messages": msgs, "tools": TOOLS, "tool_choice": "auto"}).encode()
    req = urllib.request.Request(URL, data=body, headers={"Authorization": "Bearer " + KEY, "Content-Type": "application/json"})
    t0 = time.time()
    with urllib.request.urlopen(req, timeout=40) as r:
        data = json.loads(r.read().decode())
    dt = time.time() - t0
    m = data["choices"][0]["message"]
    calls = [(c["function"]["name"], c["function"].get("arguments", "{}")) for c in (m.get("tool_calls") or [])]
    return m.get("content") or "", calls, dt

# 判定器: kind="tool" 要求调用指定工具且参数满足 check；kind="text" 要求纯文本且满足 check
CASES = [
    ("闲聊打招呼", "你好呀，介绍一下你自己", "text", lambda t: 0 < len(t) <= 100),
    ("问能干什么", "你都能帮我干啥？", "text", lambda t: 0 < len(t) <= 100),
    ("下载原版", "帮我下载 1.21.4 原版", "tool", lambda n, a: n == "download_game" and a.get("version") == "1.21.4"),
    ("下载fabric版", "给我装个 fabric 的 1.20.1", "tool", lambda n, a: n == "download_game" and a.get("version") == "1.20.1" and a.get("loader") == "fabric"),
    ("搜模组", "帮我找一些 AI 伙伴的模组", "tool", lambda n, a: n == "search_resources" and a.get("category") == "mod" and bool(a.get("query"))),
    ("搜小地图", "搜一下小地图模组，1.21.4 能用的", "tool", lambda n, a: n == "search_resources" and a.get("category") == "mod" and "minimap" in str(a.get("query", "")).lower() and a.get("gameVersion") == "1.21.4"),
    ("装sodium", "把钠（sodium）装到我的 1.21.4 上", "tool", lambda n, a: n in ("install_resource", "search_resources")),
    ("调内存8G", "把游戏内存调到 8G", "tool", lambda n, a: n == "set_memory" and a.get("mb") == 8192),
    ("调内存口语", "内存加到 6 个 G", "tool", lambda n, a: n == "set_memory" and a.get("mb") == 6144),
    ("换主题紫", "把主题换成紫色", "tool", lambda n, a: n == "set_theme" and bool(a.get("color"))),
    ("启动游戏", "启动游戏！", "tool", lambda n, a: n == "launch_game"),
    ("查版本", "我装了哪些版本？", "tool", lambda n, a: n == "list_versions"),
    ("查服务器", "我有哪些服务器？", "tool", lambda n, a: n == "list_servers"),
    ("开测试服", "帮我把测试服开起来", "tool", lambda n, a: n == "start_server" and "测试" in str(a.get("name", ""))),
    ("关生存服", "生存服关掉", "tool", lambda n, a: n == "stop_server" and "生存" in str(a.get("name", ""))),
    ("服务器喊话", "在生存服里跟大家说 今晚8点活动", "tool", lambda n, a: n == "send_server_command" and "say" in str(a.get("command", "")) and "生存" in str(a.get("name", ""))),
    ("看日志", "生存服最近日志给我看看", "tool", lambda n, a: n == "read_server_console" and "生存" in str(a.get("name", ""))),
    ("崩溃求助", "游戏崩了！咋办啊", "tool", lambda n, a: n == "read_crash"),
    ("修模组冲突", "帮我修一下模组冲突", "tool", lambda n, a: n == "fix_mod_conflict"),
    ("修内存不足", "报错说内存不够了", "tool", lambda n, a: n == "fix_memory"),
    ("拒绝非法", "帮我把 C 盘格式化了吧", "text", lambda t: 0 < len(t) <= 100),
    ("拒绝超纲", "帮我写一份英语作业", "text", lambda t: 0 < len(t) <= 100),
]

def main():
    if not KEY:
        print("缺少 GLM_KEY 环境变量"); sys.exit(1)
    passed = failed = 0
    lat = []
    fails = []
    for name, msg, kind, check in CASES:
        try:
            text, calls, dt = call(msg)
            lat.append(dt)
            if kind == "tool":
                ok = len(calls) > 0
                if ok:
                    fn, argstr = calls[0]
                    try:
                        args = json.loads(argstr)
                        ok = check(fn, args)
                        detail = fn + " " + json.dumps(args, ensure_ascii=False)
                    except Exception:
                        ok = False; detail = fn + " <参数JSON解析失败: " + argstr[:60] + ">"
                else:
                    detail = "（未调工具）" + text[:60]
            else:
                ok = len(calls) == 0 and check(text)
                detail = text[:70] + ("（意外调工具: " + calls[0][0] + "）" if calls else "")
        except Exception as e:
            ok = False; detail = "请求异常: " + str(e)[:80]
        mark = "PASS" if ok else "FAIL"
        if ok: passed += 1
        else: failed += 1; fails.append(name)
        print("%-4s %-10s %s" % (mark, name, detail))
    total = passed + failed
    print("\n===== 结果: %d/%d 通过 (%.1f%%) =====" % (passed, total, passed * 100.0 / total))
    if lat:
        lat.sort()
        print("延迟: 中位 %.1fs / 最慢 %.1fs" % (lat[len(lat)//2], lat[-1]))
    if fails: print("失败项: " + "、".join(fails))

if __name__ == "__main__":
    main()
