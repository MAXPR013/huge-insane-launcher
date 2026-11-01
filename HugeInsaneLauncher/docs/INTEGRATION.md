# HIL 多子代理并行开发 · 集成契约（必读，违反会被整合拒绝）

项目：Huge Insane Launcher（HIL），单文件 WPF 启动器。工作目录根：`C:/Users/Administrator/Documents/minecraft`。

## 0. 品牌

- 用户可见名称一律是 **"Huge Insane Launcher"**（简称 HIL）：窗口标题、关于、Toast、报错文案（例："HIL 找不到可用的游戏版本"）。
- 内部命名空间**不改**：`HugeLauncher`（核心层）、`HugeLauncher.Ui`（界面层）、`HugeLauncher.Ui.Pages`（页面层）。

## 1. 编译环境铁律（老 csc.exe = C# 5 语法子集）

编译器是 `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`，只支持 C# 5：

- **禁止**：字符串插值 `$"..."`、`nameof`、`?.` / `?[]`、`out var`、表达式体成员（`=> expr` 属性/方法）、元组、局部函数、`var` 字典初始化器语法糖。
- 字符串拼接用 `+` 或 `string.Format`。
- `Dictionary` 取可能不存在的 key **一律 `TryGetValue`**，直接索引会崩。
- 匿名 `delegate { ... }` 后面**不能**直接跟对象初始化器 `{ X = 1 }`。
- 写 JSON / .json / .mrpack 内文件等**给 Java 读的文件**，必须 `new UTF8Encoding(false)`（无 BOM）。
- 后台线程（Thread/ThreadPool/网络回调）里碰 UI 元素，必须 `Dispatcher.BeginInvoke`。
- 命名空间：页面代码放 `HugeLauncher.Ui.Pages`，复用核心层 `using HugeLauncher;`，复用控件 `using HugeLauncher.Ui;`。

## 2. 文件纪律（防并行冲突）

- **只能新建你自己前缀的文件**，放在 `HugeInsaneLauncher/src/ui/pages/` 下。
- **禁止修改任何已存在的文件**（包括但不限于：`src/` 全部核心层、`src/ui/` 下的 Widgets.cs / ThemeManager.cs / StepPanel.cs / TaskCenter.cs / IconCache.cs / pages/PageBase.cs、build.bat）。缺什么能力就在自己文件里写私有辅助。
- 禁止读取/改动其他子代理前缀的文件（它们可能正在被写）。

## 3. 编译自测方法（重要，别用 build.bat）

`build.bat` 是 `-recurse` 全量编译，会把其他子代理的半成品文件编进来互相炸。自测**必须显式列文件**，且输出到你自己名字的 exe。模板（Git Bash 里跑）：

```bash
cmd //c "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" -nologo -target:winexe -out:TestA.exe \
  -lib:"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF" \
  -r:PresentationFramework.dll -r:PresentationCore.dll -r:WindowsBase.dll \
  -r:System.Web.Extensions.dll -r:System.Xml.dll -r:System.IO.Compression.dll \
  -r:System.IO.Compression.FileSystem.dll -r:System.Windows.Forms.dll -r:System.Drawing.dll -r:System.Xaml.dll \
  HugeInsaneLauncher\src\Infrastructure.cs HugeInsaneLauncher\src\GameCore.cs HugeInsaneLauncher\src\Loaders.cs \
  HugeInsaneLauncher\src\Modrinth.cs HugeInsaneLauncher\src\LocalMods.cs \
  HugeInsaneLauncher\src\ui\Widgets.cs HugeInsaneLauncher\src\ui\ThemeManager.cs \
  HugeInsaneLauncher\src\ui\TaskCenter.cs HugeInsaneLauncher\src\ui\IconCache.cs \
  HugeInsaneLauncher\src\ui\pages\PageBase.cs \
  HugeInsaneLauncher\src\ui\pages\<你的文件1>.cs HugeInsaneLauncher\src\ui\pages\<你的文件2>.cs
```

- 注意：传源文件路径**用反斜杠**（MSYS 会把 `/` 路径弄坏）；上面这条在 Git Bash 里可整体加引号执行。
- `Program.cs` / `MainWindow.cs` 等启动文件**不要**编进来（会和别人的冲突；你的页面不需要入口也能编译，winexe 缺 Main 只会警告不报错——若要 0 警告可 `-target:library -out:TestA.dll`）。
- 自测编译通过后**删除你的 Test*.exe / Test*.dll**。

