# H史（AI 史蒂夫）施工图纸 — 26.8.3

本文档是 AI 史蒂夫功能的完整实现规格。分四期施工（P1~P4），每期结束必须编译验证
（0 error / 0 warning），最后版本号 26.8.2 → 26.8.3。

## 硬约束

- C# 5 语法（csc v4.0.30319）：禁止 `$""`、禁止 `?.`、禁止 `nameof`、禁止表达式体成员、
  禁止 `out var`。字符串拼接用 `+` 或 `string.Format`。
- JSON 解析用已引用的 `System.Web.Extensions`（JavaScriptSerializer），HTTP 用
  `HttpWebRequest`（参考 Modrinth.cs 现有写法）。不新增任何外部依赖。
- UI 文案全部中文。代码风格跟随周边文件。
- 编译：`cmd //c build.bat`（工作目录 minecraft 根）。编译前
  `taskkill //IM HugeInsaneLauncher.exe //F`（允许失败）。绝不杀 java 进程。
- 完工后启动 HugeInsaneLauncher.exe 交付。

## 总体架构

```
用户输入(聊天框) ─→ AiSteve(对话管理) ─→ AiClient(DeepSeek, OpenAI兼容, tool calling)
                         │                      │ tool_call
                         │                      ▼
                         │                AiTools(工具注册表) ─→ HIL 真实功能(后台执行)
                         │                      │
                         ▼                      ▼
                    气泡(回复展示)        SteveDirector(剧本演戏, 受 AiAnim 开关控制)
                                              │
                                        SteveView(IDLE⇄ACTING 状态机)
崩溃捕获(CrashReport) ─→ 错误分类器(AiRepair) ─→ 主动弹泡"需要我帮你解决吗?"
```

原则：AI 只做"听懂 + 选工具"，执行全是后台代码；界面上的点击/切页是演戏，
数据和操作是真实发生的。AI 永远接触不到文件系统和命令行，只能调注册过的工具。

## P1：基础设施

### 1. Config 新增字段（Infrastructure.cs，按现有 key=value 模式）

- `AiEnabled`（bool，默认 false）
- `AiApiKey`（string，默认空）
- `AiEndpoint`（string，默认 `https://api.deepseek.com/v1`）
- `AiAnim`（bool，默认 true，"H史拟人动画"开关）

### 2. src/ai/AiClient.cs（DeepSeek 客户端）

- OpenAI 兼容协议：POST `{endpoint}/chat/completions`，
  Header `Authorization: Bearer {apiKey}`，body JSON：
  `{ model: "deepseek-chat", messages: [...], tools: [...], tool_choice: "auto" }`
- messages 项：`{role:"system"|"user"|"assistant"|"tool", content, tool_calls?, tool_call_id?}`
- 解析响应：`choices[0].message` 里的 `content` 与 `tool_calls`
  （`tool_calls[i].function.name` / `.arguments` 是 JSON 字符串）。
- 单次请求超时 60s；网络/解析失败抛出带中文说明的异常（调用方兜底成气泡文案）。
- 全部用 JavaScriptSerializer + Dictionary<string,object> 构造/解析。

### 3. src/ai/AiTools.cs（工具注册表）

每个工具：`{ name, description(中文), parametersSchema(Dictionary), Execute(argsJson) }`，
Execute 返回 string（喂回 AI 的结果摘要）。本期注册以下工具：

