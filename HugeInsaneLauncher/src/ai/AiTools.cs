// HugeLauncher.Ai - 工具注册表：AI 只能调用这里注册过的工具（永远接触不到文件系统/命令行）
// 每个工具声明：名称/中文描述/参数 schema/是否需要确认/自己的剧本。
// 剧本统一三段式拟人节奏：LookAt(看目标) → DoSwing(挥手) → Navigate/Run（UI 真实响应）；
// 目标不在视野时由页面 FindXxx 方法内部先滚动（看滚动区，不挥手）。
// Execute 产物 = 剧本里 Run 步骤返回的 string（喂回 AI 的结果摘要）。
using System;
using System.Collections.Generic;
using System.Windows;
using HugeLauncher.Ui;
using HugeLauncher.Ui.Pages;

namespace HugeLauncher.Ai
{
    class AiTool
    {
        public string Name, Description;
        public Dictionary<string, object> Schema;                              // parameters JSON schema
        public bool NeedConfirm;                                               // 重操作执行前必须经气泡确认
        public Func<Dictionary<string, object>, bool> NeedConfirmIf;           // 按参数动态判定（危险命令等）；null = 不用
        public Func<Dictionary<string, object>, string> ConfirmText;           // 确认气泡文案
        public Func<Dictionary<string, object>, List<ScriptStep>> BuildScript; // 工具自己的剧本
    }

    static class AiTools
    {
        public static Config Cfg = null;   // 主窗口持有的共享配置实例（启动时接线）

        static readonly List<AiTool> tools = new List<AiTool>();
        static readonly Dictionary<string, AiTool> byName = new Dictionary<string, AiTool>();

        static AiTools()
        {
            RegisterSearch();
            RegisterInstallResource();
            RegisterListVersions();
            RegisterDownloadGame();
            RegisterLaunchGame();
            RegisterSetMemory();
            RegisterSetTheme();
            RegisterListServers();
            RegisterStartServer();
            RegisterStopServer();
            RegisterReadServerConsole();
            RegisterSendServerCommand();
            RegisterReadCrash();
            RegisterFixTools();
            RegisterRepairGameFiles();
        }

        public static AiTool Find(string name)
        {
            AiTool t;
            return name != null && byName.TryGetValue(name, out t) ? t : null;
        }

        // OpenAI 格式：[{type:"function", function:{name, description, parameters}}]
        public static List<Dictionary<string, object>> SchemasForApi()
        {
            var list = new List<Dictionary<string, object>>();
            foreach (var t in tools)
            {
                var fn = new Dictionary<string, object>();
                fn["name"] = t.Name;
                fn["description"] = t.Description;
                fn["parameters"] = t.Schema;
                var wrap = new Dictionary<string, object>();
                wrap["type"] = "function";
                wrap["function"] = fn;
                list.Add(wrap);
            }
            return list;
        }

        // ---------- 小助手 ----------

        static Dictionary<string, object> Prop(string type, string desc, string enumCsv)
        {
            var p = new Dictionary<string, object>();
            p["type"] = type;
            p["description"] = desc;
            if (enumCsv != null) p["enum"] = new List<string>(enumCsv.Split(','));
            return p;
        }

        static Dictionary<string, object> Schema(string[][] props, params string[] required)
        {
            var ps = new Dictionary<string, object>();
            foreach (var p in props)
                ps[p[0]] = Prop(p[1], p[2], p.Length > 3 ? p[3] : null);
            var s = new Dictionary<string, object>();
            s["type"] = "object";
            s["properties"] = ps;
            s["required"] = new List<string>(required);
            return s;
        }

        static string ArgStr(Dictionary<string, object> args, string key)
        {
            object v;
            if (args != null && args.TryGetValue(key, out v) && v != null)
            {
                string s = v.ToString().Trim();
                if (s.Length > 0) return s;
            }
            return null;
        }

        static long ArgLong(Dictionary<string, object> args, string key, long def)
        {
            object v;
            if (args != null && args.TryGetValue(key, out v) && v != null)
            {
                long n;
                if (long.TryParse(v.ToString(), out n)) return n;
            }
            return def;
        }

        static void Register(AiTool t)
        {
            tools.Add(t);
            byName[t.Name] = t;
        }

        // 三段式第一步：看 UiMap 里的目标
        static ScriptStep Gaze(string uiMapKey)
        {
            return ScriptStep.LookAt(delegate { return UiMap.Find(uiMapKey); });
        }

        // 在 UI 线程同步执行（Run 步骤跑在剧本工作线程上，碰 UI 必须封送）
        static void OnUi(Action a)
        {
            var app = Application.Current;
            if (app == null || app.Dispatcher.CheckAccess()) { a(); return; }
            app.Dispatcher.Invoke(a);
        }

        // 带返回值的 UI 封送（.NET 4.0 没有 Dispatcher.Invoke<T>，走 Delegate 重载）
        static T OnUiGet<T>(Func<T> f)
        {
            var app = Application.Current;
            if (app == null || app.Dispatcher.CheckAccess()) return f();
            return (T)app.Dispatcher.Invoke(new Func<T>(f));
        }

        static PageDownloadInstall InstallPage()
        {
            var f = SteveDirector.PageFinder;
            return f == null ? null : f("download:install") as PageDownloadInstall;
        }

        static PageResourceBase ResPage(string pageKey)
        {
            var f = SteveDirector.PageFinder;
            return f == null ? null : f(pageKey) as PageResourceBase;
        }

        static PageResourceDetail DetailPage()
        {
            var f = SteveDirector.PageFinder;
            return f == null ? null : f("download:detail") as PageResourceDetail;
        }

