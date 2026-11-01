# HugeLauncher WPF UI 规格（给实现者）

## 环境与约束（必须遵守）

- 编译器是 Windows 自带的老 csc.exe（C# 5 时代语法子集）：
  - 禁止：字符串插值 `$""`、表达式体成员 `=>`（属性/方法体）、`nameof`、空条件 `?.`、元组、模式匹配、`out var` 内联声明
  - 允许：`var`、lambda、对象/集合初始化器（但**不要**跟在匿名 `delegate {...}` 后面使用对象初始化器语法，会编译失败，改成先赋值变量再设置属性）
- WPF **纯代码**，禁止 XAML。引用程序集已在 build.bat 中配好（PresentationFramework/PresentationCore/WindowsBase）
- 所有源码放 `HugeLauncher/src/ui/` 子目录，命名空间 `HugeLauncher.Ui`
- 核心层 API 在 `HugeLauncher/src/*.cs`（Infrastructure.cs / GameCore.cs / Loaders.cs / Modrinth.cs / Program.cs），**禁止修改核心层文件**，唯一例外是 Program.cs 的 GUI 分支（见「入口」）
- 构建验证命令（在仓库根目录的 Git Bash 里）：`cmd //c "build.bat -out:HugeLauncher_ui_test.exe"`
  （输出到测试 exe，避免覆盖正在使用的 HugeLauncher.exe；末尾的 %* 会把你传的参数附加给 csc，后出现的 -out 覆盖前者）

## 核心层 API 速查

```csharp
// Infrastructure.cs
class Config { string Username; List<string> UserHistory; string VersionId; bool ShowSnapshots;
  int MemoryMb; string Priority /*高/高于正常/正常*/; double AnimSpeed /*0-2,0=关动画*/;
  string Theme /*绿/蓝/紫/橙/红/白*/; void PushUserHistory(string); static Config Load(); void Save(); }
class StepEvent { enum Kind{StepStart,StepProgress,StepDone,Log}; Kind EventKind; int StepIndex;
  string StepName; long Current,Total; double MbPerSec; bool Success; string Message;
  static StepEvent Start(int,string); Prog(int,long,long,double); Done(int,bool,string); LogLine(string); }
class Env { static bool SystemJavaPresent(); }
class Paths { static string McDir, LauncherDir, JavawExe; static string InstanceDir(string); }

// GameCore.cs
class GameCore {
  class VersionEntry { string Id, Type /*release/snapshot*/, Url; }
  static List<VersionEntry> GetVersionList();           // 网络，慢，需后台线程
  static List<string> GetInstalledVersions();           // 本地扫描，快
  static bool IsVersionReady(string id);
  static void DownloadVanilla(VersionEntry, Action<StepEvent>);  // 步骤 0-3
  static System.Diagnostics.Process Launch(string id, string user, Config, Action<string> log, string instanceName=null);
}

// Loaders.cs
class Loaders {
  class LoaderVersion { string Version, Label; }
  static List<LoaderVersion> GetFabricLoaderList(string mc);   // 网络
  static List<LoaderVersion> GetForgeVersions(string mc);      // 网络
  static List<LoaderVersion> GetNeoForgeVersions(string mc);   // 网络
  static string InstallFabric(string mc, string ver, Action<StepEvent>);   // 返回最终版本 id，步骤 4
  static string InstallForge(string mc, string ver, Action<StepEvent>);    // 步骤 4-6，慢（跑安装器）
  static string InstallNeoForge(string mc, string ver, Action<StepEvent>);
}

// Modrinth.cs
class Modrinth {
  class SearchHit { string ProjectId, Slug, Title, Author, Description, IconUrl; long Downloads; }
  static List<SearchHit> Search(string projectType /*mod/resourcepack/shader/modpack*/, string query, string gameVersion);
  static string DetectLoader(string versionId);  // fabric/forge/neoforge/vanilla
  static string InstallResource(string projectType, string projectId, string gameVersion, string loader,
                                string instanceName, Action<string> log);   // 无匹配抛 HglException("HIL 找不到可用的游戏版本")
  static string InstallModpack(string projectId, string packName, Action<StepEvent>); // 返回实例名，步骤 0-4
  static string GetInstanceLaunchId(string instanceName);
}
class HglException : Exception { HglException(string); }
```

## 入口