| 工具名 | 说明 | 参数 | 实现依托 |
|---|---|---|---|
| search_resources | 搜索资源 | category(mod/modpack/resourcepack/shader/datapack), query, gameVersion可空, loader可空 | Modrinth.cs |
| install_resource | 安装资源到当前/指定版本 | projectId 或 slug, gameVersion, category | Modrinth.cs + 下载引擎 |
| list_versions | 列出已安装游戏版本 | 无 | GameCore.cs |
| download_game | 下载游戏版本 | version, loader可空(vanilla/forge/fabric/neoforge) | Loaders.cs + A_InstallPage 链路 |
| launch_game | 启动游戏 | version可空(默认当前选中) | 现有启动链路 |
| set_memory | 设置游戏内存MB | mb(512~32768) | Config |
| set_theme | 设置主题色 | color(十六进制如 #4A90D9) | ThemeManager |
| list_servers | 列出服务器实例与状态 | 无 | ServerManager.cs |
| start_server | 启动服务器 | name | ServerManager.cs |
| stop_server | 关闭服务器 | name | ServerManager.cs |
| read_crash | 读最近一次崩溃摘要 | 无 | CrashReport.cs |
| fix_mod_conflict | 修模组冲突 | (无,自动从崩溃报告抽取) | AiRepair.cs(P4 实装,P1 可先注册占位返回"尚未就绪") |
| fix_memory | 修内存不足 | 无 | AiRepair.cs(同上) |
| repair_game_files | 校验修复游戏文件 | version | 下载引擎 sha1 校验(可占位) |

占位工具在 P4 实装；P1 要求注册表、schema、分发机制完整可用。
重操作（下载游戏、安装资源）执行前必须经"确认"（见 P3 聊天气泡确认流，
AiSteve 里以 pendingConfirmation 状态实现：AI 回复中请求确认→用户回复同意类话语→继续）。

### 4. src/ai/AiSteve.cs（对话管理器，单例）

- 维护 messages 历史（上限保留最近 20 条，防 token 爆炸）
- system prompt：人设"H史"，驻在 HIL 底层的 AI 助手，语气简洁友好，中文回复；
  附上**状态快照**（已装版本列表、当前用户名、内存设置、主题色、服务器列表与状态）
- 工具调用循环：发请求 → 若有 tool_calls 则逐个执行 AiTools → 结果以 role:tool 回填 →
  再发请求，直到拿到纯文本回复（循环上限 6 轮防失控）
- 全程后台线程执行，回调到 UI 线程更新气泡/触发射本
- 事件：`ReplyReceived(string)`、`ToolStarting(string toolName, string argsJson)`、
  `ToolFinished(string toolName, string result)`、`ErrorOccurred(string)`

## P2：史蒂夫状态机 + 剧本引擎

### 1. SteveView.cs 改造

- 加 `enum SteveMode { Idle, Acting }` 与 `Mode` 属性（默认 Idle）
- 姿态插值系统保持单一写入源：Idle 时现有 PreviewMouseMove 写 targetYaw/targetPitch；
  Acting 时鼠标处理器**直接 return**，由 `AiGaze(double yaw, double pitch)` 写入
- `AiGazeToControlPoint(FrameworkElement target)`：把控件中心换算成 yaw/pitch
  （复用现有 rx*120/ry*35 的换算与限幅）
- 现有 `Swing()` 保持 public，剧本直接调
- ACTING 进出都有平滑过渡（现有插值天然平滑，不需额外补间）

### 2. src/ai/SteveDirector.cs（剧本引擎，单例）

- 剧本 = `List<ScriptStep>`；步骤类型：
  `Navigate(pageKey)` / `LookAt(FrameworkElement)` / `Swing` / `Say(string)`（弹气泡）/
  `WaitFor(pred, timeoutMs)` / `Run(Action)`（真实执行，后台）
- `Play(script, onDone)`：串行执行；`Cancel()`：中断当前剧本
- **try/finally 硬保证**：剧本无论成功/失败/取消，结束必须 SteveView.Mode=Idle
  并撤掉操作锁（见 P3）
- 多工具连续调用时剧本**排队**，绝不并行
- 每个工具在 AiTools 里声明自己的剧本（如 search_resources：Navigate 模组页 →
  LookAt 搜索框 → Swing → 真实搜索 → Say 结果摘要）
- 长任务（下载）：剧本步骤 `WatchProgress`，执行期间 LookAt 停在进度控件上
- **AiAnim=false 时**：SteveDirector.Play 直接只执行 Run/真实逻辑，跳过全部
  Navigate/LookAt/Swing，Say 保留（气泡汇报必须有）

## P3：聊天 UI + 设置项 + 操作锁

### 1. 聊天输入框（MainWindow 左侧栏）

- 位置：用户名输入框正下方，同宽同高同风格（黑底白字、圆角跟随现有输入框）
- 仅当 Config.AiEnabled=true 时显示（动态显隐，不用重启）
- 回车发送 → AiSteve.Send(text)；发送中输入框禁用并显示"思考中…"占位

### 2. 回复气泡

- 主题色底（ThemeManager.CurrentColor）+ 白字，圆角，位于聊天框上方，最大宽同左栏，
  文字自动换行，超长时最多 6 行 + 省略
- 进场：下滑动画（Ani AaY + AaOpacity，用 ThemeManager.Dur 的时长）
- 每条气泡带 ✕（右上角小字）：点击向左淡出关闭（AaX 负向 + AaOpacity）
- 新回复出现时旧气泡以同样动画自动关闭，场上最多一条
- 确认流：AI 请求确认时气泡末尾带"好/算了"两个小按钮（主题色描边），
  点击等价于用户回复同意/拒绝

### 3. 启动器设置页（SettingsPanel.cs 或对应设置页文件）

新增一组"AI 史蒂夫"：
- CheckBox「开启AI史蒂夫」↔ Config.AiEnabled
- Label+TextBox「API Key」：AiEnabled=false 时禁用（灰底），勾选后启用 ↔ Config.AiApiKey
- Label+TextBox「API 端点」↔ Config.AiEndpoint（默认 https://api.deepseek.com/v1）
- CheckBox「H史拟人动画」↔ Config.AiAnim（AiEnabled=false 时同样禁用）
改动即存 Config.Save()。

### 4. 操作锁（ACTING 期间）

- SteveDirector.Play 开始时：主窗口盖透明拦截层（WinForms 透明 Panel 置顶或
  窗体级 PreviewMouseDown 拦截），所有鼠标按下被吞掉（到不了按钮=全场禁用）
- 拦截层带浅遮罩 + 主题色小字"H史操作中…（点击任意处可打断）"
- 任何点击（含聊天框、含 ✕）：Cancel() 当前剧本 → 撤拦截层 → 归还 IDLE
- 中断善后：下载任务保留已下部分（断点）；已完成的步骤不回滚；
  删除类动作永远排在剧本最后一步

## P4：错误分类器 + 修复动作 + 崩溃弹泡

### 1. src/ai/AiRepair.cs

- 正则分类器（机读优先，不信 AI 猜）：
  - `requires .* between|incompatible with|Mixin apply failed` → 模组冲突
  - `OutOfMemoryError: Java heap space` → 内存不足（fix_memory：Xmx +1024，上限物理内存 80%）
  - `Unsupported class file version|A JNI error` → Java 版本错
  - `requires .*fabric-api|missing dependency` → 缺前置（Modrinth 查依赖自动下）
- fix_mod_conflict：从崩溃报告抽冲突对（模组A 要求 模组B 的版本区间）→
  Modrinth `/v2/project/{slug}/version?game_versions=[..]&loaders=[..]` 枚举双方候选 →
  取满足区间的最新交集版本 → 下载替换（走下载引擎）→ 气泡汇报
- 本地 jar → Modrinth 项目：优先 `/v2/version_file`（sha1 反查），失败退回文件名启发式
- 分类不了的交给 AI 读崩溃摘要，从动作库选；动作库没有的，AI 生成图文解释
  （正版验证、硬件、对面服务器问题=只解释不修）

### 2. 崩溃主动弹泡

- CrashReport.cs 捕获到崩溃后：若 AiEnabled，气泡"H史：你遇到了游戏崩溃！
  需要我帮你解决吗？"+ 确认按钮；同意 → AiRepair 分类 → 对应动作 + 剧本演戏
- 若 AiAnim 开：修复期间 ACTING 演戏（看向日志区/下载进度等）

## 收尾

- 版本号 26.8.2 → 26.8.3（搜 "26.8.2" 全替换，含标题栏与关于信息）
- `cmd //c build.bat` 0 error 0 warning
- 启动 HugeInsaneLauncher.exe 验证：设置页出现 AI 组；勾选后聊天框出现；
  无 key 时发消息有友好错误气泡；勾选/取消拟人动画即时生效