        static string PageKeyFor(string category)
        {
            switch (category)
            {
                case "modpack": return "download:pack";
                case "resourcepack": return "download:resourcepack";
                case "shader": return "download:shader";
                case "datapack": return "download:datapack";
                default: return "download:mod";
            }
        }

        static string TypeNameFor(string category)
        {
            switch (category)
            {
                case "modpack": return "整合包";
                case "resourcepack": return "资源包";
                case "shader": return "光影包";
                case "datapack": return "数据包";
                default: return "Mod";
            }
        }

        // 二级侧栏标签名（UiMap 的 sub: key）
        static string SubLabelFor(string category)
        {
            switch (category)
            {
                case "modpack": return "整合包";
                case "resourcepack": return "资源包";
                case "shader": return "光影包";
                case "datapack": return "数据包";
                default: return "模组";
            }
        }

        // ---------- 工具注册 ----------

        // search_resources：看下载标签→挥手→切页；看二级标签→挥手→切换；看搜索框→挥手→真实填词触发搜索→汇报
        static void RegisterSearch()
        {
            var t = new AiTool
            {
                Name = "search_resources",
                Description = "在 Modrinth 搜索社区资源（模组/整合包/资源包/光影/数据包）",
                Schema = Schema(new[]
                {
                    new[] { "category", "string", "资源类型", "mod,modpack,resourcepack,shader,datapack" },
                    new[] { "query", "string", "搜索关键词（英文效果更好）", null },
                    new[] { "gameVersion", "string", "游戏版本筛选，如 1.21.1，可空", null },
                    new[] { "loader", "string", "加载器筛选，可空", "vanilla,forge,fabric,neoforge" },
                }, "category", "query"),
                BuildScript = delegate(Dictionary<string, object> args)
                {
                    string cat = ArgStr(args, "category") ?? "mod";
                    string query = ArgStr(args, "query") ?? "";
                    string gv = ArgStr(args, "gameVersion");
                    string key = PageKeyFor(cat);
                    string summary = null;
                    return new List<ScriptStep>
                    {
                        Gaze("tab:下载"),
                        ScriptStep.DoSwing(),
                        ScriptStep.Navigate(key),
                        Gaze("sub:" + SubLabelFor(cat)),
                        ScriptStep.DoSwing(),
                        ScriptStep.Navigate(key),   // 同页重进 = 刷新数据（幂等）
                        Gaze("res:search:" + cat),
                        ScriptStep.DoSwing(),
                        ScriptStep.Run(delegate
                        {
                            // 界面真实填词触发搜索（用户能看到列表刷新）
                            OnUi(delegate
                            {
                                var page = ResPage(key);
                                if (page != null) page.AiSearch(query);
                            });
                            // 摘要数据自己查一份（页面列表是异步填的，不等它）
                            var hits = Modrinth.Search(cat, query, gv);
                            if (hits.Count == 0)
                            {
                                summary = "没搜到相关的" + TypeNameFor(cat) + "，换个关键词试试？";
                                return summary;
                            }
                            var sb = new System.Text.StringBuilder();
                            sb.Append("搜索到 ").Append(hits.Count).Append(" 个结果，前 5 个：");
                            int n = Math.Min(5, hits.Count);
                            for (int i = 0; i < n; i++)
                            {
                                var h = hits[i];
                                sb.Append("\n").Append(i + 1).Append(". ").Append(h.Title)
                                    .Append("（下载 ").Append(ResourceUiUtil.FormatDownloads(h.Downloads))
                                    .Append("，slug: ").Append(h.Slug).Append("）");
                            }
                            summary = "帮你搜到啦！最热门的是「" + hits[0].Title + "」，共 " + hits.Count
                                + " 个结果，列表已经摆在你面前了。";
                            return sb.ToString();
                        }),
                        ScriptStep.SayFunc(delegate { return summary ?? "搜索完成。"; }),
                    };
                },
            };
            Register(t);
        }