## 4. 页面契约（src/ui/pages/PageBase.cs，已实现，禁改）

```csharp
class PageBase : Grid {
    public Config Cfg;                                  // 主窗口注入
    public virtual void OnEnter();                      // 切到前台时刷新数据/启动后台加载
    public virtual void OnLeave();
    public static void Toast(string msg);               // 轻提示
}
static class Navigator {
    public static void Go(string pageKey);
    public static void Go(string pageKey, object param);
}
```

- 你的每个页面类继承 `PageBase`，构造函数**无参**（主窗口统一 new 后赋 Cfg）。
- 页面间跳转**只准 `Navigator.Go`**，禁止 new 别的页面类。

### 页面 key 与 param 约定

| key | 页面 | param |
|---|---|---|
| `launch` | 启动主页（主窗口自带） | - |
| `settings` | 启动器设置（主窗口自带） | - |
| `download:install` | 原版游戏下载页【A】 | - |
| `download:speed` | 下载管理页【A】 | - |
| `download:mod` / `download:pack` / `download:datapack` / `download:resourcepack` / `download:shader` | 社区资源页【B】 | - |
| `download:detail` | 资源详情页【B】 | `Modrinth.SearchHit` 对象 + 来源 key（见下） |
| `instance:overall` / `instance:setup` / `instance:mod` / `instance:export` | 版本设置页【C】 | `string` = 版本 id（如 `26.2` 或 `fabric-loader-0.19.3-26.2`） |

- 主窗口在 `OnEnter()` 前把 param 赋给 `page.NavParam`；页面在 OnEnter 里读 `NavParam`（可能为 null 或类型不符，须防御）。
- 页面内要启动游戏：调 `PageBase.RequestLaunch(versionId)`（主窗口统一走启动流程），不要自己调 GameCore.Launch。
- `download:detail` 的 param 是一个 `object[]`：`[0]=SearchHit, [1]=来源页面key(字符串，返回用)`。
- 页面会被缓存复用、反复 OnEnter，别在构造函数里拉网络。

### 版本设置（C）与主窗口的启动联动约定

- C 在自己文件里实现 `static class VersionSettings`（namespace `HugeLauncher.Ui.Pages`），暴露：
  ```csharp
  public static bool TryLoad(string versionId, out int memoryMb, out string priority);
  ```
  存取格式自定（建议版本目录下 `hil.version.json`，无 BOM UTF8）。主窗口启动游戏时会调它做每版本覆盖；未存过则返回 false。

## 5. 下载任务契约（src/ui/TaskCenter.cs，已实现，禁改）

任何"开始下载"动作：

```csharp
var reporter = TaskCenter.Register("任务显示名");       // 返回 Action<StepEvent>
// 后台线程里调核心层，把 reporter 传进去：
ThreadPool.QueueUserWorkItem(delegate {
    try {
        GameCore.DownloadVanilla(ve, reporter);          // 或 Loaders.InstallFabric(..., reporter) 等
        TaskCenter.Complete(item?, true, "");            // 见下
    } catch (Exception ex) { /* 失败 */ }
});
```

- `TaskCenter.Register` 返回 reporter，但**不返回 item**；要拿 item 就 `lock (TaskCenter.Tasks)` 后找第一个 `Name` 匹配且 `!Finished` 的（Register 是 Insert(0)，最新在最前）。
- 成功/失败结束调 `TaskCenter.Complete(item, success, message)`。
- 下载管理页监听 `TaskCenter.Changed` 事件（**后台线程触发**，UI 刷新须 Dispatcher.BeginInvoke），读 `TaskCenter.Tasks` 列表渲染；左栏统计用 `TaskCenter.Overall(out progress, out mbps, out remainingFiles)`。
- `DownloadTaskItem`：Name / Steps(List<TaskStep>) / Finished / Failed / FailMessage / Progress(0-1) / TotalMbps。`TaskStep`：Index / Name / Cur / Total(0=不确定) / Mbps / State(0等待 1进行 2完成 3失败) / Error。

## 6. 图标契约（src/ui/IconCache.cs，已实现，禁改）

```csharp
string path = IconCache.Get(hit.IconUrl, delegate(string url, string p) {
    // 后台线程！Dispatcher.BeginInvoke 回 UI 把 Image.Source 换掉
});
// 返回值：已缓存=本地路径（直接用）；未缓存=null（先用默认占位图，等回调）
```

