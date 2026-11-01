// HugeLauncher.Ui.Pages - 版本设置·导出页（子代理 C）：导出精简版 .mrpack（Modrinth 整合包）
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HugeLauncher;
using HugeLauncher.Ui;

namespace HugeLauncher.Ui.Pages
{
    // 导航 key: instance:export，NavParam = string 版本 id（可为 null）
    class PageInstanceExport : PageBase
    {
        string versionId;
        HintTextBox nameBox;
        HintTextBox verBox;
        HintTextBox summaryBox;
        FlatButton exportBtn;
        bool exporting;

        public PageInstanceExport()
        {
            var scroll = new SmoothScrollViewer();
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            var stack = new StackPanel();
            stack.Margin = new Thickness(16);
            scroll.Content = stack;
            Children.Add(scroll);

            // 顶部返回栏（回实例列表）
            var back = new FlatButton("← 返回");
            back.SetPadding(new Thickness(10, 5, 10, 5));
            back.HorizontalAlignment = HorizontalAlignment.Left;
            back.Margin = new Thickness(0, 0, 0, 10);
            back.Click += delegate { Navigator.Go("instance:list"); };
            stack.Children.Add(back);

            // ---- 说明卡 ----
            var introCard = Card();
            var introStack = new StackPanel();
            introStack.Children.Add(Title("导出整合包"));
            introStack.Children.Add(DimText("将当前版本的 Mod 列表导出为 .mrpack（Modrinth 整合包格式），可供他人导入。"));
            introCard.Child = introStack;
            stack.Children.Add(introCard);

            // ---- 表单卡 ----
            var formCard = Card();
            var formStack = new StackPanel();
            formStack.Children.Add(Title("整合包信息"));
            formStack.Children.Add(FieldLabel("整合包名称"));
            nameBox = new HintTextBox("默认使用版本 id");
            formStack.Children.Add(nameBox);
            formStack.Children.Add(FieldLabel("版本号"));
            verBox = new HintTextBox("1.0.0");
            formStack.Children.Add(verBox);
            formStack.Children.Add(FieldLabel("简介"));
            summaryBox = new HintTextBox("一句话简介（可选）");
            formStack.Children.Add(summaryBox);
            formCard.Child = formStack;
            stack.Children.Add(formCard);

            // ---- 导出按钮 ----
            exportBtn = new FlatButton("导出");
            exportBtn.SetColors(ThemeManager.CurrentColor, Palette.Lighten(ThemeManager.CurrentColor, 0.2), Palette.Dark, Palette.Dark);
            exportBtn.HorizontalAlignment = HorizontalAlignment.Left;
            exportBtn.Click += OnExport;
            stack.Children.Add(exportBtn);
        }

        public override void OnEnter()
        {
            versionId = NavParam as string;
            nameBox.Text = versionId ?? "";
            verBox.Text = "1.0.0";
            summaryBox.Text = "";
        }