        // install_resource 七幕剧本：看"下载"标签→挥手→切页 → 看搜索框→挥手→真实填词搜索 →
        // 等结果→看首张卡→挥手→真实进详情页 → 等分组加载完 → 滚动到目标「加载器+游戏版本」分组
        // （头跟滚动走，不挥手）→ 对准分组标题挥手 → 真实展开发布列表 → 滚动到文件行（不挥手）→
        // 挥手 → 真实触发下载（InstallFile 自带跳下载管理页）→ WatchProgress 盯悬浮球到完成 → 汇报。
        // 整合包例外：不装进实例，到详情页选最新文件下载导入为止。
        static void RegisterInstallResource()
        {
            var t = new AiTool
            {
                Name = "install_resource",
                Description = "把 Modrinth 上的资源（模组/资源包/光影/数据包）安装到指定游戏版本；整合包则下载导入为新实例",
                NeedConfirm = true,
                ConfirmText = delegate(Dictionary<string, object> args)
                {
                    return "我准备把「" + (ArgStr(args, "projectId") ?? ArgStr(args, "slug") ?? "?")
                        + "」安装到版本「" + (ArgStr(args, "gameVersion") ?? "?") + "」，要现在开始下载安装吗？";
                },
                Schema = Schema(new[]
                {
                    new[] { "projectId", "string", "Modrinth 项目 ID 或 slug（优先用 search_resources 拿到的 slug）", null },
                    new[] { "gameVersion", "string", "目标游戏版本 id（启动器里的版本名，不是 MC 版本号也可）；整合包可空", null },
                    new[] { "category", "string", "资源类型", "mod,modpack,resourcepack,shader,datapack" },
                }, "projectId", "category"),
                BuildScript = delegate(Dictionary<string, object> args)
                {
                    string pid = ArgStr(args, "projectId") ?? ArgStr(args, "slug");
                    string gv = ArgStr(args, "gameVersion");
                    string cat = ArgStr(args, "category") ?? "mod";
                    bool isPack = cat == "modpack";
                    string key = PageKeyFor(cat);
                    // 运行时解析（二幕 Run 填）：目标实例 id / MC 基版本 / 加载器 / 首张命中 / 选中文件 / 任务条目
                    string targetId = null, mcVer = null, loaderName = null;
                    Modrinth.SearchHit hit = null;
                    Modrinth.MrVersion pickedFile = null;
                    DownloadTaskItem item = null;
                    return new List<ScriptStep>
                    {
                        // 一幕：看"下载"标签→挥手→切页
                        Gaze("tab:下载"),
                        ScriptStep.DoSwing(),
                        ScriptStep.Navigate(key),
                        // 二幕：看搜索框→挥手→真实填词搜索（顺便解析目标实例，先报错再演戏）
                        Gaze("res:search:" + cat),
                        ScriptStep.DoSwing(),
                        ScriptStep.Run(delegate
                        {
                            if (string.IsNullOrEmpty(pid)) throw new Exception("缺少项目 id");
                            if (!isPack)
                            {
                                // 版本解析（Bug4 那套）：AI 常传纯 MC 版本号而非实例 id。
                                // 为空默认当前选中；不是已装实例 id 时按"实例底座版本 == 传入值"映射（当前选中优先）
                                string target = gv;
                                if (string.IsNullOrEmpty(target) && Cfg != null) target = Cfg.VersionId;
                                if (string.IsNullOrEmpty(target))
                                    throw new Exception("没说装到哪个版本，当前也没有选中版本");
                                if (!GameCore.IsVersionReady(target))
                                {
                                    string mapped = null;
                                    string cur = Cfg != null ? Cfg.VersionId : null;
                                    if (!string.IsNullOrEmpty(cur) && GameCore.IsVersionReady(cur)
                                        && GameCore.BaseGameVersion(cur) == target)
                                        mapped = cur;
                                    else
                                    {
                                        foreach (var id in GameCore.GetInstalledVersions())
                                        {
                                            if (GameCore.BaseGameVersion(id) == target) { mapped = id; break; }
                                        }
                                    }
                                    if (mapped == null)
                                        throw new Exception("版本 " + target + " 还没有安装，先下载它吧");
                                    target = mapped;
                                }
                                targetId = target;
                                mcVer = GameCore.BaseGameVersion(targetId);
                                loaderName = Modrinth.DetectLoader(targetId);
                                // 原版装模组不会生效：不默默装，把问题抛回给用户
                                if (loaderName == "vanilla" && cat == "mod")
                                    throw new Exception("「" + targetId + "」是原版，没装加载器，模组不会生效；要我帮你装对应加载器吗");
                            }
                            OnUi(delegate
                            {
                                var page = ResPage(key);
                                if (page != null) page.AiSearch(pid);
                            });
                            return "已在界面搜索 " + pid;
                        }),
                        // 三幕：等结果→看首张卡（先滚动）→挥手→真实进详情页
                        ScriptStep.Wait(delegate
                        {
                            var page = ResPage(key);
                            return page != null && page.AiResultsReady;
                        }, 12000),
                        ScriptStep.LookAt(delegate
                        {
                            var page = ResPage(key);
                            return page == null ? null : page.AiFirstItem();
                        }),
                        ScriptStep.DoSwing(),
                        ScriptStep.Run(delegate
                        {
                            hit = OnUiGet(delegate
                            {
                                var page = ResPage(key);
                                return page == null ? null : page.AiFirstHit();
                            });
                            if (hit == null) throw new Exception("没搜到「" + pid + "」，换个关键词试试");
                            OnUi(delegate { Navigator.Go("download:detail", new object[] { hit, key }); });
                            return "已打开「" + (string.IsNullOrEmpty(hit.Title) ? hit.Slug : hit.Title) + "」的详情页";
                        }),
                        // 四幕：等当前项目的分组加载完（按项目归属等，残留数据不算数；失败则把页面错误抛出来）
                        ScriptStep.Wait(delegate
                        {
                            var d = DetailPage();
                            return d != null && ((hit != null && d.GroupsReadyFor(hit.ProjectId)) || d.FetchError != null);
                        }, 20000),
                        ScriptStep.Run(delegate
                        {
                            var d = DetailPage();
                            if (d == null || hit == null || !d.GroupsReadyFor(hit.ProjectId))
                                throw new Exception("获取版本列表失败：" + (d != null && d.FetchError != null ? d.FetchError : "页面未就绪"));
                            return "版本分组加载完成";
                        }),
                        // 五幕：滚动到目标分组（头跟滚动走，不挥手）→ 对准分组标题挥手 → 真实展开
                        ScriptStep.LookAt(delegate
                        {
                            var d = DetailPage();
                            return d == null ? null : OnUiGet(delegate { return d.FindGroupHead(loaderName, mcVer); });
                        }),
                        ScriptStep.DoSwing(),
                        ScriptStep.Run(delegate
                        {
                            bool ok = OnUiGet(delegate
                            {
                                var d = DetailPage();
                                return d != null && d.AiExpandGroup(loaderName, mcVer);
                            });
                            if (!ok)
                                throw new Exception(isPack ? "该整合包没有可下载的文件"
                                    : "HIL 找不到该模组支持 " + loaderName + " " + mcVer + " 的版本");
                            return "已展开目标分组";
                        }),
                        // 六幕：滚动到要下载的文件行（不挥手）→ 挥手 → 真实触发下载
                        ScriptStep.LookAt(delegate
                        {
                            FrameworkElement row = null;
                            var d = DetailPage();
                            if (d == null) return null;
                            return OnUiGet(delegate { d.AiPickFile(loaderName, mcVer, out row); return row; });
                        }),
                        ScriptStep.DoSwing(),
                        ScriptStep.Run(delegate
                        {
                            item = OnUiGet(delegate
                            {
                                var d = DetailPage();
                                if (d == null) return null;
                                return d.AiInstallFile(loaderName, mcVer, targetId, out pickedFile);
                            });
                            if (pickedFile == null)
                                throw new Exception(isPack ? "该整合包没有可下载的文件"
                                    : "HIL 找不到该模组支持 " + loaderName + " " + mcVer + " 的版本");
                            if (item == null) throw new Exception("下载任务创建失败");
                            return "已开始下载 " + (pickedFile.Name ?? pickedFile.VersionNumber ?? "");
                        }),
                        // 七幕：盯下载悬浮球到完成 → 汇报
                        ScriptStep.Watch(
                            delegate { var f = SteveDirector.FloatBall; return f == null ? null : f(); },
                            delegate { return item == null || item.Finished; }, 20 * 60 * 1000),
                        ScriptStep.SayFunc(delegate
                        {
                            if (item == null) return "下载任务没建成。";
                            if (!item.Finished) return "下载还在继续，右下角悬浮球能看进度，完了我再向你汇报。";
                            if (item.Failed) return "下载失败了：" + item.FailMessage + "。网络不稳的话可以让我再试一次。";
                            if (isPack) return "搞定！整合包「" + hit.Title + "」已经下载导入成新实例了，回主页就能选它启动。";
                            return "搞定！「" + hit.Title + "」已经装进 " + targetId + " 了。";
                        }),
                    };
                },
            };
            Register(t);
        }

