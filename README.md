# Huge Insane Launcher (HIL)

> 一个 **单文件、免安装** 的 Minecraft 启动器：液态玻璃界面、AI 助手、一键开服、内网穿透联机。
> 全部用 C# + WPF 手写，**零第三方依赖**，编译产物就是一个 exe。

[![Version](https://img.shields.io/badge/version-26.8.14%20Alpha-4F8FDF)]()
[![License](https://img.shields.io/badge/license-MIT-55C45A)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D4)]()
[![.NET](https://img.shields.io/badge/.NET%20Framework-4.x-512BD4)]()

[English](README.en.md) · [下载](#下载) · [功能](#功能) · [从源码构建](#从源码构建) · [常见问题](#常见问题)

---

## 截图

| 浅色模式 | 深色模式 |
|---|---|
| ![浅色](website/screenshot-light.png) | ![深色](website/screenshot-dark.png) |

---

## 这是什么

HIL 是一个 **exe 即一个启动器** 的 Minecraft 启动器：把 exe 放在任意目录，双击运行，
它会在**同目录**自动生成 `.minecraft/`（游戏文件）和 `HugeInsaneLauncher/`（配置与缓存），
删除目录即彻底卸载，不写注册表、不留系统残留。

它解决的主要是这几件事：

- **不想折腾环境**：Java 运行时按版本自动匹配下载，原版 / Forge / NeoForge / Fabric 一键装。
- **不想开一堆网页**：模组、资源包、光影、整合包、数据包直接在启动器里搜和装。
- **想和朋友联机但没公网 IP**：内置 Huge 隧道，6 位房码进房，自动打洞、失败自动中转。
- **出问题不知道看哪**：崩溃自动分类，AI 助手能直接读崩溃报告并给出修复动作。

---

## 功能

### 版本与加载器
- 原版、快照版下载，Mojang 官方清单 + BMCLAPI 镜像回退
- Forge / NeoForge / Fabric 加载器安装，安装器输出实时解析成步骤进度
- 同名实例重复下载自动加 `#` 区分，互不覆盖
- 每个实例可单独覆盖内存与进程优先级

### 资源下载（Modrinth）
- 模组 / 资源包 / 光影包 / 整合包 / 数据包 五类，支持关键词搜索与游戏版本过滤
- 资源详情页：多版本文件、依赖关系、更新记录
- 一键安装到指定实例，自动匹配 loader 与游戏版本

### 实例管理
- 实例总览、每实例设置（内存、优先级、Java 参数、窗口大小）
- 本地模组列表：启停（`.jar` ↔ `.jar.disabled`）、删除、批量导入
- 存档管理、整合包导出

### 账号
- 离线账号库：多账号切换、UUID 稳定、历史用户名自动迁移
- 外置登录（Yggdrasil / authlib-injector）：首次使用时自动下载补丁
- 微软正版登录：预留接口，当前版本未启用

### 联机
- **一键开服**：Vanilla / Paper 服务端直链下载、EULA、端口、白名单、控制台与命令
- **联机大厅**：Server List Ping 协议直查在线人数与延迟（含 DNS SRV 解析）
- **服务器列表**：自建服务器状态聚合
- **Huge 隧道**：6 位房码，Cloudflare Durable Object 信令 + NAT 打洞，最多 8 人同房，
  打洞失败自动降级为 Workers 中转

### AI 助手「H史」
- 支持 DeepSeek（自备 Key）或官网内置免费通道（GLM-4-Flash，每日限流）
- **16 个可调用工具**：搜/装资源、下载/启动游戏、改内存与主题、服务器开关与控制台、
  读崩溃、修模组冲突、修内存不足、校验游戏文件
- 崩溃后主动弹泡询问是否修复，AI 只做“听懂 + 选工具”，实际动作全由本地代码执行
- 可选拟人动画：3D 史蒂夫会走到对应控件前“点”给你看

### 外观与体验
- **液态玻璃**：VisualBrush 活底透镜 + 8 环无缝折射，pill 可像 iOS 一样拖动
- **流动史莱姆**：metaball 软团壁纸，颜色取自桌面壁纸
- 浅色 / 深色主题，支持按时间（7:00–19:00）自动切换
- 桌面悬浮球模式：进游戏后主窗收起，球体内显示进度 / AI 聊天 / 服务器日志
- 全局异常兜底 + 崩溃报告 + UI 卡顿哨兵 + 分级日志

---

## 下载

前往 [Releases](../../releases) 下载最新 `HugeInsaneLauncher.exe`，
或访问官网 <https://hugeinsane.pages.dev>。

> 单文件、免安装、无需管理员权限。首次启动会下载对应 Java 运行时与游戏文件。

---

## 从源码构建

### 环境要求

| 依赖 | 说明 |
|---|---|
| Windows 10/11 | 仅支持 Windows |
| .NET Framework 4.x | 系统自带，无需安装 |
| Python 3 | **可选**，仅用于生成单文件合并版 |

编译器用的是 Windows 自带的 `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`，
因此源码必须遵守 **C# 5 语法子集**（见 [开发约定](#开发约定)）。

### 构建步骤

```bat
:: 1. 先结束正在运行的实例，否则 csc 写不进 exe
taskkill /IM HugeInsaneLauncher.exe /F

:: 2. 编译
cmd /c build.bat

:: 3. 生成单文件合并版（可选，用于存档/分发源码）
python HugeInsaneLauncher\tools\merge_single.py 26.8.14
```

产物：

- `HugeInsaneLauncher.exe` —— 可直接运行的启动器
- `HugeInsaneLauncher_26.8.14.cs` —— 合并后的单文件源码（可选）

CI 见 [.github/workflows/build.yml](.github/workflows/build.yml)：打 `v*` tag 会自动编译并发布 Release。

---

## 项目结构

```
.
├─ build.bat                         # csc 全量编译脚本
├─ config.example.ini                # 配置示例（真实 config.ini 不入库）
├─ HugeInsaneLauncher/
│  ├─ src/                           # 源码（约 2.3 万行）
│  │  ├─ Infrastructure.cs           # 路径 / 配置 / JSON / 网络 / 下载池
│  │  ├─ GameCore.cs                 # 版本清单、下载、启动、进程管理
│  │  ├─ Loaders.cs                  # Forge / NeoForge / Fabric
│  │  ├─ Modrinth.cs                 # 资源搜索与安装
│  │  ├─ ServerCore.cs               # 一键开服
│  │  ├─ Tunnel.cs                   # Huge 隧道（WebSocket + NAT 打洞）
│  │  ├─ Accounts.cs                 # 离线 / 外置登录
│  │  ├─ CrashReport.cs              # 崩溃采集与分类
│  │  ├─ Updater.cs                  # 自动更新
│  │  ├─ ui/                         # 界面层：控件库、玻璃、史莱姆、3D 史蒂夫、悬浮球
│  │  ├─ ui/pages/                   # 18 个内容页
│  │  └─ ai/                         # AiClient / AiTools / AiSteve / 剧本引擎
│  ├─ Images/                        # 图标与方块贴图
│  ├─ docs/                          # 设计文档
│  └─ tools/                         # 合并器、自检工具、LAN agent、CF Worker
├─ website/                          # Cloudflare Pages 官网 + AI 中转
└─ .github/workflows/build.yml       # CI 编译与发布
```

---

## 技术亮点

- **零依赖单文件 WPF**：不用 NuGet、不用 XAML，全部 UI 代码建树；图标、贴图、Java agent
  都以 `-resource:` 静态链接进 exe，产物不到 600 KB。
- **VisualBrush 活底透镜**（[Glass.cs](HugeInsaneLauncher/src/ui/Glass.cs)）：
  玻璃件实时反射正下方图层，8 个互不相交的 1px 甜甜圈环按 `(1-t)²` 剖面外扩取景，
  中心恒等所以文字不糊；逐帧扫描仅在矩形变化时重绘，静止零开销。
- **手写 HTTP/1.1 over SslStream**（[AiClient.cs](HugeInsaneLauncher/src/ai/AiClient.cs)）：
  绕开 .NET Framework `HttpWebRequest` 没有 Happy Eyeballs 导致 IPv6 黑洞卡死的问题。
- **Cloudflare Durable Objects 隧道**（[hgl-relay/worker.js](HugeInsaneLauncher/tools/hgl-relay/worker.js)）：
  房间状态挂在 DO 上，WebSocket Hibernation 空闲不计费，客户端做 NAT 打洞、失败降级中转。
- **AI tool-calling**（[AiTools.cs](HugeInsaneLauncher/src/ai/AiTools.cs)）：
  AI 只能调用注册过的 16 个工具，重操作需用户确认，永远接触不到文件系统与命令行。

### 开发约定

- **C# 5 语法子集**：禁止字符串插值 `$""`、`?.`、`nameof`、表达式体成员、`out var`、元组
- 注释与界面文案用中文；给 Java 读的 JSON 必须 `UTF8Encoding(false)`（无 BOM）
- 后台线程碰 UI 一律 `Dispatcher.BeginInvoke`；`Dictionary` 取键一律 `TryGetValue`
- 不提交 `config.ini`、`accounts.json`、日志、缓存和任何构建产物

设计文档：[UI 规格](HugeInsaneLauncher/docs/UI_SPEC.md) ·
[集成契约](HugeInsaneLauncher/docs/INTEGRATION.md) ·
[AI 史蒂夫施工图](HugeInsaneLauncher/docs/ai-steve.md)

---

## 已知问题

- **浅色模式下部分文字有重影**：版本下拉条目、顶部“下载”标签页、实例管理按钮在浅色玻璃上
  可见双层描边，深色模式正常。根因疑似文字被绘制两层，尚未修复。
- **实例管理按钮边缘折射缝**：玻璃 pill 压在亮背景上时边缘有极细接缝，已接受现状。
- 仅支持 Windows；高 DPI 多屏切换后偶见 1px 偏移。
- 微软正版登录尚未实现，当前仅离线与外置登录。

---

## 常见问题

**需要自己装 Java 吗？**
不需要。启动器按游戏版本自动下载匹配的 Java 运行时，存放在 `.minecraft/runtime/`。

**杀毒软件报毒？**
单文件、未签名的 exe 容易被启发式误报。可以自行用 `build.bat` 编译，
或把文件加入信任列表。源码完全公开，可自行审查。

**数据和存档在哪？**
都在 exe 同目录。存档在 `.minecraft/instances/<实例名>/saves/`，
配置在 `HugeInsaneLauncher/config.ini`，删除整个目录即完全卸载。

**版本号怎么读？**
`年.月.当月第几版`，例如 `26.8.14` = 2026 年 8 月第 14 个版本；当月第一个版本会省略末位。

**AI 功能必须联网吗？**
是。AI 助手需要网络；不开启 AI 时启动器核心功能完全离线可用。

**支持 macOS / Linux 吗？**
不支持。本项目基于 Windows 专属的 WPF 与 .NET Framework。

---

## 免责声明

- 本项目**不是** Mojang Studios / Microsoft 的官方产品，与二者无任何隶属关系。
- 仓库**不包含** Minecraft 游戏本体、Java 运行时或任何账号凭证；使用前请通过官方渠道购买游戏。
- Minecraft 相关贴图版权归 Mojang Studios 所有，此处仅作界面装饰。
- 第三方组件与许可详见 [NOTICE](NOTICE)。

## 致谢

- [PCL2](https://github.com/Meloong-Git/PCL) —— 界面设计参考（未包含其代码）
- [authlib-injector](https://github.com/yushijinhun/authlib-injector) —— 外置登录
- [Modrinth](https://modrinth.com) —— 资源 API
- [BMCLAPI](https://bmclapi2.bangbang93.com) —— 下载镜像

## 许可证

[MIT](LICENSE) © 2026 Huge Insane Launcher contributors
