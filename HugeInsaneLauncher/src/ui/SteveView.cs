// HugeLauncher - 史蒂夫 3D 小人：头跟随鼠标转动、点击时挥臂（仿 MC 背包界面的玩家模型）
// 皮肤优先从游戏 jar 抽取 steve.png，失败则用代码画的经典史蒂夫配色兜底。
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace HugeLauncher.Ui
{
    // 史蒂夫状态机：Idle=头跟鼠标；Acting=H史演戏（鼠标跟随停用，姿态由 AiGaze 单一写入）
    enum SteveMode { Idle, Acting }

    class SteveView : Grid
    {
        SteveMode mode = SteveMode.Idle;
        // ACTING 期间鼠标处理器直接停用；进出都有平滑过渡（现有插值天然平滑）
        public SteveMode Mode { get { return mode; } set { mode = value; } }

        // AI 视线：直接写目标角度（与鼠标走同一插值通道）
        public void AiGaze(double yaw, double pitch)
        {
            targetYaw = Clamp(yaw, -120, 120);
            targetPitch = Clamp(pitch, -35, 35);
        }

        // 把控件中心换算成 yaw/pitch（复用鼠标跟随 rx*120/ry*35 的换算与限幅）
        public void AiGazeToControlPoint(FrameworkElement target)
        {
            if (target == null || ActualWidth < 2 || ActualHeight < 2) return;
            try
            {
                var w = Window.GetWindow(this);
                if (w == null) return;
                var pc = target.TransformToAncestor(w).Transform(new Point(target.ActualWidth / 2, target.ActualHeight / 2));
                var ps = TransformToAncestor(w).Transform(new Point(0, 0));
                double rx = ((pc.X - ps.X) - ActualWidth / 2) / (ActualWidth / 2);
                double ry = ((pc.Y - ps.Y) - ActualHeight / 2) / (ActualHeight / 2);
                AiGaze(rx * 120, ry * 35);
            }
            catch { }
        }

        readonly AxisAngleRotation3D headYaw = new AxisAngleRotation3D(new Vector3D(0, 1, 0), 0);
        readonly AxisAngleRotation3D headPitch = new AxisAngleRotation3D(new Vector3D(1, 0, 0), 0);
        readonly AxisAngleRotation3D modelYaw = new AxisAngleRotation3D(new Vector3D(0, 1, 0), 0);     // 整体朝向（原版 renderYawOffset，转头超限时身体跟随）
        readonly AxisAngleRotation3D bodyTwist = new AxisAngleRotation3D(new Vector3D(0, 1, 0), 0);    // 挥臂躯干扭转（只带躯干+双臂，腿和头不扭）
        readonly AxisAngleRotation3D leftArmPitch = new AxisAngleRotation3D(new Vector3D(1, 0, 0), 0); // MC：挥臂时左臂 rotX += 身扭
        readonly AxisAngleRotation3D armSwing = new AxisAngleRotation3D(new Vector3D(1, 0, 0), 0);
        readonly AxisAngleRotation3D armYaw = new AxisAngleRotation3D(new Vector3D(0, 1, 0), 0);
        readonly AxisAngleRotation3D armRoll = new AxisAngleRotation3D(new Vector3D(0, 0, 1), 0);

        double targetYaw, targetPitch;      // 头部目标角度
        double curYaw = 1e9, curPitch;      // 当前角度（首次强制刷新）
        double curBody;                     // 身体朝向（头身限位跟随，原版 updateDistance）
        double swingP = -1;                 // 挥臂进度 0..1，-1 = 未挥臂
        double swingBodyDeg;                // 挥臂时身体扭转角（叠加在头部跟随上）
        TimeSpan lastRender = TimeSpan.Zero;
        bool hooked;
        Window hostWin;                     // 缓存宿主窗口（最小化时渲染循环停摆）

        readonly Viewport3D viewport;   // 实时 3D 视口：屏幕显示走 GPU 合成（流畅）
        PerspectiveCamera camera;        // 自适应相机：宽扁视口拉远，避免裁头切脚
        // 玻璃透镜源镜像：VisualBrush 刷 Viewport3D 在 GPU 实时路径会把整场景塞进画刷块、忽略布局偏移
        // （按钮里看到上半身而不是正下方的腿）。所以透镜源用这张 2D 位图镜像——摆进 steveLayer 和本控件同位，
        // 降帧（跟随 12Hz / 挥臂 30Hz）+ 半分辨率渲染，开销比每帧全尺寸 RTB 低一个数量级
        public readonly Image MirrorImg;
        bool mirrorDirty = true;        // 有待上屏的姿态（静止时补一张最终帧）
        int lastMirrorMs;

        public SteveView()
        {
            ClipToBounds = true;
            viewport = new Viewport3D();
            viewport.IsHitTestVisible = false;
            Children.Add(viewport);
            MirrorImg = new Image();
            MirrorImg.Stretch = Stretch.Fill;
            MirrorImg.IsHitTestVisible = false;

            camera = new PerspectiveCamera(
                new Point3D(0, 14, 58), new Vector3D(0, 0, -1), new Vector3D(0, 1, 0), 33);
            viewport.Camera = camera;

            var skin = LoadSkin();
            // 两份图集两个材质：基础层强制不透明（opaque 队列先画），帽子层 alpha 二值化
            // （transparent 队列后画）。共用一份带 alpha 的材质会把整个模型拖进透明队列，
            // 帽子盒先画写深度、后面的头被剔除——就是"边框后面变透明"的元凶（MC：基础层不透明、帽层 cutout）
            var mat = MakeMaterial(MakeAtlas(skin, false));
            // 皮肤没有帽子层就不建帽子盒：全透盒子也进透明队列，深度/混合一乱就是"透明壳"
            bool hasHat = HasHatLayer(skin);
            var matHat = hasHat ? MakeMaterial(MakeAtlas(skin, true)) : null;

            var root = new Model3DGroup();
            root.Children.Add(new AmbientLight(Color.FromRgb(0xB2, 0xB2, 0xB2)));
            root.Children.Add(new DirectionalLight(Color.FromRgb(0x72, 0x72, 0x72), new Vector3D(0.4, -0.55, -0.75)));

            bool slim32 = skin.PixelHeight == 32;   // 老版 64x32 皮肤：左肢复用右肢贴图

            // 头（不透明）与帽子层（透明 cutout）拆成两个模型，渲染队列天然保证头先画
            var headXform = new Transform3DGroup();
            headXform.Children.Add(new RotateTransform3D(headPitch));
            headXform.Children.Add(new RotateTransform3D(headYaw));
            headXform.Children.Add(new TranslateTransform3D(0, 24, 0)); // 颈部枢轴
            var headMesh = new MeshGeometry3D();
            BuildBox(headMesh, 0, 4, 0, 8, 8, 8, 0, 0, 8, 8, 8);      // 头（相对颈部枢轴）
            var headModel = new GeometryModel3D(headMesh, mat);
            headModel.Transform = headXform;
            var headGroup = new Model3DGroup();
            headGroup.Children.Add(headModel);
            if (hasHat)
            {
                var hatMesh = new MeshGeometry3D();
                BuildBox(hatMesh, 0, 4, 0, 9, 9, 9, 32, 0, 8, 8, 8);      // 帽子层（大 0.5）
                var hatModel = new GeometryModel3D(hatMesh, matHat);
                hatModel.Transform = headXform;
                headGroup.Children.Add(hatModel);
            }

            // 右臂（模型右手 = 屏幕左侧，MC 左键挥的就是它），绕肩膀枢轴摆动
            var armMesh = new MeshGeometry3D();
            BuildBox(armMesh, 0, -5, 0, 4, 12, 4, 40, 16, 4, 12, 4);   // 相对肩膀枢轴
            var armModel = new GeometryModel3D(armMesh, mat);
            var armXform = new Transform3DGroup();
            armXform.Children.Add(new RotateTransform3D(armRoll));     // MC 旋转应用顺序 Z→Y→X
            armXform.Children.Add(new RotateTransform3D(armYaw));
            armXform.Children.Add(new RotateTransform3D(armSwing));
            armXform.Children.Add(new TranslateTransform3D(-6, 23, 0)); // 肩膀枢轴
            armModel.Transform = armXform;

            // 部件全部独立：躯干/左臂/双腿。MC 挥臂时只有躯干+双臂参与扭转
            // （肩膀枢轴随身体轴心公转，手臂不是独自绕肩空转），腿和头不扭
            var torsoMesh = new MeshGeometry3D();
            BuildBox(torsoMesh, 0, 18, 0, 8, 12, 4, 16, 16, 8, 12, 4);  // 躯干
            var torsoModel = new GeometryModel3D(torsoMesh, mat);

            var leftArmMesh = new MeshGeometry3D();
            BuildBox(leftArmMesh, 0, -5, 0, 4, 12, 4, slim32 ? 40 : 32, slim32 ? 16 : 48, 4, 12, 4); // 相对肩膀枢轴
            var leftArmModel = new GeometryModel3D(leftArmMesh, mat);
            var leftArmXform = new Transform3DGroup();
            leftArmXform.Children.Add(new RotateTransform3D(leftArmPitch));
            leftArmXform.Children.Add(new TranslateTransform3D(6, 23, 0)); // 左肩枢轴
            leftArmModel.Transform = leftArmXform;

            var legMesh = new MeshGeometry3D();
            BuildBox(legMesh, -2, 6, 0, 4, 12, 4, 0, 16, 4, 12, 4);      // 右腿
            BuildBox(legMesh, 2, 6, 0, 4, 12, 4, slim32 ? 0 : 16, slim32 ? 16 : 48, 4, 12, 4);    // 左腿
            var legModel = new GeometryModel3D(legMesh, mat);

            // 层级（对齐 MC）：modelYaw 转整个模型 → bodyTwist 只扭躯干+双臂
            var twistGroup = new Model3DGroup();
            twistGroup.Children.Add(torsoModel);
            twistGroup.Children.Add(armModel);
            twistGroup.Children.Add(leftArmModel);
            twistGroup.Transform = new RotateTransform3D(bodyTwist);

            var modelCore = new Model3DGroup();
            modelCore.Children.Add(headGroup);
            modelCore.Children.Add(twistGroup);
            modelCore.Children.Add(legModel);
            modelCore.Transform = new RotateTransform3D(modelYaw);

            root.Children.Add(modelCore);

            var visual = new ModelVisual3D();
            visual.Content = root;
            viewport.Children.Add(visual);

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            SizeChanged += delegate { RenderMirror(0); };   // 尺寸变了补一帧
        }

        // 把实时视口当前姿态渲成镜像位图（满分辨率、随帧同步：RTB 按当前姿态同步渲，
        // 与视口当帧内容完全一致——限帧会让旧姿态从透明视口底下透出来，形成短暂残影；静止零渲染）。
        // 不手动 Measure/Arrange——视口在可视树里，布局归框架管，直接按当前尺寸 RTB
        void RenderMirror(int minGapMs)
        {
            if (!Glass.On) { mirrorDirty = true; return; }   // 关玻璃没人看镜像，攒着等开
            if (minGapMs > 0 && Environment.TickCount - lastMirrorMs < minGapMs) return;
            int w = (int)Math.Round(ActualWidth), h = (int)Math.Round(ActualHeight);
            if (w < 8 || h < 8) return;
            try
            {
                var rt = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                rt.Render(viewport);
                rt.Freeze();
                MirrorImg.Source = rt;
                mirrorDirty = false;
                lastMirrorMs = Environment.TickCount;
            }
            catch { }
        }

        // 玻璃开关打开时主窗口调一次：立刻补镜像（关玻璃期间攒的脏姿态一次结清）
        public void RefreshMirror() { RenderMirror(0); }

        // ---------- 交互：窗口级鼠标监听（像背包界面那样，光标在哪都看） ----------

        void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (hooked) return;
            hooked = true;
            var w = Window.GetWindow(this);
            hostWin = w;
            if (w != null)
            {
                w.PreviewMouseMove += OnMouseMove;
                w.PreviewMouseLeftButtonDown += OnMouseDown;
            }
            CompositionTarget.Rendering += OnRendering;
        }

        void OnUnloaded(object sender, RoutedEventArgs e)
        {
            hooked = false;
            hostWin = null;
            var w = Window.GetWindow(this);
            if (w != null)
            {
                w.PreviewMouseMove -= OnMouseMove;
                w.PreviewMouseLeftButtonDown -= OnMouseDown;
            }
            CompositionTarget.Rendering -= OnRendering;
        }

        void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (mode == SteveMode.Acting) return;   // H史演戏时头归剧本管
            if (ActualWidth < 2 || ActualHeight < 2) return;
            var p = e.GetPosition(this);
            double rx = (p.X - ActualWidth / 2) / (ActualWidth / 2);
            double ry = (p.Y - ActualHeight / 2) / (ActualHeight / 2);
            targetYaw = Clamp(rx * 120, -120, 120);    // 左右转头（头身夹角超 75° 身体跟转）
            targetPitch = Clamp(ry * 35, -35, 35);     // 抬头低头（鼠标在下 = 低头看）
        }

        void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            // H史演戏中点击 = 打断剧本：吃掉这次点击（不挥臂），取消演戏并撤锁
            if (mode == SteveMode.Acting)
            {
                e.Handled = true;
                HugeLauncher.Ai.SteveDirector.Cancel();
                return;
            }
            Swing();
        }

        // 挥臂（MC 左键攻击）：进度交给渲染循环推进，公式与原版一致
        public void Swing()
        {
            if (!ThemeManager.AnimOn) return;
            swingP = 0;
        }

        // 原版挥臂公式（反编译 ModelBiped.setRotationAngles 的 swingProgress 段，1.12 时代沿用至今）：
        //   f1 = 1-(1-p)⁴，f2 = sin(f1·π)   —— 抬臂波形，p≈0.16 到峰，p=1 精确归零
        //   f3 = sin(p·π)·0.525             —— 头部俯仰项（取平视 -(0-0.7)·0.75）
        //   俯仰 rx = -(f2·1.2 + f3)        —— 从垂手 0° 起最多抬 ~83°，绝不翻过头顶
        //   身扭 bd = -sin(√p·2π)·0.2 rad；内摆 ry = bd·2；滚转 rz = sin(p·π)·0.4 rad
        // 符号经离屏标定渲染实证与 MC 一致（对照 15:47 验证版 exe 反编译复核）：负 yaw=手收向胸口（内扫）
        static void ComputeSwing(double p, out double rx, out double ry, out double rz, out double bd)
        {
            const double Rad2Deg = 57.29578;
            double f1 = 1 - Math.Pow(1 - p, 4);
            double f2 = Math.Sin(f1 * Math.PI);
            double f3 = Math.Sin(p * Math.PI) * 0.525;
            rx = -(f2 * 1.2 + f3) * Rad2Deg;
            bd = -Math.Sin(Math.Sqrt(p) * Math.PI * 2) * 0.2 * Rad2Deg;
            ry = bd * 2;
            rz = Math.Sin(p * Math.PI) * 0.4 * Rad2Deg;
        }

        void OnRendering(object sender, EventArgs e)
        {
            // 窗口最小化时整个停摆：看不见的东西不花一帧
            if (hostWin != null && hostWin.WindowState == WindowState.Minimized) return;
            var re = e as RenderingEventArgs;
            var now = re != null ? re.RenderingTime : lastRender;
            // 跟随降帧 30Hz：跟手时每帧重绘 3D 视口在软渲染机器上太贵，30Hz 观感无差；
            // 挥臂保持满帧（0.3s 短动画，手感优先）。dt 按实际间隔算，跟随速度不受影响
            if (swingP < 0 && lastRender != TimeSpan.Zero && (now - lastRender).TotalMilliseconds < 33) return;
            double dt = re != null ? (now - lastRender).TotalSeconds : 0.016;
            lastRender = now;
            if (dt <= 0 || dt > 0.2) dt = 0.016;

            // 挥臂推进（原版 6 tick ≈ 0.3s，按动画速度缩放；波形自身归零无需补间）
            if (swingP >= 0)
            {
                double dur = 0.3 / Math.Max(0.05, ThemeManager.Speed);
                swingP += dt / dur;
                if (swingP >= 1)
                {
                    swingP = -1;
                    swingBodyDeg = 0;
                    armSwing.Angle = 0;
                    armYaw.Angle = 0;
                    armRoll.Angle = 0;
                }
                else
                {
                    double rx, ry, rz, bd;
                    ComputeSwing(swingP, out rx, out ry, out rz, out bd);
                    armSwing.Angle = rx;
                    armYaw.Angle = ry;
                    armRoll.Angle = rz;
                    swingBodyDeg = bd;
                }
            }

            if (curYaw > 1e8) { curYaw = targetYaw; curPitch = targetPitch; }
            double k = ThemeManager.AnimOn ? Math.Min(1, dt * 10) : 1;
            double ny = curYaw + (targetYaw - curYaw) * k;
            double np = curPitch + (targetPitch - curPitch) * k;
            if (Math.Abs(ny - targetYaw) < 0.05) ny = targetYaw;
            if (Math.Abs(np - targetPitch) < 0.05) np = targetPitch;
            if (ny == curYaw && np == curPitch && swingP < 0)
            {
                if (mirrorDirty) RenderMirror(0);   // 姿态落定：补一张清晰的最终帧（被限帧跳过的末拍）
                return;   // 完全静止才不触发重绘
            }
            curYaw = ny; curPitch = np;
            UpdateBodyFollow(dt);
            modelYaw.Angle = curBody;                 // 整体朝向（转头超限时身体跟转）
            bodyTwist.Angle = swingBodyDeg;           // 挥臂躯干扭转（带双臂公转）
            leftArmPitch.Angle = swingBodyDeg;        // MC：挥臂时左臂 rotX += 身扭
            headYaw.Angle = curYaw - curBody;         // 头 = 相对身体的差角（限位后 ≤75°）
            headPitch.Angle = curPitch;
            RenderMirror(0);      // 姿态每变一帧同步更镜像：与视口同帧内容，透明视口下不透出旧姿态残影
        }

        // 原版 EntityLivingBase.updateDistance 的站立态移植（mcp940，EntityLivingBase.java:2489）：
        // 头身夹角硬限 ±75°，超 50° 身体追赶。原版「每刻对整个夹角跳 20%」是帧量化步进：
        // 越界瞬间白跳 10°、回死区又骤停，30Hz 裸渲染下肉眼可见地一顿一顿。
        // 改为等效速率（0.8²⁰/s ≈ e^(-4.5/s)）的时间连续追赶，且只追超出死区的部分——
        // 死区/硬限规则与原版一致，但身体连续滑动不再跳步
        void UpdateBodyFollow(double dt)
        {
            double f1 = WrapDeg(curYaw - curBody);
            double excess = f1 > 50 ? f1 - 50 : (f1 < -50 ? f1 + 50 : 0);   // 死区 ±50°：超出多少追多少
            if (excess != 0)
                curBody += excess * (1 - Math.Exp(-4.5 * dt));
            double f2 = Clamp(WrapDeg(curYaw - curBody), -75, 75);   // 硬限 ±75° 保底
            curBody = curYaw - f2;
        }

        static double WrapDeg(double a)
        {
            a = a % 360;
            if (a >= 180) a -= 360;
            if (a < -180) a += 360;
            return a;
        }

        static double Clamp(double v, double lo, double hi)
        {
            return v < lo ? lo : (v > hi ? hi : v);
        }

        // ---------- 3D 建模：一个带 MC 皮肤 UV 的立方体 ----------

        // 皮肤贴图放大倍数（最近邻），UV 以放大后的像素计
        const int SkinScale = 8;

        // 在 (cx,cy,cz) 处放一个尺寸 (sx,sy,sz) 的立方体；
        // (tx,ty) 是 MC 皮肤布局里该盒子的贴图原点，(w,h,d) 是皮肤用的盒子尺寸
        static void BuildBox(MeshGeometry3D m, double cx, double cy, double cz,
                             double sx, double sy, double sz,
                             int tx, int ty, int w, int h, int d)
        {
            double hx = sx / 2, hy = sy / 2, hz = sz / 2;
            // MC 皮肤盒 UV 布局（front/back/west/east/top/bottom 各自取一块）
            Face(m, P(cx - hx, cy + hy, cz + hz), P(cx + hx, cy + hy, cz + hz), P(cx + hx, cy - hy, cz + hz), P(cx - hx, cy - hy, cz + hz), tx + d, ty + d, w, h);          // 前
            Face(m, P(cx + hx, cy + hy, cz - hz), P(cx - hx, cy + hy, cz - hz), P(cx - hx, cy - hy, cz - hz), P(cx + hx, cy - hy, cz - hz), tx + 2 * d + w, ty + d, w, h); // 后
            Face(m, P(cx - hx, cy + hy, cz - hz), P(cx - hx, cy + hy, cz + hz), P(cx - hx, cy - hy, cz + hz), P(cx - hx, cy - hy, cz - hz), tx, ty + d, d, h);             // 右外侧
            Face(m, P(cx + hx, cy + hy, cz + hz), P(cx + hx, cy + hy, cz - hz), P(cx + hx, cy - hy, cz - hz), P(cx + hx, cy - hy, cz + hz), tx + d + w, ty + d, d, h);     // 左外侧
            Face(m, P(cx - hx, cy + hy, cz - hz), P(cx + hx, cy + hy, cz - hz), P(cx + hx, cy + hy, cz + hz), P(cx - hx, cy + hy, cz + hz), tx + d, ty, w, d);             // 顶
            Face(m, P(cx - hx, cy - hy, cz + hz), P(cx + hx, cy - hy, cz + hz), P(cx + hx, cy - hy, cz - hz), P(cx - hx, cy - hy, cz - hz), tx + d + w, ty, w, d);         // 底
        }

        static Point3D P(double x, double y, double z) { return new Point3D(x, y, z); }

        // 一个四边面：tl/tr/br/bl 为从外侧看的四角；(u,v,w,h) 为皮肤像素矩形（未放大）
        static void Face(MeshGeometry3D m, Point3D tl, Point3D tr, Point3D br, Point3D bl,
                         double u, double v, double w, double h)
        {
            int i0 = m.Positions.Count;
            m.Positions.Add(tl); m.Positions.Add(tr); m.Positions.Add(br); m.Positions.Add(bl);
            // 法线 = (bl-tl) × (tr-tl)（外法线）
            var n = Vector3D.CrossProduct(bl - tl, tr - tl);
            n.Normalize();
            for (int i = 0; i < 4; i++) m.Normals.Add(n);
            // UV：贴图向内收 1 个放大像素，防止边缘渗色
            double texW = 64.0 * SkinScale, texH = 64.0 * SkinScale;
            double u0 = (u * SkinScale + 1) / texW, u1 = ((u + w) * SkinScale - 1) / texW;
            double v0 = (v * SkinScale + 1) / texH, v1 = ((v + h) * SkinScale - 1) / texH;
            // WPF 3D 纹理坐标 (0,0)=图像左上角（整图四边形测试实证），皮肤行号直接使用
            m.TextureCoordinates.Add(new Point(u0, v0));
            m.TextureCoordinates.Add(new Point(u1, v0));
            m.TextureCoordinates.Add(new Point(u1, v1));
            m.TextureCoordinates.Add(new Point(u0, v1));
            // 从外侧看逆时针为正面：TL→BL→BR 与 TL→BR→TR
            m.TriangleIndices.Add(i0 + 0); m.TriangleIndices.Add(i0 + 3); m.TriangleIndices.Add(i0 + 2);
            m.TriangleIndices.Add(i0 + 0); m.TriangleIndices.Add(i0 + 2); m.TriangleIndices.Add(i0 + 1);
        }

        // ---------- 皮肤加载与处理 ----------

        static BitmapSource cachedSkin;

        public static BitmapSource LoadSkin()
        {
            if (cachedSkin != null) return cachedSkin;
            cachedSkin = LoadSkinCore();
            return cachedSkin;
        }

        static BitmapSource LoadSkinCore()
        {
            try
            {
                string cache = Path.Combine(Paths.LauncherDir, "cache", "steve.png");
                if (File.Exists(cache)) return LoadPng(File.ReadAllBytes(cache));
                var bytes = ExtractSkinFromJars();
                if (bytes != null)
                {
                    try { Directory.CreateDirectory(Path.GetDirectoryName(cache)); File.WriteAllBytes(cache, bytes); } catch { }
                    return LoadPng(bytes);
                }
                bytes = LoadEmbeddedSkin();
                if (bytes != null) return LoadPng(bytes);
            }
            catch { }
            return MakeFallbackSkin();
        }

        // 内嵌资源兜底（build.bat 把 Images/steve.png 以 "stevepng" 打进 exe）：
        // exe 被单独复制到别的目录时，既没有 cache/steve.png 也没有版本 jar，皮肤必须还能出来
        static byte[] LoadEmbeddedSkin()
        {
            try
            {
                var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("stevepng");
                if (s == null) return null;
                using (s)
                using (var ms = new MemoryStream())
                {
                    s.CopyTo(ms);
                    return ms.ToArray();
                }
            }
            catch { return null; }
        }

        // 从任意一个版本 jar 里抽 steve.png（jar 就是 zip）
        static byte[] ExtractSkinFromJars()
        {
            string[] entries = new[]
            {
                "assets/minecraft/textures/entity/player/wide/steve.png",
                "assets/minecraft/textures/entity/steve.png"
            };
            string[] jars;
            try { jars = Directory.Exists(Paths.VersionsDir) ? Directory.GetFiles(Paths.VersionsDir, "*.jar", SearchOption.AllDirectories) : new string[0]; }
            catch { return null; }
            foreach (var jar in jars)
            {
                try
                {
                    using (var zip = ZipFile.OpenRead(jar))
                    {
                        foreach (var name in entries)
                        {
                            var entry = zip.GetEntry(name);
                            if (entry == null) continue;
                            using (var s = entry.Open())
                            using (var ms = new MemoryStream())
                            {
                                s.CopyTo(ms);
                                return ms.ToArray();
                            }
                        }
                    }
                }
                catch { }
            }
            return null;
        }

        static BitmapSource LoadPng(byte[] bytes)
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.StreamSource = new MemoryStream(bytes);
            bi.EndInit();
            bi.Freeze();
            return bi;
        }

        // 由图集建 3D 材质
        static DiffuseMaterial MakeMaterial(BitmapSource atlas)
        {
            var ib = new ImageBrush(atlas);
            // 关键：WPF 画刷默认 ViewportUnits=RelativeToBoundingBox，3D 下会按网格纹理坐标的
            // 包围盒归一化（图集子区域采样全乱）；改成 Absolute 让 TC 直接对应图像 0..1 空间
            ib.ViewportUnits = BrushMappingMode.Absolute;
            ib.Viewport = new Rect(0, 0, 1, 1);
            var m = new DiffuseMaterial(ib);
            m.Freeze();
            return m;
        }

        // 皮肤有没有帽子层：(32,0)-(56,16) 区域存在 alpha≥128 的像素才算。
        // 没有就不建帽子盒——全透盒子也会进透明渲染队列参与深度/混合，就是"透明壳"的来源
        static bool HasHatLayer(BitmapSource skin)
        {
            if (skin.PixelWidth < 56 || skin.PixelHeight < 16) return false;
            var bgra = new FormatConvertedBitmap(skin, PixelFormats.Bgra32, null, 0);
            int w = bgra.PixelWidth;
            var px = new byte[w * bgra.PixelHeight * 4];
            bgra.CopyPixels(px, w * 4, 0);
            for (int y = 0; y < 16; y++)
                for (int x = 32; x < 56; x++)
                    if (px[(y * w + x) * 4 + 3] >= 128) return true;
            return false;
        }

        // 皮肤预处理：hatLayer=false → 所有像素 alpha=255 并转成无 alpha 的 Bgr24
        // （基础层，MC 本就不透明渲染；无 alpha 通道 WPF 才会把它归到 opaque 队列）；
        // hatLayer=true → 只保留帽子盒 UV 区 (32,0)-(56,16)（顶/底行 + 四个侧面行），
        // alpha 二值化（<128 全透，否则不透明），其余全透。
        // 写 alpha 前先做 16 轮颜色洪泛：把不透明像素的 RGB 扩散进相邻透明像素——
        // 透明像素的 RGB 是脏数据（常为纯黑/白），不扩散的话边缘采样会把脏色带出来
        static BitmapSource MakeAtlas(BitmapSource skin, bool hatLayer)
        {
            int w = skin.PixelWidth, h = skin.PixelHeight;
            var bgra = new FormatConvertedBitmap(skin, PixelFormats.Bgra32, null, 0);
            byte[] px = new byte[w * h * 4];
            bgra.CopyPixels(px, w * 4, 0);
            var opaque = new bool[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (hatLayer)
                    {
                        bool inHat = x >= 32 && x < 56 && y < 16;
                        opaque[i] = inHat && px[i * 4 + 3] >= 128;
                    }
                    else opaque[i] = px[i * 4 + 3] > 0;
                }
            }
            var filled = (bool[])opaque.Clone();
            for (int round = 0; round < 16; round++)
            {
                var todo = new List<int>();
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        int i = y * w + x;
                        if (filled[i]) continue;
                        int from = -1;
                        if (x > 0 && filled[i - 1]) from = i - 1;
                        else if (x < w - 1 && filled[i + 1]) from = i + 1;
                        else if (y > 0 && filled[i - w]) from = i - w;
                        else if (y < h - 1 && filled[i + w]) from = i + w;
                        if (from < 0) continue;
                        px[i * 4] = px[from * 4];
                        px[i * 4 + 1] = px[from * 4 + 1];
                        px[i * 4 + 2] = px[from * 4 + 2];
                        todo.Add(i);
                    }
                }
                if (todo.Count == 0) break;
                foreach (var i in todo) filled[i] = true;
            }
            for (int i = 0; i < w * h; i++)
                px[i * 4 + 3] = hatLayer ? (opaque[i] ? (byte)255 : (byte)0) : (byte)255;
            var src = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
            src.Freeze();
            var up = Upscale8(src);
            if (hatLayer) return up;
            var flat = new FormatConvertedBitmap(up, PixelFormats.Bgr24, null, 0);
            flat.Freeze();
            return flat;
        }

        // 最近邻放大 8 倍：3D 采样默认双线性会糊，放大后接近像素风
        // （用 System.Drawing 做真正的最近邻放大，WPF 的 BitmapScalingMode 在这里靠不住）
        static BitmapSource Upscale8(BitmapSource skin)
        {
            int w = skin.PixelWidth, h = skin.PixelHeight;
            // BitmapSource → System.Drawing.Bitmap（先统一转成 Bgra32，字节序与 Format32bppArgb 一致）
            var bgra = new FormatConvertedBitmap(skin, PixelFormats.Bgra32, null, 0);
            var src = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var data = src.LockBits(new System.Drawing.Rectangle(0, 0, w, h),
                System.Drawing.Imaging.ImageLockMode.WriteOnly, src.PixelFormat);
            bgra.CopyPixels(Int32Rect.Empty, data.Scan0, data.Height * data.Stride, data.Stride);
            src.UnlockBits(data);
            // 最近邻放大
            var dst = new System.Drawing.Bitmap(w * SkinScale, h * SkinScale, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = System.Drawing.Graphics.FromImage(dst))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                g.DrawImage(src, new System.Drawing.Rectangle(0, 0, dst.Width, dst.Height),
                    new System.Drawing.Rectangle(0, 0, w, h), System.Drawing.GraphicsUnit.Pixel);
            }
            src.Dispose();
            using (var ms = new MemoryStream())
            {
                dst.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                dst.Dispose();
                return LoadPng(ms.ToArray());
            }
        }

        // 兜底：代码画一个经典史蒂夫配色（拿不到 jar 时用）
        static BitmapSource MakeFallbackSkin()
        {
            var bmp = new System.Drawing.Bitmap(64, 64);
            var skin = System.Drawing.Color.FromArgb(197, 142, 104);
            var hair = System.Drawing.Color.FromArgb(59, 40, 24);
            var shirt = System.Drawing.Color.FromArgb(0, 168, 168);
            var pants = System.Drawing.Color.FromArgb(58, 58, 178);
            var shoe = System.Drawing.Color.FromArgb(120, 120, 120);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.Clear(System.Drawing.Color.Transparent);
                // 头：脸 + 头发 + 眼睛
                Box(g, 0, 0, 8, 8, 8, hair, hair, hair, hair, hair, hair);          // 先全头发色
                FaceRect(g, 8, 8, 8, 8, skin);                                      // 脸
                g.FillRectangle(new System.Drawing.SolidBrush(hair), 8, 8, 8, 2);   // 刘海
                g.FillRectangle(new System.Drawing.SolidBrush(System.Drawing.Color.White), 9, 12, 2, 1);   // 左眼白
                g.FillRectangle(new System.Drawing.SolidBrush(System.Drawing.Color.White), 13, 12, 2, 1);  // 右眼白
                g.FillRectangle(new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(74, 58, 140)), 10, 12, 1, 1);
                g.FillRectangle(new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(74, 58, 140)), 13, 12, 1, 1);
                // 身体：青色 T 恤
                Box(g, 16, 16, 8, 12, 4, shirt, shirt, shirt, shirt, shirt, shirt);
                // 手臂：上 1/3 袖子青色，下皮肤
                Limb(g, 40, 16, shirt, skin);   // 右臂
                Limb(g, 32, 48, shirt, skin);   // 左臂
                // 腿：蓝裤 + 灰鞋
                Leg(g, 0, 16, pants, shoe);
                Leg(g, 16, 48, pants, shoe);
            }
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                return LoadPng(ms.ToArray());
            }
        }

        static void FaceRect(System.Drawing.Graphics g, int x, int y, int w, int h, System.Drawing.Color c)
        {
            g.FillRectangle(new System.Drawing.SolidBrush(c), x, y, w, h);
        }

        // 按 MC 布局给一个盒子的六个面上色（前/后/左/右/顶/底）
        static void Box(System.Drawing.Graphics g, int tx, int ty, int w, int h, int d,
                        System.Drawing.Color front, System.Drawing.Color back, System.Drawing.Color west,
                        System.Drawing.Color east, System.Drawing.Color top, System.Drawing.Color bottom)
        {
            FaceRect(g, tx + d, ty + d, w, h, front);
            FaceRect(g, tx + 2 * d + w, ty + d, w, h, back);
            FaceRect(g, tx, ty + d, d, h, west);
            FaceRect(g, tx + d + w, ty + d, d, h, east);
            FaceRect(g, tx + d, ty, w, d, top);
            FaceRect(g, tx + d + w, ty, w, d, bottom);
        }

        static void Limb(System.Drawing.Graphics g, int tx, int ty, System.Drawing.Color sleeve, System.Drawing.Color skin)
        {
            Box(g, tx, ty, 4, 12, 4, skin, skin, skin, skin, sleeve, skin);
            for (int i = 0; i < 6; i++)
            {
                int x = tx, y = ty + 4, w = 0;
                if (i == 0) { x = tx + 4; w = 4; }        // 前
                else if (i == 1) { x = tx + 12; w = 4; }  // 后
                else if (i == 2) { x = tx; w = 4; }       // 侧
                else if (i == 3) { x = tx + 8; w = 4; }   // 侧
                else break;
                g.FillRectangle(new System.Drawing.SolidBrush(sleeve), x, y, w, 4);
            }
        }

        static void Leg(System.Drawing.Graphics g, int tx, int ty, System.Drawing.Color pants, System.Drawing.Color shoe)
        {
            Box(g, tx, ty, 4, 12, 4, pants, pants, pants, pants, pants, shoe);
            // 鞋：各侧面底部两行
            int[] xs = { tx + 4, tx + 12, tx, tx + 8 };
            for (int i = 0; i < 4; i++)
                g.FillRectangle(new System.Drawing.SolidBrush(shoe), xs[i], ty + 14, 4, 2);
        }
    }
}