        // list_versions：纯查询，简短演戏
        static void RegisterListVersions()
        {
            string result = null;
            var t = new AiTool
            {
                Name = "list_versions",
                Description = "列出启动器里已安装的游戏版本",
                Schema = Schema(new string[0][]),
                BuildScript = delegate(Dictionary<string, object> args)
                {
                    return new List<ScriptStep>
                    {
                        ScriptStep.DoSwing(),
                        ScriptStep.Run(delegate
                        {
                            var list = GameCore.GetInstalledVersions();
                            result = list.Count == 0 ? "还没有安装任何游戏版本" : string.Join("、", list.ToArray());
                            return result;
                        }),
                        ScriptStep.SayFunc(delegate
                        {
                            return result == null ? "查询完成。"
                                : (result.StartsWith("还没有") ? result : "你现在装了这些版本：" + result);
                        }),
                    };
                },
            };
            Register(t);
        }

        // download_game：完整链路——
        // 看"下载"标签→挥手→切页；看"原版游戏"二级标签→挥手→刷新；等版本清单；
        // 头对准目标版本行（不在视野先滚动）→挥手→真实选中（弹页内确认视图）；
        // （带加载器时：看加载器卡→挥手→真实勾选→等版本列表）；
        // 看"开始安装"按钮→挥手→真实触发下载；最后转向下载悬浮球全程注视进度。
        static void RegisterDownloadGame()
        {
            var t = new AiTool
            {
                Name = "download_game",
                Description = "下载一个 Minecraft 游戏版本（可选带加载器），走原版游戏下载页的完整安装链路",
                NeedConfirm = true,
                ConfirmText = delegate(Dictionary<string, object> args)
                {
                    string l = ArgStr(args, "loader");
                    return "我准备下载 Minecraft " + (ArgStr(args, "version") ?? "?")
                        + (string.IsNullOrEmpty(l) || l == "vanilla" ? "（原版）" : "（" + l + "）")
                        + "，文件有点大，要现在开始吗？";
                },
                Schema = Schema(new[]
                {
                    new[] { "version", "string", "Minecraft 版本号，如 1.21.1", null },
                    new[] { "loader", "string", "加载器，可空（默认 vanilla 原版）", "vanilla,forge,fabric,neoforge" },
                }, "version"),
                BuildScript = delegate(Dictionary<string, object> args)
                {
                    string mc = ArgStr(args, "version");
                    string loader = (ArgStr(args, "loader") ?? "vanilla").ToLowerInvariant();
                    DownloadTaskItem item = null;
                    var steps = new List<ScriptStep>
                    {
                        Gaze("tab:下载"),
                        ScriptStep.DoSwing(),
                        ScriptStep.Navigate("download:install"),
                        Gaze("sub:原版游戏"),
                        ScriptStep.DoSwing(),
                        ScriptStep.Navigate("download:install"),   // 同页重进 = 刷新（幂等）
                        // 等后台版本清单加载完
                        ScriptStep.Wait(delegate
                        {
                            var p = InstallPage();
                            return p != null && p.VersionsLoaded;
                        }, 25000),
                        // 头对准版本行（不在视野时 FindVersionItem 内部先滚动，不挥手）
                        ScriptStep.LookAt(delegate
                        {
                            var p = InstallPage();
                            return p == null ? null : p.FindVersionItem(mc);
                        }),
                        ScriptStep.DoSwing(),
                        // 真实选中：打开页内安装确认视图
                        ScriptStep.Run(delegate
                        {
                            if (string.IsNullOrEmpty(mc)) throw new Exception("缺少版本号");
                            bool ok = OnUiGet(delegate
                            {
                                var p = InstallPage();
                                return p != null && p.AiOpenConfirm(mc);
                            });
                            if (!ok) throw new Exception("版本清单里没有 " + mc + "，它可能不是正式版 id");
                            return "已选中 " + mc + "，确认视图已打开";
                        }),
                    };
                    if (loader != "vanilla")
                    {
                        steps.Add(ScriptStep.LookAt(delegate
                        {
                            var p = InstallPage();
                            return p == null ? null : p.LoaderCardElement(loader);
                        }));
                        steps.Add(ScriptStep.DoSwing());
                        steps.Add(ScriptStep.Run(delegate
                        {
                            OnUi(delegate
                            {
                                var p = InstallPage();
                                if (p != null) p.AiSelectLoader(loader);
                            });
                            return "已勾选 " + loader + " 加载器";
                        }));
                        // 加载器版本列表异步拉取（注意 Combo 是依赖属性，读它必须回 UI 线程）
                        steps.Add(ScriptStep.Wait(delegate
                        {
                            return OnUiGet(delegate
                            {
                                var p = InstallPage();
                                return p != null && p.AiLoaderReady(loader);
                            });
                        }, 25000));
                    }
                    // 看"开始安装"→挥手→真实触发下载
                    steps.Add(ScriptStep.LookAt(delegate
                    {
                        var p = InstallPage();
                        return p == null ? null : p.ConfirmButton;
                    }));
                    steps.Add(ScriptStep.DoSwing());
                    steps.Add(ScriptStep.Run(delegate
                    {
                        if (loader != "vanilla")
                        {
                            bool ready = OnUiGet(delegate
                            {
                                var p = InstallPage();
                                return p != null && p.AiLoaderReady(loader);
                            });
                            if (!ready) throw new Exception(mc + " 没有可用的 " + loader + " 加载器版本");
                        }
                        item = OnUiGet(delegate
                        {
                            var p = InstallPage();
                            return p == null ? null : p.AiStartInstall();
                        });
                        if (item == null) throw new Exception("下载任务创建失败");
                        return "已开始下载 " + mc + (loader == "vanilla" ? "" : "-" + loader);
                    }));
                    // 演戏切到下载管理页（StartInstall 内部已跳过一次，这里让史蒂夫的视线动线与真实页面一致）
                    steps.Add(ScriptStep.Navigate("download:speed"));
                    // 最后转向下载悬浮球，全程注视进度直到任务结束
                    steps.Add(ScriptStep.Watch(
                        delegate { var f = SteveDirector.FloatBall; return f == null ? null : f(); },
                        delegate { return item == null || item.Finished; }, 20 * 60 * 1000));
                    steps.Add(ScriptStep.SayFunc(delegate
                    {
                        if (item == null) return "下载任务没建成。";
                        if (!item.Finished) return "下载还在继续，右下角悬浮球能看进度，完了我再向你汇报。";
                        if (item.Failed) return "下载失败了：" + item.FailMessage + "。网络不稳的话可以让我再试一次。";
                        return "下载完成！已经帮你把当前版本切好，点启动就能玩了。";
                    }));
                    return steps;
                },
            };
            Register(t);
        }