Program.cs 里 CLI 分支保持不变。GUI 分支当前是：
```csharp
System.Windows.Forms.Application.EnableVisualStyles();
System.Windows.Forms.Application.Run(new PlaceholderForm());
```
把它改成调用 `HugeLauncher.Ui.UiEntry.Run();`（你在 ui/UiEntry.cs 里实现该静态方法，内部 `new System.Windows.Application().Run(new MainWindow())`）。可以删除 PlaceholderForm 类。WPF 的 Application 与 WinForms 命名冲突处用全限定名。

## 界面规格

### 主窗口（MainWindow）
- 无边框：`WindowStyle=None`、`AllowsTransparency=true`、背景透明，根容器是圆角 Border（CornerRadius=12），深色渐变背景（#FF1B1E27 → #FF14161D），外侧一圈主题色细边 + DropShadowEffect
- 自绘标题栏（高 36）：左侧 "HugeLauncher" 字样 + 小字 "HIL"，右侧最小化/关闭两个按钮；拖动标题栏移动窗口；关闭 hover 变红、最小化 hover 变亮，颜色用 200ms 渐变过渡
- 启动动画：窗口加载时 Opacity 0→1 + 整体上移 14px 的入场动画（400ms， CubicEase.EaseOut）
- 尺寸 900x560，居中

### 左侧栏（宽 300，即 1/3）
从上到下：
1. 品牌区："HugeLauncher" 大号字（28pt，主题色）+ "社区驱动 · 离线模式" 小灰字
2. 用户名：可编辑 ComboBox（IsEditable=true），ItemsSource=Config.UserHistory，Text=Config.Username
3. 当前版本小字（灰色，如 "当前版本: fabric-loader-0.19.3-26.2"，未安装显示 "当前版本: 未安装"）
4. 三个**圆形按钮**竖排居中：
   - 「版本选择」「版本设置」：直径 64，深灰底 #FF2A2E3A，hover 变主题色描边，文字 11
   - 「启动！！」：直径 96，主题色底，白字 16 加粗，待机时有呼吸光晕动画（DropShadowEffect 的 BlurRadius 在 8↔24 之间 1.6s 往复，AutoReverse）
   - 按钮点击都有 0.92→1.0 缩放回弹（RenderTransform ScaleTransform，150ms）

### 右侧栏（其余宽度）
TabControl（自绘深色样式），两个 Tab：

**Tab 1「下载」**：内嵌第二个 TabControl，四个子 Tab：Mod / 资源包 / 光影包 / 整合包。每个子 Tab 内容相同结构（复用一个 UserControl 式的方法构建）：
- 顶部一行：搜索 TextBox（占位提示 "搜索名称..."，无边框深色样式）+ 版本筛选 ComboBox（已安装版本列表 + "全部"）+ 「搜索」按钮
- 结果列表：ListBox（自绘 ItemTemplate：标题加粗 + 作者灰色 + 简介一行省略 + 下载量 + 右侧「安装」按钮）
- 列表下方或右侧：状态文字（"输入关键词搜索" / "加载中..." / "共 N 个结果" / 错误信息）
- 搜索流程：后台线程调 Modrinth.Search(type, query, 选中版本=="全部"?null:根游戏版本(用 GameCore.Resolve?——简化：直接用实例版本 id 的 RootId，可对已安装版本调 GameCore.Resolve(id).RootId 得到游戏版本，异常则忽略用 null))，结果回 UI 线程填充
- 点「安装」：弹确认（资源名 + 目标实例=当前选中版本），后台线程调 Modrinth.InstallResource(type, projectId, rootGameVersion, Modrinth.DetectLoader(当前版本), 当前版本, log)；整合包调 Modrinth.InstallModpack(projectId, title, report) 成功后提示实例名
- 捕获 HglException：MessageBox 显示其 Message（即 "HIL 找不到可用的游戏版本"）；其他异常也 MessageBox

**Tab 2「设置」**（全部即时生效并 Config.Save()）：
- 内存分配：Slider 1024–16384 步进 512，旁边 TextBlock 显示 "4096 MB"，绑定 Config.MemoryMb
- 进程优先级：ComboBox：高 / 高于正常 / 正常
- 动画速度：Slider 0–2 步进 0.25，0 显示 "关闭"
- 主题配色：6 个圆形色块（绿 #FF55C45A、蓝 #FF4F8FDF、紫 #FF9B6BD3、橙 #FFE08A3C、红 #FFD9534F、白 #FFE8E8E8），点击切换全局主题色（ThemeManager，见下）
- 底部小字：系统 Java 检测结果（启动时 Env.SystemJavaPresent()）：未安装显示 "系统未安装 Java（不影响游戏运行）"