## 7. 核心层 API 速查（namespace HugeLauncher，全部 static 可直接调）

### Config（src/Infrastructure.cs）
```csharp
cfg.Username; cfg.UserHistory;           // List<string>，cfg.PushUserHistory(name)
cfg.VersionId;                           // 当前选中版本 id
cfg.ShowSnapshots;                       // 是否显示快照版
cfg.MemoryMb; cfg.Priority;              // "高"/"高于正常"/"正常"
cfg.AnimSpeed; cfg.Theme;                // "绿/蓝/紫/橙/红/白"
cfg.Save();
```

### GameCore（src/GameCore.cs）
```csharp
class VersionEntry { public string Id, Type, Url; }        // Type: release/snapshot/old_alpha 等
List<VersionEntry> GameCore.GetVersionList();              // 官方版本清单（网络）
List<string> GameCore.GetInstalledVersions();              // 已装版本 id
bool GameCore.IsVersionReady(string id);                   // 文件齐不齐
void GameCore.DownloadVanilla(VersionEntry ve, Action<StepEvent> report);
Process GameCore.Launch(string id, string username, Config cfg, Action<string> log, string instanceName /*可null*/);
```

### Loaders（src/Loaders.cs）
```csharp
class LoaderVersion { public string Version, Label; }
List<LoaderVersion> Loaders.GetFabricLoaderList(string mcVersion);
List<LoaderVersion> Loaders.GetForgeVersions(string mcVersion);       // Label 已是友好显示名
List<LoaderVersion> Loaders.GetNeoForgeVersions(string mcVersion);
// 三个 Install* 返回最终版本 id（用于 Cfg.VersionId / Launch）；report 传 TaskCenter 的 reporter
string Loaders.InstallFabric(string mcVersion, string loaderVersion, Action<StepEvent> report);
string Loaders.InstallForge(string mcVersion, string forgeVersion, Action<StepEvent> report);
string Loaders.InstallNeoForge(string mcVersion, string neoVersion, Action<StepEvent> report);
```

### Modrinth（src/Modrinth.cs）
```csharp
class SearchHit { public string ProjectId, Slug, Title, Author, Description, IconUrl; public long Downloads; }
// projectType: "mod" / "modpack" / "resourcepack" / "shader"；数据包也用 "datapack"（Modrinth 支持，若空结果降级提示）
List<SearchHit> Modrinth.Search(string projectType, string query, string gameVersion);
// gameVersion 传 "" = 不限版本；传具体版本则服务端过滤。失败抛 HglException（消息已中文化）
string Modrinth.DetectLoader(string versionId);            // 由已装版本 id 推 "fabric"/"forge"/"neoforge"/"vanilla"
// 安装资源到指定版本实例目录（instanceName 必须传版本 id，游戏实际从 instances/<versionId>/mods 读 Mod；传 null 会崩）；
// 找不到匹配版本抛 HglException("HIL 找不到可用的游戏版本"字样)；末参是 Action<string> log（不是 StepEvent）
string Modrinth.InstallResource(string projectType, string projectId, string gameVersion, string loader, string instanceName, Action<string> log);
string Modrinth.InstallModpack(string projectId, string packName, Action<StepEvent> report);   // 返回实例名
string Modrinth.GetInstanceLaunchId(string instanceName);
```
> 注：以上签名为整合前快照，**以源码为准**；用前先 Read 对应方法确认参数个数。

### LocalMods（src/LocalMods.cs）
```csharp
class LocalMod { public string FilePath, FileName, DisplayName, Version, Description, IconPath; public bool Enabled; }
List<LocalMod> LocalMods.Scan(string instanceName);        // 传版本 id（扫 instances/<id>/mods，与启动目录一致）；传 null 会崩
void LocalMods.SetEnabled(LocalMod m, bool enabled);       // 改名 .jar <-> .jar.disabled
void LocalMods.Delete(LocalMod m);
int LocalMods.InstallFromFiles(string instanceName, string[] files);   // 复制 jar 进 mods，返回成功数
```

### StepEvent（进度事件，核心层 → reporter）
```csharp
StepEvent.Kind: StepStart / StepProgress / StepDone / Log
e.StepIndex, e.StepName, e.Current, e.Total, e.MbPerSec, e.Success, e.Message
```

## 8. 可复用 UI 资源（namespace HugeLauncher.Ui，已编译验证）