        // launch_game：看「启动！！」按钮→挥手→真实启动
        static void RegisterLaunchGame()
        {
            var t = new AiTool
            {
                Name = "launch_game",
                Description = "启动游戏（不传版本则用当前选中的版本）",
                Schema = Schema(new[]
                {
                    new[] { "version", "string", "要启动的版本 id，可空", null },
                }),
                BuildScript = delegate(Dictionary<string, object> args)
                {
                    string ver = ArgStr(args, "version");
                    return new List<ScriptStep>
                    {
                        ScriptStep.LookAt(delegate { var f = SteveDirector.LaunchButton; return f == null ? null : f(); }),
                        ScriptStep.DoSwing(),
                        ScriptStep.Run(delegate
                        {
                            OnUi(delegate { PageBase.RequestLaunch(ver ?? ""); });
                            return ver == null ? "已触发启动当前版本" : "已触发启动 " + ver;
                        }),
                        ScriptStep.Say(ver == null ? "好嘞，正在为你启动游戏！" : "好嘞，正在为你启动 " + ver + "！"),
                    };
                },
            };
            Register(t);
        }

        // set_memory：切到 设置→游戏设置→看内存滑条→挥手→真实改值（滑条 UI 同步）
        static void RegisterSetMemory()
        {
            string result = null;
            var t = new AiTool
            {
                Name = "set_memory",
                Description = "设置游戏运行内存（MB）",
                Schema = Schema(new[]
                {
                    new[] { "mb", "integer", "内存大小 MB（512~32768）", null },
                }, "mb"),
                BuildScript = delegate(Dictionary<string, object> args)
                {
                    long mb = ArgLong(args, "mb", -1);
                    return new List<ScriptStep>
                    {
                        Gaze("tab:设置"),
                        ScriptStep.DoSwing(),
                        ScriptStep.Navigate("settings:game"),
                        Gaze("sub:游戏设置"),
                        ScriptStep.DoSwing(),
                        ScriptStep.Navigate("settings:game"),
                        Gaze("setup:memory"),
                        ScriptStep.DoSwing(),
                        ScriptStep.Run(delegate
                        {
                            if (mb < 512 || mb > 32768)
                            {
                                result = "内存要在 512 ~ 32768 MB 之间哦。";
                                return result;
                            }
                            // 滑条量程内就走真实 UI（ValueChanged 即时生效存配置）；超量程直接改配置
                            if (mb >= 1024 && mb <= 16384)
                            {
                                OnUi(delegate
                                {
                                    var f = SteveDirector.PageFinder;
                                    var page = f == null ? null : f("settings:game") as SettingsGamePanel;
                                    if (page != null) page.AiSetMemory((int)mb);
                                });
                            }
                            if (Cfg == null) { result = "配置未就绪"; return result; }
                            Cfg.MemoryMb = (int)mb;
                            Cfg.Save();
                            result = "已把游戏内存调成 " + mb + " MB 了。";
                            return result;
                        }),
                        ScriptStep.SayFunc(delegate { return result ?? "设置完成。"; }),
                    };
                },
            };
            Register(t);
        }

