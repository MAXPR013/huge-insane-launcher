# Huge Insane Launcher (HIL) — 项目备忘录

> 写给未来的自己/接续者：对话压缩会丢细节，这份 README 是完整存档。读它 + 这份目录，就能无缝接续开发。

## 这是什么

- **单文件 WPF Minecraft 启动器**：C# 4.0 / .NET Framework 4.x，无第三方 NuGet 依赖，全部静态链接进一个 exe。
- 命名史：HugeLauncher → Huge Going Launcher (HGL) → **Huge Insane Launcher (HIL，现名，别再改)**。
- 当前版本 **26.12.0 Alpha**。产物 = 根目录 `HugeInsaneLauncher.exe`（约 3.2MB，内嵌种子地图内核、物品贴图与方块模型）。
- 运行布局：exe 放哪，就在哪自动生成 `.minecraft/`（游戏文件）和 `HugeInsaneLauncher/`（配置目录）——所以"一个 exe 就是一个启动器"。
- 核心功能：离线模式用户名登录、原版/Forge/NeoForge/Fabric 版本下载（重复下载目录名加 `#` 区分）、模组/资源包/光影/整合包四大下载源（带搜索）、内存/优先级/动画/配色设置、联机、H史 AI 聊天、史蒂夫双层 3D 看板 + 史莱姆背景、液态玻璃 UI、首页/找乐子小部件面板、种子地图、合成查询（3×3 + 游戏贴图/方块模型图标）、账号页、迁移向导、新手引导。

## 目录地图（2026-08-29 整理后）

| 路径 | 内容 |
|---|---|
| `.minecraft/` | 游戏文件。**红线：`26.2-fabric-0.19.3` 是正在玩的版本，绝不动** |
| `HugeInsaneLauncher/` | 启动器工程：`src/`（源码，`src/ui/` 界面层）、`tools/merge_single.py`（单文件合并器）、`Images/`、`docs/`、`update/`、`config.ini`（**含 DeepSeek API key，不外传**） |
| `website/` | 官网+发布：`index.html`、`_worker.js`、`version.txt`、`HugeInsaneLauncher.exe`（发布副本） |
| `回滚/` | 历史单文件版本（HugeGoingLauncher 26.8.1/6/8、HugeLauncher.cs、HugeInsaneLauncher_26.8.13.cs）+ `debug.exe`（26.8.13 编译件，对照实验用） |
| `promo/` | 宣传视频工程：card_gen / overlay_gen / promo_cut.py / promo_render.py / card_*.png / promo.mp4 / promo.webm |
| `LiquidGlass/` | 液态玻璃沙盒（渲染实验台，与 HIL 解耦，可随便折腾） |
| `tmp/` | 调试素材：`shots/`（140 张截屏）、`scripts/`（ps1 探针，**`screen_top.ps1`=置顶强拍工具**）、`ref_frames/`、`misc/`（json 缓存/Xray 包等） |
| `tmp2/.tools/` | 下载的工具链：`jdk-21`、`ffmpeg`（视频转码） |
| `video/`、`参考视频/` | 录屏素材 |
| `promo/HIL公告.mp4` + 根目录 `slam_gen.cs/.exe` | 公告视频成品（14s 硬切白底黑字，成品在 promo/ 里）与生成器（改常量重跑，再 ffmpeg 转 MP4） |
| `HugeLauncher/`、`IcoConverter/`、`images/`、`tmp2/dinodash` | 联机 agent 工程、图标工具、官网图源、小项目，均未动 |

## 编译与发布（顺序别乱）

```bash
taskkill //IM HugeInsaneLauncher.exe //F          # 必须先杀，且 tasklist 确认 0，否则 csc 写不进 exe
cmd //c build.bat                                 # csc 直接编 HugeInsaneLauncher/src/**.cs → 根目录 exe
python HugeInsaneLauncher/tools/merge_single.py 26.8.14   # 合并出单文件版 HugeInsaneLauncher_26.8.14.cs
cp HugeInsaneLauncher.exe website/HugeInsaneLauncher.exe  # 发布（必要时同步 version.txt）
```

## 液态玻璃大战 — 定稿与教训（最重要的一节）

**定稿方案**（`src/ui/Glass.cs` 的 `LensRings`）：
中心恒等透镜 + 8 个 1px 甜甜圈折射环（单调 `(1-t)²` 剖面，`RimMax=5`，`BandPx=8`）+ **不透明衬底**（史莱姆开 = `Palette.Tile` 且 alpha≥220，关 = `Palette.BgTop`）+ 高光罩 + 边缘受光描边。pill 压在文字层上面、透镜刷文字层。**用户拍板"黑色罩就罩着"，此页翻篇，别再折腾。**