- **Palette**：`BgTop/BgBottom/Panel/Input(纯黑)/Btn/BtnHover/Text(白)/Dim/Line/Green/Yellow/Red/Dark`，`Palette.B(color)` 画刷，`Palette.Lighten(c,f)`，`Palette.WindowBg()`。
- **ThemeManager**：`Accent`（共享强调色画刷，直接引用即可随主题变色）、`CurrentColor`、`Dur(ms)`（动画时长，尊重动画速度）、`AnimateColor(brush,to,ms)`、`AnimateDouble(target,prop,to,ms)`、`AnimOn`。所有动画必须走它。
- **FlatButton(text)**：`Click` 事件、`SetColors/SetBorder/SetPadding/SetFontSize/SetBold/SetEnabled`、`SetToggle+SetChecked(v,raise)+CheckedChanged`。
- **CircleButton(text, diameter, bigButton)**：圆形按钮，`Click`、`SetEnabled`、`EnableGlow()`（仅 big）。左栏"启动！！"用大圆钮（直径 96，big=true）。
- **MiniCheckBox**：无参构造 `new MiniCheckBox()`（标签另放 TextBlock），`SetChecked(v, raise)`、`Changed` 事件、`SetEnabled`、`IsChecked`。
- **DarkTabControl**：`AddTab(header, contentUIElement)`、`Select(idx)`。自带切换淡入。
- **DarkComboBox**：标准 ComboBox 用法（Items.Add(string)、SelectedIndex、SelectionChanged、IsEditable）。
- **DarkListBox**：标准 ListBox 用法。
- **HintTextBox(hint)**：占位提示输入框，`.Text` 读写、`.Box` 拿内部 TextBox。
- **StepPanel**（src/ui/StepPanel.cs）：步骤进度条控件，`Reset()`、`Handle(StepEvent)`。下载详情可用它（`new StepPanel()`，UserControl）。

## 9. 视觉规范（PCL 卡片风 + 我方深色主题）

- 页面布局：根 Grid 一行，内放 ScrollViewer + StackPanel（卡片纵向排列，间距 10，页边距 16）。
- **卡片**：`Border`，`Background=Palette.Panel`，`CornerRadius=8`，`Padding=14`，可做折叠卡（点击标题行展开/收起内容，动画用 ThemeManager）。
- 卡片标题 13px SemiBold（Palette.Text），说明文字 11~12px Palette.Dim，正文 12px Palette.Text。
- **文本框一律黑底白字**：`Background=Palette.B(Palette.Input)`（已是纯黑）、`Foreground=Palette.B(Palette.Text)`、CaretBrush 白色。直接用 HintTextBox/DarkComboBox 已符合。
- 列表项悬停提亮 `Palette.BtnHover`；选中态用 `ThemeManager.Accent`。
- 图标（PCL 风方块贴图，16x16）在 `HugeInsaneLauncher/Images/Blocks/`（Grass/CommandBlock/GoldBlock/CobbleStone/Anvil/RedstoneBlock/Egg/CraftingTable/Furnace/GrassPath.png）与 `Images/Loaders/`（Fabric.png、NeoForge.png、Anvil 用 Blocks/Anvil.png）。加载：
  ```csharp
  var img = new Image();
  img.Source = new BitmapImage(new Uri(Paths.LauncherDir + "/Images/Blocks/Grass.png", UriKind.Absolute));
  img.Width = 16; img.Height = 16;
  ```
- PCL 蓝本规格文档（**照它仿制**，在 `HugeInsaneLauncher/docs/ref/`）：`PCL_下载界面规格.md`、`PCL_版本设置规格.md`。PCL 源码快照在 `docs/ref/PCL-main/`（要深挖再查）。

## 10. 常见坑

- WPF 控件只能在 UI 线程创建；后台线程拿到数据后用 `Dispatcher.BeginInvoke` 回 UI 建树。
- `Image.Source` 用文件路径时要 `new BitmapImage(new Uri(绝对路径))`；相对路径会按程序基目录解析出错。
- 网络调用（GameCore.GetVersionList / Loaders.Get* / Modrinth.Search）全部可能抛异常且慢，**必须后台线程 + try/catch**，失败 `PageBase.Toast` 或页面内错误条。
- 版本 id 里可能含 `/` 之外的非法字符已由核心层处理，页面层直接当字符串用。
- 页面会被反复 OnEnter（切页回来），加载逻辑要幂等（已加载过就别重复拉网络，或提供刷新按钮）。