        // set_theme：切到 设置→启动器设置→看主题色块→挥手→真实换色
        static void RegisterSetTheme()
        {
            string result = null;
            var t = new AiTool
            {
                Name = "set_theme",
                Description = "设置启动器主题色（十六进制颜色，如 #4A90D9；或 绿/蓝/紫/橙/红/白）",
                Schema = Schema(new[]
                {
                    new[] { "color", "string", "十六进制颜色，如 #4A90D9；也可说 绿/蓝/紫/橙/红/白", null },
                }, "color"),
                BuildScript = delegate(Dictionary<string, object> args)
                {
                    string color = ArgStr(args, "color");
                    return new List<ScriptStep>
                    {
                        Gaze("tab:设置"),
                        ScriptStep.DoSwing(),
                        ScriptStep.Navigate("settings:launcher"),
                        Gaze("sub:启动器设置"),
                        ScriptStep.DoSwing(),
                        ScriptStep.Navigate("settings:launcher"),
                        Gaze("setup:theme"),
                        ScriptStep.DoSwing(),
                        ScriptStep.Run(delegate
                        {
                            if (string.IsNullOrEmpty(color)) { result = "要告诉我颜色哦"; return result; }
                            string name = color;
                            if (color.StartsWith("#"))
                            {
                                if (!ThemeManager.IsValidHex(color))
                                {
                                    result = "「" + color + "」不是有效的颜色，格式像 #4A90D9 这样。";
                                    return result;
                                }
                            }
                            else
                            {
                                switch (color)
                                {
                                    case "绿": case "蓝": case "紫": case "橙": case "红": case "白": break;
                                    default:
                                        result = "支持 绿/蓝/紫/橙/红/白，或者十六进制颜色如 #4A90D9。";
                                        return result;
                                }
                            }
                            string finalName = name;
                            OnUi(delegate { ThemeManager.ApplyTheme(finalName); });
                            if (Cfg != null) { Cfg.Theme = finalName; Cfg.Save(); }
                            result = "主题色换好啦，喜欢吗？";
                            return result;
                        }),
                        ScriptStep.SayFunc(delegate { return result ?? "完成。"; }),
                    };
                },
            };
            Register(t);
        }

        // list_servers：切到开服页再汇报（纯查询，简短）
        static void RegisterListServers()
        {
            string result = null;
            var t = new AiTool
            {
                Name = "list_servers",
                Description = "列出一键开服的服务器实例与运行状态",
                Schema = Schema(new string[0][]),
                BuildScript = delegate(Dictionary<string, object> args)
                {
                    return new List<ScriptStep>
                    {
                        Gaze("tab:服务器"),
                        ScriptStep.DoSwing(),
                        ScriptStep.Navigate("server:oneclick"),
                        ScriptStep.Run(delegate
                        {
                            var list = ServerCore.GetInstances();
                            if (list.Count == 0) { result = "还没有服务器实例。"; return result; }
                            var parts = new List<string>();
                            foreach (var n in list)
                                parts.Add(n + (ServerManager.IsRunning(n) ? "（运行中）" : "（已停止）"));
                            result = string.Join("、", parts.ToArray());
                            return result;
                        }),
                        ScriptStep.SayFunc(delegate { return result == null ? "查询完成。" : "服务器情况：" + result; }),
                    };
                },
            };
            Register(t);
        }

        // start_server：切到开服页→看实例下拉框→看操作按钮→挥手→真实启动
        static void RegisterStartServer()
        {
            string result = null;
            var t = new AiTool
            {
                Name = "start_server",
                Description = "启动一个已存在的服务器实例",
                Schema = Schema(new[]
                {
                    new[] { "name", "string", "服务器实例名", null },
                }, "name"),
                BuildScript = delegate(Dictionary<string, object> args)
                {
                    string name = ArgStr(args, "name");
                    return new List<ScriptStep>
                    {
                        Gaze("tab:服务器"),
                        ScriptStep.DoSwing(),
                        ScriptStep.Navigate("server:oneclick"),
                        Gaze("server:instances"),
                        Gaze("server:action"),
                        ScriptStep.DoSwing(),
                        ScriptStep.Run(delegate
                        {
                            if (string.IsNullOrEmpty(name) || !ServerCore.InstanceExists(name))
                            {
                                result = "没有找到叫「" + (name ?? "?") + "」的服务器，先用 list_servers 看看有哪些吧。";
                                return result;
                            }
                            if (ServerManager.IsRunning(name))
                            {
                                result = "「" + name + "」已经在运行了。";
                                return result;
                            }
                            ServerManager.StartExisting(ServerCore.Load(name));
                            result = "开始启动「" + name + "」，第一次开服要下载核心，稍等一下。";
                            return result;
                        }),
                        ScriptStep.SayFunc(delegate { return result ?? "已处理。"; }),
                    };
                },
            };
            Register(t);
        }

        // stop_server：切到开服页→看实例下拉框→看操作按钮→挥手→真实优雅关服
        static void RegisterStopServer()
        {
            string result = null;
            var t = new AiTool
            {
                Name = "stop_server",
                Description = "关闭一个正在运行的服务器实例（优雅关服）",
                Schema = Schema(new[]
                {
                    new[] { "name", "string", "服务器实例名", null },
                }, "name"),
                BuildScript = delegate(Dictionary<string, object> args)
                {
                    string name = ArgStr(args, "name");
                    return new List<ScriptStep>
                    {
                        Gaze("tab:服务器"),
                        ScriptStep.DoSwing(),
                        ScriptStep.Navigate("server:oneclick"),
                        Gaze("server:instances"),
                        Gaze("server:action"),
                        ScriptStep.DoSwing(),
                        ScriptStep.Run(delegate
                        {
                            if (string.IsNullOrEmpty(name) || !ServerManager.IsRunning(name))
                            {
                                result = "「" + (name ?? "?") + "」没有在运行。";
                                return result;
                            }
                            ServerManager.Stop(name);
                            result = "正在关闭「" + name + "」（先存档再停，最多等 10 秒）。";
                            return result;
                        }),
                        ScriptStep.SayFunc(delegate { return result ?? "已处理。"; }),
                    };
                },
            };
            Register(t);
        }