**被用户否决、别再试的方案**：
1. 透镜取背景/史莱姆层 → 文字不进透镜，完全不弯（14 初版的事故）；
2. 抛物线位移剖面 `4t(1-t)` → 没治好鞋边缝，还加重文字重影；
3. 无衬底"真·清透玻璃" → 环带里真文字+位移副本双显，重影回归。

**血泪教训**：
- `Lens()` 里 Viewport **必须**绝对钉死为元素矩形（`Brush.Viewport = (0,0,el.W,el.H)`）；取景框 = 元素矩形相对源 + 源的 `VisualTreeHelper.GetOffset`（steveLayer 偏移 147.3 是实证）。
- pill 衬底 alpha 是重影/遮罩的同一个旋钮：<220 真文字渗上来=重影，=220 就是不透明罩（已定稿接受）。
- 截屏验证：`CopyFromScreen` **拍不到史蒂夫区域的按钮**（3D 视口合成怪癖），pill/文字区域正常；Chrome 抢前台时用 `tmp/scripts/screen_top.ps1`（置顶强拍）。**用户肉眼才是最终验收**，探针只辅助。
- 探针：`HugeInsaneLauncher.exe --bubble-demo` 启动后 2.5s/5s 自动写 `lens_imgs/` + `lens_dump*.txt`（每个透镜画刷内容+取景框数据）。

## 代码红线

- C# 4.0 语法（无字符串插值 / `?.` / `nameof`）；注释用中文；ps1 脚本纯 ASCII。
- 改 `config.ini` 前必杀 HIL 进程；config 里的 DeepSeek key 不复述不外传。
- 禁改 `TaskCenter.cs`。

## 近期状态与待办

- 液态玻璃定稿（见上）；启动按钮文字已随明暗切换（亮黑/暗白）。
- 公告视频 `HIL公告.mp4` 已交付（用户备考 CSP 两个月的停更说明）。
- **用户正在备考 CSP（约 2 个月），开发放缓。**
- 已知未解：实例管理按钮鞋边折射缝（我的截屏拍不到那块，只有用户能验收；定稿版可能仍有轻微痕迹，用户已接受现状）。

---

## 2026-08-29 追加：根文件夹改名风波（已回滚）与会话机制备忘

- 当天根文件夹 `minecraft` 被改名为 `HugeInsane`，Kimi 会话随之"消失"：会话按工作目录路径分桶存储（`~/.kimi-code/sessions/wd_<slug>_<sha256(路径)前12位>/`），改名=换桶，旧会话在新路径下不可见；跨目录 `kimi --session <id>` 会被 CLI 拒绝（"created under a different directory"）。
- 结局：用户把全部内容移回 `minecraft`，会话恢复正常，**迁移不再必要**。
- 改名期间做过半套桶迁移，残留物（再改名时可续用，也可无视）：新桶 `wd_hugeinsane_db62f9b8e9c6` 已复制 13 个会话，但各 `state.json` 的 workDir/homedir 未补丁、`session_index.jsonl` 未改写（原索引备份在 `~/.kimi-code/session_index.jsonl.bak`）；旧桶 `wd_minecraft_78d07a786add` 完好；玻璃大战主会话 = `session_4b1e7b0f-df1d-47b9-849e-92b591795b7a`。
- 教训：**别改根文件夹名**；非要改，先完成桶迁移再改。
- 另：根目录旁可能残留一个**空的** `HugeInsane/` 文件夹——改名期间误建的残留，因被本 Session 进程占用删不掉，里面没有任何东西，随时可手动删除。

## 遗留 UI 问题（2026-08-29 用户临走前截图反馈，未修，复归后优先看）

截图在对话里未存盘，按以下文字描述复现。症状集中在玻璃药丸的文字渲染，浅色模式最显眼：

1. 版本下拉条目（"原版游戏""模组"）文字呈黑色双重涂抹，几乎不可读；
2. 顶部"下载"选项卡文字灰色双边重影（放大可见双层）；深色模式下同一 tab 也有轻微残迹；
3. "实例管理"按钮文字带深色投影鬼影，且按钮透出身后亮背景（史蒂夫腿部），白字压亮蓝对比度不足；
4. 对照：深色模式设置页（动画速度滑条、主题配色）渲染干净——问题集中在浅色模式/浅色玻璃的文字路径。

排查方向（未验证）：与玻璃大战修过的重影同族——疑似文字被画两层（衬底罩下一层透过罩隐约可见 + 罩上一层）。220 衬底罩只压住了主按钮，tab 标签与下拉条目可能走了不同绘制分支、没吃到同样的衬底处理。先查 `src/ui/Glass.cs` 的 LensRings 应用范围与各控件文字绘制顺序；验收只能靠用户肉眼（参见"血泪教训"）。
