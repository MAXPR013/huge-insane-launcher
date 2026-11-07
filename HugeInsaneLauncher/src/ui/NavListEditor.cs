using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace HugeLauncher.Ui
{
    // 导航列表编辑器：拖 ≡ 排序时，被让位的行平滑滑到新位置（不是瞬移）；松手才提交顺序
    class NavListEditor
    {
        class Row
        {
            public string Key;
            public Border El;
            public TranslateTransform Tf;
        }

        const double Step = 44;   // 行高 40 + 间距 4

        readonly StackPanel list;
        readonly List<string> order;
        readonly List<string> hidden;
        readonly Action dirty;
        readonly Action rebuild;
        readonly List<Row> rows = new List<Row>();

        Row dragging;
        double grabOffset;
        int fromIndex, toIndex = -1;

        public NavListEditor(StackPanel list, List<string> order, List<string> hidden, Action dirty, Action rebuild)
        {
            this.list = list;
            this.order = order;
            this.hidden = hidden;
            this.dirty = dirty;
            this.rebuild = rebuild;
        }

        List<string> VisibleKeys()
        {
            var keys = new List<string>();
            for (int i = 0; i < order.Count; i++)
                if (!hidden.Contains(order[i])) keys.Add(order[i]);
            if (keys.Count == 0 && NavCatalog.All.Count > 0) keys.Add(NavCatalog.All[0].Key);
            return keys;
        }

        public void BuildRows()
        {
            list.Children.Clear();
            rows.Clear();
            var keys = VisibleKeys();
            for (int i = 0; i < keys.Count; i++) AddRow(keys[i], keys.Count);
        }

        void AddRow(string key, int total)
        {
            var def = NavCatalog.Find(key);
            var el = new Border();
            el.Height = 40;
            el.CornerRadius = new CornerRadius(6);
            el.Background = Palette.B(Palette.Tile);
            el.BorderThickness = new Thickness(1);
            el.BorderBrush = Palette.B(Palette.Line);
            el.Margin = new Thickness(0, 0, 0, 4);
            el.Tag = key;
            var tf = new TranslateTransform(0, 0);
            el.RenderTransform = tf;

            var g = new Grid();
            g.ColumnDefinitions.Add(UiHelpers.StarCol());
            g.ColumnDefinitions.Add(UiHelpers.AutoCol());
            g.ColumnDefinitions.Add(UiHelpers.AutoCol());

            var nm = new TextBlock();
            nm.Text = def == null ? key : def.Name;
            nm.FontSize = 12.5;
            nm.VerticalAlignment = VerticalAlignment.Center;
            nm.Margin = new Thickness(12, 0, 0, 0);
            nm.Foreground = Palette.B(Palette.Text);
            g.Children.Add(nm);

            var handle = new TextBlock();
            handle.Text = "≡";
            handle.FontSize = 15;
            handle.Cursor = Cursors.SizeAll;
            handle.VerticalAlignment = VerticalAlignment.Center;
            handle.Margin = new Thickness(10, 0, 10, 0);
            handle.Foreground = Palette.B(Palette.Dim);
            Grid.SetColumn(handle, 1);
            g.Children.Add(handle);

            var del = new TextBlock();
            del.Text = "×";
            del.FontSize = 14;
            del.VerticalAlignment = VerticalAlignment.Center;
            del.Margin = new Thickness(0, 0, 12, 0);
            del.Foreground = Palette.B(Palette.Dim);
            del.Cursor = total <= 1 ? Cursors.Arrow : Cursors.Hand;
            if (total <= 1) del.Opacity = 0.35;
            Grid.SetColumn(del, 2);
            g.Children.Add(del);

            el.Child = g;
            HoverMagnet.Attach(el);   // 和其它列表项一样带磁吸

            var row = new Row();
            row.Key = key;
            row.El = el;
            row.Tf = tf;
            rows.Add(row);

            handle.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                dragging = row;
                grabOffset = e.GetPosition(el).Y;
                fromIndex = rows.IndexOf(row);
                toIndex = fromIndex;
                handle.CaptureMouse();
                e.Handled = true;
            };
            handle.MouseMove += delegate(object s, MouseEventArgs e)
            {
                if (dragging == null || e.LeftButton != MouseButtonState.Pressed) return;
                double y = e.GetPosition(list).Y;
                int target = (int)Math.Round((y - grabOffset) / Step);
                if (target < 0) target = 0;
                if (target > rows.Count - 1) target = rows.Count - 1;
                // 被拖的那行跟手
                int idx = rows.IndexOf(row);
                row.Tf.Y = y - grabOffset - idx * Step;
                if (target == toIndex) return;
                toIndex = target;
                Relayout();
            };
            handle.MouseLeftButtonUp += delegate
            {
                if (dragging == null) return;
                try { handle.ReleaseMouseCapture(); } catch { }
                Commit();
            };

            del.MouseLeftButtonUp += delegate
            {
                if (rows.Count <= 1) return;
                hidden.Add(key);
                order.Remove(key);
                dirty();
                rebuild();
            };

            list.Children.Add(el);
        }

        // 让位的行滑动到目标槽位（缓出 140ms）
        void Relayout()
        {
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                if (r == dragging) continue;
                int slot = i;
                if (fromIndex < toIndex && i > fromIndex && i <= toIndex) slot = i - 1;
                else if (fromIndex > toIndex && i >= toIndex && i < fromIndex) slot = i + 1;
                double targetY = (slot - i) * Step;
                var tf = r.Tf;
                double from = tf.Y;
                Ani.Start("navShift" + r.El.GetHashCode(), Ani.AaProgress(delegate(double k)
                {
                    tf.Y = from + (targetY - from) * k;
                }, 140, new AniEaseOutFluent(Ani.Weak)));
            }
        }

        void Commit()
        {
            if (dragging == null) return;
            var r = dragging;
            var el = r.El;
            dragging = null;
            int cur = rows.IndexOf(r);
            int target = toIndex < 0 ? cur : toIndex;
            rows.RemoveAt(cur);
            rows.Insert(target, r);
            list.Children.Remove(el);
            list.Children.Insert(target, el);
            for (int i = 0; i < rows.Count; i++)
            {
                rows[i].El.RenderTransform = new TranslateTransform(0, 0);
                rows[i].Tf = (TranslateTransform)rows[i].El.RenderTransform;
            }
            // 顺序写回：可见项按新顺序依次填回「原来的可见槽位」，隐藏项留在原位不动
            var seq = new List<string>();
            for (int i = 0; i < rows.Count; i++) seq.Add(rows[i].Key);
            int vi = 0;
            for (int i = 0; i < order.Count && vi < seq.Count; i++)
            {
                if (hidden.Contains(order[i])) continue;
                order[i] = seq[vi++];
            }
            toIndex = -1;
            dirty();
        }
    }
}