        // 服务器崩溃日志分类（崩溃关怀用）：OOM / 端口占用 / 模组报错 / 通用异常行
        public static string ClassifyServerCrash(string tail)
        {
            if (string.IsNullOrEmpty(tail)) return "日志里没看到线索。";
            if (tail.Contains("OutOfMemoryError"))
                return "看日志是内存不够（OutOfMemoryError）";
            if (tail.Contains("Address already in use") || tail.Contains("BindException")
                || tail.Contains("Failed to bind to port"))
                return "看日志是端口被占用了";
            string cause = PickLogLine(tail, "Incompatible mod set");
            if (cause == null) cause = PickLogLine(tail, "ModResolutionException");
            if (cause != null) return "看日志是模组加载失败：" + cause;
            cause = PickLogLine(tail, "Caused by");
            if (cause == null) cause = PickLogLine(tail, "Exception");
            if (cause != null) return "日志里的关键行：" + cause;
            return "日志里没看到明确原因";
        }

        // 从日志尾部倒序找含 marker 的一行（截 80 字）；没有返回 null
        static string PickLogLine(string tail, string marker)
        {
            var lines = tail.Split('\n');
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                string l = lines[i].Trim();
                if (l.Contains(marker)) return l.Length > 80 ? l.Substring(0, 80) + "…" : l;
            }
            return null;
        }

        static PageServerOneClick ServerPage()
        {
            var f = SteveDirector.PageFinder;
            return f == null ? null : f("server:oneclick") as PageServerOneClick;
        }

        // 危险命令名单（首词匹配，小写）：这些必须气泡确认；say/list/tps/weather/time/difficulty 等无害的直接发
        static readonly string[] DangerousCmds = new[]
        {
            "stop", "ban", "ban-ip", "kick", "op", "deop", "whitelist", "pardon",
            "reload", "save-off", "kill", "clear"
        };

        static bool IsDangerousCommand(string cmd)
        {
            if (string.IsNullOrEmpty(cmd)) return false;
            string c = cmd.Trim();
            if (c.StartsWith("/")) c = c.Substring(1).Trim();
            int sp = c.IndexOf(' ');
            string first = (sp < 0 ? c : c.Substring(0, sp)).ToLowerInvariant();
            foreach (var d in DangerousCmds)
                if (first == d) return true;
            return false;
        }

        // read_server_console：切服务器页→看控制台区→挥手→真实读取日志尾部→Say 摘要
        static void RegisterReadServerConsole()
        {
            string summary = null;
            var t = new AiTool
            {
                Name = "read_server_console",
                Description = "读取服务器控制台日志尾部（运行中或已停止的实例都能读）",
                Schema = Schema(new[]
                {
                    new[] { "name", "string", "服务器实例名", null },
                    new[] { "lines", "integer", "读取的行数（默认 50，最多 500）", null },
                }, "name"),
                BuildScript = delegate(Dictionary<string, object> args)
                {
                    string name = ArgStr(args, "name");
                    int lines = (int)ArgLong(args, "lines", 50);
                    if (lines < 1) lines = 50;
                    if (lines > 500) lines = 500;
                    int lineCount = lines;
                    return new List<ScriptStep>
                    {
                        Gaze("tab:服务器"),
                        ScriptStep.DoSwing(),
                        ScriptStep.Navigate("server:oneclick"),
                        Gaze("server:console"),
                        ScriptStep.DoSwing(),
                        ScriptStep.Run(delegate
                        {
                            if (string.IsNullOrEmpty(name) || !ServerCore.InstanceExists(name))
                            {
                                summary = "没有找到叫「" + (name ?? "?") + "」的服务器，先用 list_servers 看看有哪些吧。";
                                return summary;
                            }
                            // 控制台区切到该实例，用户看到的和读到的一致
                            OnUi(delegate { var p = ServerPage(); if (p != null) p.AiSelectInstance(name); });
                            string tail = ServerManager.GetLogTail(name, lineCount);
                            if (tail.Length == 0)
                            {
                                summary = "「" + name + "」还没有控制台日志。";
                                return summary;
                            }
                            // Say 只报最后两行摘要；完整尾部喂回 AI
                            var ls = tail.Split('\n');
                            int n = Math.Min(2, ls.Length);
                            var last = new List<string>();
                            for (int i = ls.Length - n; i < ls.Length; i++) last.Add(ls[i].Trim());
                            summary = "「" + name + "」最新的日志：" + string.Join("；", last.ToArray());
                            return "以下是「" + name + "」控制台最后 " + lineCount + " 行：\n" + tail;
                        }),
                        ScriptStep.SayFunc(delegate { return summary ?? "读完了。"; }),
                    };
                },
            };
            Register(t);
        }