### ThemeManager（ui/ThemeManager.cs）
- 静态：当前强调色 SolidColorBrush；ApplyTheme(string name) 更新画刷颜色并刷新需要换色的元素
- 简单做法：各主题色元素在加载时向 ThemeManager 注册（静态事件 ColorChanged），切换时回调
- 动画速度全局系数：静态 double Speed；动画创建后 Duration 乘以 1/Speed；Speed=0 时跳过所有动画直接到终态

### 版本选择对话框（VersionDialog）
- 模态 Window（同样无边框圆角深色风格），宽 560 高 480
- 顶部：ToggleButton「显示快照版」；ListBox 列出 GameCore.GetVersionList()（后台线程加载，加载中显示旋转圈：旋转的 ArcSegment 或一个旋转的 "◌" 字符动画），每行：版本号 + 类型徽标（release=绿/snapshot=黄）+ 状态（已安装=灰字"已安装"，未安装=主题色"下载"按钮）
- 已安装行点击 → DialogResult 返回该 id
- 未安装行点「下载」→ 进入下载确认页（同窗口切换内容，见下）

### 下载确认页（DownloadConfirmView，嵌入 VersionDialog）
- 标题 "安装 {mcVersion}" + 说明 "预计下载约 700MB"
- 三行加载器（布局相同）：CheckBox + 名称（Forge/NeoForge/Fabric）+ 版本 ComboBox（对应 Loaders.GetXxxVersions，后台线程加载，失败显示"获取失败"）
- **互斥**：勾任一 CheckBox，另外两个自动取消并 IsEnabled=false；全不勾=原版
- 「开始安装」按钮 → 后台线程执行：GameCore.DownloadVanilla(entry, report)，若勾选则调对应 Loaders.Install*，全程 StepEvent 推给步骤面板（见下）；成功后 MessageBox 提示并返回最终版本 id
- 步骤面板（StepPanel 控件，ui/StepPanel.cs）：垂直步骤列表，每行：状态图标（○ 未开始灰 / 旋转动画 当前 / ✓ 绿 / ✗ 红）+ 步骤名 + 右侧 "cur/total  x.x MB/s" + 该行一个小 ProgressBar（Total=0 时 IsIndeterminate）。步骤出现顺序动态添加行（StepStart 时若 index 超出现有行数则新增一行）。StepDone(ok=false) 该行变红并 tooltip 显示错误
- 所有 StepEvent 从后台线程来，必须 Dispatcher.BeginInvoke 到 UI 线程

### 日志区
主窗口右侧栏底部（两个 Tab 之下共享）放一个高 90 的只读 TextBox（等宽字体 Consolas 9，深色，滚动到底），收 StepEvent.Kind.Log 和安装/启动日志。

### 动画清单（全部受 AnimSpeed 控制）
窗口入场、按钮 hover 颜色过渡、启动按钮呼吸光晕、按钮点击缩放、进度条平滑（ProgressBar 用动画过渡到新值而非直接赋值）、Tab 切换淡入、步骤图标旋转、列表加载旋转圈。

### 启动流程（主窗口）
1. 构造时 Config.Load()，填用户名/版本/设置值，应用主题与动画速度
2. 标题栏下方或版本小字实时刷新：当前版本 = Config.VersionId（若无则用最新已安装版本）
3. 「启动！！」点击：版本为空 → MessageBox 提示先去版本选择；否则后台线程 GameCore.Launch(versionId, username, cfg, log, instanceName: 若 Modrinth.GetInstanceLaunchId(versionId)!=null 则传 versionId 作为实例名) —— 简化：instanceName 恒传 null，整合包实例由版本选择对话框返回实例名处理（hil-instance.txt 存在时 Launch 用 GetInstanceLaunchId 的结果作为 id，实例名为目录名）
4. 关闭时 Config.Save()

## 验收

- `cmd //c "build.bat -out:HugeLauncher_ui_test.exe"` 编译零错误
- `--cli` 回归不受影响（不要动 CLI 分支逻辑）
- 代码注释用中文，风格与核心层一致