        void OnExport(object sender, EventArgs e)
        {
            if (exporting) return;
            if (string.IsNullOrEmpty(versionId)) { Toast("未选择版本"); return; }
            string name = nameBox.Text.Trim();
            if (name.Length == 0) name = versionId;
            string ver = verBox.Text.Trim();
            if (ver.Length == 0) ver = "1.0.0";
            string summary = summaryBox.Text.Trim();

            var dlg = new System.Windows.Forms.SaveFileDialog();
            dlg.Title = "导出整合包";
            dlg.Filter = "Modrinth 整合包|*.mrpack";
            dlg.FileName = SanitizeFileName(name) + ".mrpack";
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            string path = dlg.FileName;

            exporting = true;
            exportBtn.SetEnabled(false);
            string vid = versionId;
            string mc = McVersionOf(versionId);
            ThreadPool.QueueUserWorkItem(delegate
            {
                string err = null;
                try { BuildMrpack(path, vid, mc, name, ver, summary); }
                catch (Exception ex) { err = ex.Message; }
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    exporting = false;
                    exportBtn.SetEnabled(true);
                    Toast(err == null ? "已导出到 " + path : "导出失败: " + err);
                }));
            });
        }

        // 生成 .mrpack（zip）：modrinth.index.json + overrides/mods/*.jar
        static void BuildMrpack(string path, string versionId, string mcVersion, string name, string ver, string summary)
        {
            var enabled = new List<LocalMod>();
            foreach (var m in LocalMods.Scan(versionId))
                if (m.Enabled) enabled.Add(m);

            var sb = new StringBuilder();
            sb.Append("{\r\n");
            sb.Append("  \"formatVersion\": 1,\r\n");
            sb.Append("  \"game\": \"minecraft\",\r\n");
            sb.Append("  \"versionId\": \"").Append(JsonEscape(ver)).Append("\",\r\n");
            sb.Append("  \"name\": \"").Append(JsonEscape(name)).Append("\",\r\n");
            if (summary.Length > 0)
                sb.Append("  \"summary\": \"").Append(JsonEscape(summary)).Append("\",\r\n");
            sb.Append("  \"files\": [");
            for (int i = 0; i < enabled.Count; i++)
            {
                var m = enabled[i];
                string sha1 = Sha1Of(m.FilePath);
                long size = new FileInfo(m.FilePath).Length;
                sb.Append(i == 0 ? "\r\n" : ",\r\n");
                sb.Append("    {\r\n");
                sb.Append("      \"path\": \"mods/").Append(JsonEscape(m.FileName)).Append("\",\r\n");
                sb.Append("      \"hashes\": {\r\n");
                sb.Append("        \"sha1\": \"").Append(sha1).Append("\"\r\n");
                sb.Append("      },\r\n");
                sb.Append("      \"fileSize\": ").Append(size).Append("\r\n");
                sb.Append("    }");
            }
            sb.Append(enabled.Count > 0 ? "\r\n  ],\r\n" : "],\r\n");
            sb.Append("  \"dependencies\": {\r\n");
            sb.Append("    \"minecraft\": \"").Append(JsonEscape(mcVersion)).Append("\"\r\n");
            sb.Append("  }\r\n");
            sb.Append("}\r\n");

            if (File.Exists(path)) File.Delete(path);
            using (var fs = new FileStream(path, FileMode.Create))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                var entry = zip.CreateEntry("modrinth.index.json");
                using (var w = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
                    w.Write(sb.ToString());
                foreach (var m in enabled)
                    zip.CreateEntryFromFile(m.FilePath, "overrides/mods/" + m.FileName);
            }
        }

        // 从版本 id 解析 MC 版本号（派生版本以 json 的 inheritsFrom 为准，见 GameCore.BaseGameVersion）
        static string McVersionOf(string versionId)
        {
            return GameCore.BaseGameVersion(versionId);
        }

        static string Sha1Of(string path)
        {
            using (var sha = SHA1.Create())
            using (var fs = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
        }

        static string JsonEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        static string SanitizeFileName(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }

        // ---------- 小控件工厂 ----------

        static Border Card()
        {
            var b = new Border();
            b.Background = Palette.B(Palette.Panel);
            b.CornerRadius = new CornerRadius(8);
            b.Padding = new Thickness(14);
            b.Margin = new Thickness(0, 0, 0, 10);
            return b;
        }

        static TextBlock Title(string s)
        {
            var t = new TextBlock();
            t.Text = s;
            t.FontSize = 13;
            t.FontWeight = FontWeights.SemiBold;
            t.Foreground = Palette.B(Palette.Text);
            t.Margin = new Thickness(0, 0, 0, 8);
            return t;
        }

        static TextBlock DimText(string s)
        {
            var t = new TextBlock();
            t.Text = s;
            t.FontSize = 11;
            t.Foreground = Palette.B(Palette.Dim);
            t.TextWrapping = TextWrapping.Wrap;
            return t;
        }

        static TextBlock FieldLabel(string s)
        {
            var t = new TextBlock();
            t.Text = s;
            t.FontSize = 12;
            t.Foreground = Palette.B(Palette.Text);
            t.Margin = new Thickness(0, 8, 0, 4);
            return t;
        }
    }
}