        // send_server_command：切服务器页→看控制台→看命令输入框→挥手→走页面真实链路写 stdin→Say 回显
        // 危险命令（stop/ban/kick/op/deop/whitelist/pardon 等）强制确认；say/list 等无害的直接发
        static void RegisterSendServerCommand()
        {
            string summary = null;
            var t = new AiTool
            {
                Name = "send_server_command",
                Description = "向运行中的服务器发送控制台命令（不用打 /）；危险命令会先向用户确认",
                NeedConfirmIf = delegate(Dictionary<string, object> args)
                {
                    return IsDangerousCommand(ArgStr(args, "command"));
                },
                ConfirmText = delegate(Dictionary<string, object> args)
                {
                    return "我准备向服务器「" + (ArgStr(args, "name") ?? "?") + "」发送危险命令「"
                        + (ArgStr(args, "command") ?? "?") + "」，确定要发吗？";
                },
                Schema = Schema(new[]
                {
                    new[] { "name", "string", "服务器实例名", null },
                    new[] { "command", "string", "控制台命令（不用打 /），如 say 你好 / list", null },
                }, "name", "command"),
                BuildScript = delegate(Dictionary<string, object> args)
                {
                    string name = ArgStr(args, "name");
                    string command = ArgStr(args, "command");
                    return new List<ScriptStep>
                    {
                        Gaze("tab:服务器"),
                        ScriptStep.DoSwing(),
                        ScriptStep.Navigate("server:oneclick"),
                        Gaze("server:console"),
                        Gaze("server:cmd"),
                        ScriptStep.DoSwing(),
                        ScriptStep.Run(delegate
                        {
                            if (string.IsNullOrEmpty(name) || !ServerCore.InstanceExists(name))
                            {
                                summary = "没有找到叫「" + (name ?? "?") + "」的服务器。";
                                return summary;
                            }
                            if (string.IsNullOrEmpty(command))
                            {
                                summary = "没说要发什么命令哦。";
                                return summary;
                            }
                            if (!ServerManager.IsRunning(name))
                            {
                                summary = "「" + name + "」没有在运行，命令发不出去。要我先帮你启动吗？";
                                return summary;
                            }
                            bool ok = OnUiGet(delegate
                            {
                                var p = ServerPage();
                                return p != null && p.AiSendCommand(name, command);
                            });
                            if (!ok)
                            {
                                summary = "命令发送失败，控制台好像没准备好。";
                                return summary;
                            }
                            // 等回显落进日志缓冲，再顺手读最后几行
                            System.Threading.Thread.Sleep(800);
                            string echo = ServerManager.GetLogTail(name, 4);
                            summary = "已发送「" + command + "」。";
                            return "已发送命令 " + command + "\n回显：\n" + echo;
                        }),
                        ScriptStep.SayFunc(delegate { return summary ?? "已处理。"; }),
                    };
                },
            };
            Register(t);
        }

        // read_crash：纯查询，简短
        static void RegisterReadCrash()
        {
            string result = null;
            var t = new AiTool
            {
                Name = "read_crash",
                Description = "读取最近一次游戏崩溃的分析摘要",
                Schema = Schema(new string[0][]),
                BuildScript = delegate(Dictionary<string, object> args)
                {
                    return new List<ScriptStep>
                    {
                        ScriptStep.DoSwing(),
                        ScriptStep.Run(delegate
                        {
                            var r = AiRepair.LastResult;
                            if (r == null || r.Reasons.Count == 0)
                            {
                                result = "最近没有记录到游戏崩溃。";
                                return result;
                            }
                            int n = Math.Min(3, r.Reasons.Count);
                            var parts = new List<string>();
                            for (int i = 0; i < n; i++) parts.Add(r.Reasons[i]);
                            result = string.Join("\n", parts.ToArray());
                            return result;
                        }),
                        ScriptStep.SayFunc(delegate { return result ?? "查完了。"; }),
                    };
                },
            };
            Register(t);
        }

        // 修复类工具：实装在 AiRepair（分类器机读优先）
        static void RegisterFixTools()
        {
            var conflict = new AiTool
            {
                Name = "fix_mod_conflict",
                Description = "修复模组冲突：从最近崩溃报告抽取冲突模组，经 Modrinth 找到兼容版本并下载替换",
                Schema = Schema(new string[0][]),
                BuildScript = delegate(Dictionary<string, object> args)
                {
                    string result = null;
                    return new List<ScriptStep>
                    {
                        ScriptStep.Run(delegate
                        {
                            result = AiRepair.FixModConflict();
                            return result;
                        }),
                        ScriptStep.SayFunc(delegate { return result ?? "处理完了。"; }),
                    };
                },
            };
            Register(conflict);

            string memResult = null;
            var mem = new AiTool
            {
                Name = "fix_memory",
                Description = "修复内存不足崩溃：把游戏内存上调 1024MB（不超过物理内存 80%）",
                Schema = Schema(new string[0][]),
                BuildScript = delegate(Dictionary<string, object> args)
                {
                    return new List<ScriptStep>
                    {
                        ScriptStep.DoSwing(),
                        ScriptStep.Run(delegate
                        {
                            memResult = AiRepair.FixMemory();
                            return memResult;
                        }),
                        ScriptStep.SayFunc(delegate { return memResult ?? "处理完了。"; }),
                    };
                },
            };
            Register(mem);
        }

        // repair_game_files：校验补齐库文件（挥手→真实校验→汇报）
        static void RegisterRepairGameFiles()
        {
            string result = null;
            var t = new AiTool
            {
                Name = "repair_game_files",
                Description = "校验并补齐指定游戏版本的依赖库文件",
                Schema = Schema(new[]
                {
                    new[] { "version", "string", "要校验的版本 id", null },
                }, "version"),
                BuildScript = delegate(Dictionary<string, object> args)
                {
                    string ver = ArgStr(args, "version");
                    return new List<ScriptStep>
                    {
                        ScriptStep.DoSwing(),
                        ScriptStep.Run(delegate
                        {
                            if (string.IsNullOrEmpty(ver) || !GameCore.IsVersionReady(ver))
                            {
                                result = "版本 " + (ver ?? "?") + " 没有安装。";
                                return result;
                            }
                            GameCore.EnsureLibraries(ver, null);
                            result = "已经把「" + ver + "」的库文件校验补齐了，重新启动试试。";
                            return result;
                        }),
                        ScriptStep.SayFunc(delegate { return result ?? "校验完成。"; }),
                    };
                },
            };
            Register(t);
        }
    }
}
