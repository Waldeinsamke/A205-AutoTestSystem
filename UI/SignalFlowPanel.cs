using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using A205AutoTestSystem.Common;

namespace A205AutoTestSystem.UI
{
    /// <summary>
    /// 接收机测试信号链路拓扑面板（纯 GDI+自绘，无图表控件依赖）。
    /// <para>按测试信号实际流向绘制汇聚拓扑：</para>
    /// <list type="bullet">
    /// <item>RF 信号源 → 接收机（射频输入）</item>
    /// <item>LO 信号源 → 接收机（本振注入）</item>
    /// <item>接收机 → 频谱仪（IF 输出观测）</item>
    /// </list>
    /// <para><b>节点三态语义</b>（由 <see cref="InstrumentLinkState"/> 驱动）：</para>
    /// <list type="bullet">
    /// <item><see cref="InstrumentLinkState.Ready"/> 绿色：已连接且通信正常</item>
    /// <item><see cref="InstrumentLinkState.Idle"/> 灰色：从未连接或已正常断开</item>
    /// <item><see cref="InstrumentLinkState.Fault"/> 红色 +⚠ 标记：地址为空 / 连接抛异常 /
    /// 掉线后探测失败 / 连接期间掉线。此状态是操作工最需要关注的告警。</item>
    /// </list>
    /// <para>节点状态与链路易通性完全由外部推送（各 <see cref="InstrumentLinkState"/> 属性），
    /// 本控件不主动查询任何仪表或串口，也不持有 Timer 轮询；
    /// 唯一的 <see cref="System.Windows.Forms.Timer"/> 仅用于链路流动虚线的重绘动画，
    /// 不做任何 I/O，且无活动链路时自动停止。</para>
    /// <para><b>链路的两个维度（重要语义约定）</b>：</para>
    /// <list type="bullet">
    /// <item><b>虚实 + 颜色 = 连接是否建立</b>：两端仪表都 <see cref="InstrumentLinkState.Ready"/>
    /// → 亮绿虚线；否则灰色实线。断开接收机串口会让全部连线变灰实线；
    /// 而<b>关掉信号源输出不会影响虚线</b>——连接确实还在。</item>
    /// <item><b>虚线流动 = 有信号/电正在通过</b>：供电中 + 输出开 + 两端就绪 → 流动；
    /// 仅就绪而未供电/未开输出 → 静态虚线。</item>
    /// </list>
    /// <para>把"连上了"与"在跑"分成两个维度，是为了让操作员一眼区分
    /// "线没接"与"接了但没发信号"这两种完全不同的故障。</para>
    /// <para>绘制坐标系为固定的 <c>371×190</c> 逻辑单位，再按实际控件尺寸等比缩放并居中，
    /// 以适配高 DPI 下 WinForms 自动缩放带来的控件尺寸变化。</para>
    /// </summary>
    public class SignalFlowPanel : Control
    {
        // ---- 逻辑设计尺寸（等比缩放基准，与 Designer 中的实际尺寸解耦）----
        // 高度方向留出底部页脚区；所有节点坐标均落在 [0, DesignWidth] 内且不贴边，
        // 避免等比缩放后因舍入误差被裁掉。
        // DesignHeight略大于内容实际高度（页脚文字底部约 187），给页脚下方留一点余量，
        // 防止文字贴死画布下边缘。
        private const float DesignWidth = 371f;
        private const float DesignHeight = 190f;

        /// <summary>拓扑图节点总数（RF / LO / 接收机 / 频谱仪 / 直流电源）。</summary>
        private const int TotalNodeCount = 5;

        // ---- 就绪态配色 ----
        private static readonly Color ReadyFill = Color.FromArgb(232, 245, 238);
        private static readonly Color ReadyBorder = Color.FromArgb(0, 138, 68);
        private static readonly Color ReadyTitle = Color.FromArgb(0, 77, 38);
        private static readonly Color ReadyDetail = Color.FromArgb(45, 110, 80);

        // ---- 未连接（空闲）态配色 ----
        private static readonly Color IdleFill = Color.FromArgb(246, 246, 247);
        private static readonly Color IdleBorder = Color.FromArgb(203, 203, 210);
        private static readonly Color IdleTitle = Color.FromArgb(122, 122, 132);
        private static readonly Color IdleDetail = Color.FromArgb(150, 150, 158);

        // ---- 故障态配色：红色警示，标题加粗以突出 ----
        private static readonly Color FaultFill = Color.FromArgb(253, 236, 234);
        private static readonly Color FaultBorder = Color.FromArgb(197, 48, 38);
        private static readonly Color FaultTitle = Color.FromArgb(155, 34, 26);
        private static readonly Color FaultDetail = Color.FromArgb(186, 66, 57);

        private static readonly Color LinkActive = Color.FromArgb(0, 150, 80);
        private static readonly Color LinkIdle = Color.FromArgb(205, 205, 212);

        private static readonly Color HeaderText = Color.FromArgb(60, 60, 68);
        private static readonly Color CaptionText = Color.FromArgb(112, 112, 122);

        // ---- 动画状态 ----
        private float _dashOffset;
        private readonly System.Windows.Forms.Timer _animationTimer;

        /// <summary>
        /// 实际绘制使用的底色缓存，避免每帧遍历父容器链。
        /// <para>取值时机为句柄创建时：此时父容器（ReceiverControlPanel，已显式设为白色）
        /// 已完全挂载，能读到页面真正使用的颜色。</para>
        /// </summary>
        private Color _effectiveBackColor = Color.White;

        // ---- 外部推送的三态状态 ----
        private InstrumentLinkState _rfState;
        private InstrumentLinkState _loState;
        private InstrumentLinkState _receiverState;
        private InstrumentLinkState _spectrumState;
        private InstrumentLinkState _powerState;
        private bool _rfOutputOn;
        private bool _loOutputOn;
        private bool _powerOn;

        public SignalFlowPanel()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw,
                true);
            // 不透明底色，与父容器（ReceiverControlPanel，已设为白色）保持一致。
            // <para>曾尝试 BackColor = Color.Transparent，但本控件位于 TabPage 内，
            // 而 TabPage 在 UseVisualStyleBackColor = true 时使用视觉样式渲染，
            // <b>不支持子控件的透明背景</b>——子控件声明透明后 WinForms 得不到
            // 可透出的底色，最终渲染为纯黑。</para>
            BackColor = Color.White;
            // 自绘控件不参与 Tab 导航，避免抢占界面键盘焦点
            TabStop = false;
            _animationTimer = new System.Windows.Forms.Timer { Interval = 70 };
            _animationTimer.Tick += OnAnimationTick;
        }

        /// <summary>
        /// 取实际绘制底色：直接采用父容器（<c>ReceiverControlPanel</c>）的 <c>BackColor</c>。
        /// <para>父容器已在 Designer 中显式设为白色，作为整页底色的唯一来源；
        /// 本控件与页面空白区因此天然一致，无需再猜测或回退。</para>
        /// <para>仍保留 <see cref="Color.Empty"/>（透明黑）防御：父容器若被改成未设色，
        /// 直接使用会渲染为纯黑。</para>
        /// </summary>
        private Color ResolveBackground()
        {
            Control parent = Parent;
            if (parent != null && parent.BackColor.A != 0)
            {
                return parent.BackColor;
            }
            return Color.White;
        }

        /// <summary>
        /// 父容器或自身背景变化时重新取色并重绘（换肤、切换 Tab、主题切换等场景）。
        /// </summary>
        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);
            RefreshEffectiveBackColor();
        }

        protected override void OnBackColorChanged(EventArgs e)
        {
            base.OnBackColorChanged(e);
            RefreshEffectiveBackColor();
        }

        /// <summary>重新取色并刷新。仅在父容器链上的背景确实变化时才重绘，避免无谓闪烁。</summary>
        private void RefreshEffectiveBackColor()
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }

            Color resolved = ResolveBackground();
            if (resolved.ToArgb() == _effectiveBackColor.ToArgb())
            {
                return;
            }

            _effectiveBackColor = resolved;
            Invalidate();
        }

        // ============================================================
        // 状态属性：外部状态变化时直接赋值，控件自行重绘
        //
        // 每个 setter 除 Invalidate() 外还调用 SyncAnimationState()：
        // 动画计时器的启停此前只挂在 OnHandleCreated / OnVisibleChanged 上，
        // 导致"连接仪表 → 开启供电"这类纯状态推送后虚线不流动，
        // 必须切走再切回 Tab 页（触发 OnVisibleChanged）才会动。
        // 状态一变就同步判据，动画的启停不再依赖任何外部事件。
        // ============================================================

        /// <summary>RF 信号源连接状态（就绪 / 未连接 / 故障）。</summary>
        public InstrumentLinkState RfState
        {
            get { return _rfState; }
            set { _rfState = value; Invalidate(); SyncAnimationState(); }
        }

        /// <summary>LO 信号源连接状态（就绪 / 未连接 / 故障）。</summary>
        public InstrumentLinkState LoState
        {
            get { return _loState; }
            set { _loState = value; Invalidate(); SyncAnimationState(); }
        }

        /// <summary>接收机串口连接状态（就绪 / 未连接 / 故障）。</summary>
        public InstrumentLinkState ReceiverState
        {
            get { return _receiverState; }
            set { _receiverState = value; Invalidate(); SyncAnimationState(); }
        }

        /// <summary>频谱仪连接状态（就绪 / 未连接 / 故障）。</summary>
        public InstrumentLinkState SpectrumState
        {
            get { return _spectrumState; }
            set { _spectrumState = value; Invalidate(); SyncAnimationState(); }
        }

        /// <summary>RF 信号源射频输出是否已开启（:OUTP 1）。</summary>
        public bool RfOutputOn
        {
            get { return _rfOutputOn; }
            set { _rfOutputOn = value; Invalidate(); SyncAnimationState(); }
        }

        /// <summary>LO 信号源射频输出是否已开启（:OUTP 1）。</summary>
        public bool LoOutputOn
        {
            get { return _loOutputOn; }
            set { _loOutputOn = value; Invalidate(); SyncAnimationState(); }
        }

        /// <summary>
        /// 直流电源 VISA 连接状态（就绪 / 未连接 / 故障）。
        /// <para>电源作为独立节点绘制在接收机下方，向上的连线表示供电关系，
        /// 仅在 <see cref="PowerOn"/> 为 true 且本状态为 <see cref="InstrumentLinkState.Ready"/> 时点亮。</para>
        /// </summary>
        public InstrumentLinkState PowerState
        {
            get { return _powerState; }
            set { _powerState = value; Invalidate(); SyncAnimationState(); }
        }

        /// <summary>
        /// 直流电源是否正在供电（三通道统一开关）。供电是整条链路的总开关，
        /// 同时决定电源节点副标题显示"供电中"还是"未供电"。
        /// </summary>
        public bool PowerOn
        {
            get { return _powerOn; }
            set { _powerOn = value; Invalidate(); SyncAnimationState(); }
        }

        // ============================================================
        // 绘制
        // ============================================================

        /// <summary>
        /// 四条链路的绘制状态。
        /// <para><b>Connected</b>（虚实+颜色）= 连接是否建立；<b>Flowing</b>（虚线是否流动）= 有信号/电在跑。</para>
        /// </summary>
        private struct LinkStates
        {
            public bool RfConnected;
            public bool RfFlowing;
            public bool LoConnected;
            public bool LoFlowing;
            public bool SaConnected;
            public bool SaFlowing;
            public bool PowerConnected;
            public bool PowerFlowing;

            /// <summary>是否至少有一条链路正在流动（决定动画计时器是否需要运行）。</summary>
            public bool AnyFlowing => RfFlowing || LoFlowing || SaFlowing || PowerFlowing;
        }

        /// <summary>
        /// 计算四条链路的绘制状态——<b>唯一判据来源</b>。
        /// <para>OnPaint（画什么）与 SyncAnimationState（动不动）都调用本方法，
        /// 保证两者永远一致。历史上两处各写一份判据，曾出现"关掉信号输出后
        /// 频谱仪连线仍在流动"的不一致。</para>
        /// <para><b>connected</b>：两端仪表都 Ready 即可，与供电/输出无关——
        /// 关掉输出时连接依然存在，只是没有信号在跑。</para>
        /// <para><b>flowing</b>：
        /// <list type="bullet">
        /// <item>RF / LO 线：供电中 + 本机输出开 + 两端就绪；</item>
        /// <item>供电线：供电中 + 两端就绪（画的是"电"）；</item>
        /// <item>频谱仪线：接收机→频谱仪测的是 IF 输出，软件无法直接观测 IF 上有无信号，
        /// 故以"供电中 **且** 至少一台信号源在输出"作代理——全关输出时 IF 上确实没有信号。</item>
        /// </list>
        /// </summary>
        private LinkStates ComputeLinkStates()
        {
            bool receiverReady = _receiverState == InstrumentLinkState.Ready;
            bool anySourceOutput = _rfOutputOn || _loOutputOn;

            return new LinkStates
            {
                RfConnected = _rfState == InstrumentLinkState.Ready && receiverReady,
                RfFlowing = _rfState == InstrumentLinkState.Ready && receiverReady
                    && _powerOn && _rfOutputOn,

                LoConnected = _loState == InstrumentLinkState.Ready && receiverReady,
                LoFlowing = _loState == InstrumentLinkState.Ready && receiverReady
                    && _powerOn && _loOutputOn,

                SaConnected = receiverReady && _spectrumState == InstrumentLinkState.Ready,
                SaFlowing = receiverReady && _spectrumState == InstrumentLinkState.Ready
                    && _powerOn && anySourceOutput,

                PowerConnected = _powerState == InstrumentLinkState.Ready && receiverReady,
                PowerFlowing = _powerState == InstrumentLinkState.Ready && receiverReady
                    && _powerOn
            };
        }

        /// <summary>
        /// 用缓存的页面底色填充，避免重绘时残留上一帧残影。
        /// <para>填充色来自 <see cref="ResolveBackground"/>：沿父容器链取第一个不透明色，
        /// 与页面实际底色一致，因此视觉上无色差。</para>
        /// </summary>
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using (SolidBrush brush = new SolidBrush(_effectiveBackColor))
            {
                e.Graphics.FillRectangle(brush, ClientRectangle);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            // 显式铺底：确保节点区域即使在重绘动画中也始终有正确底色
            using (SolidBrush brush = new SolidBrush(_effectiveBackColor))
            {
                g.FillRectangle(brush, ClientRectangle);
            }

            float scale = Math.Min(Width / DesignWidth, Height / DesignHeight);
            if (scale <= 0f)
            {
                return;
            }
            float offsetX = (Width - DesignWidth * scale) / 2f;
            float offsetY = (Height - DesignHeight * scale) / 2f;

            GraphicsState saved = g.Save();
            g.TranslateTransform(offsetX, offsetY);
            g.ScaleTransform(scale, scale);

            try
            {
                // ---- 链路：两个维度分开 ----
                // connected（虚实+颜色）= 两端仪表是否都已就绪（连接是否建立）
                // flowing（虚线是否流动） = 是否有信号/电正在通过
                // 注：虚线**不再**受输出开关影响——关掉信号源输出时连接依然存在，
                //     只应表现为"虚线不流动"，而不是"线变灰实线"。
                //
                // 判据统一由 ComputeLinkStates() 提供，OnPaint 与 SyncAnimationState 共用，
                // 避免两处各写一份判据而互相漂移（历史上正是这样漏掉了"关输出后频谱仪线仍在流动"）。
                LinkStates links = ComputeLinkStates();

                DrawHeader(g);

                // ---- 布局常量：横向节点区左右各留 Margin ----
                const float Margin = 10f;
                const float SrcWidth = 97f;
                const float MidWidth = 86f;      // 接收机 / 频谱仪 / 直流电源三者同宽
                const float NodeHeight = 44f;

                float srcX = Margin;
                float rxX = srcX + SrcWidth + 38f;                 // 145
                float saX = rxX + MidWidth + 44f;                 // 275，右边缘 361=DesignWidth-10
                // 直流电源与接收机同 x 同宽：既在视觉上严格对齐，
                // 也让供电连线成为一条完美的垂直线（两端水平中心完全相同）。
                float powerX = rxX;
                float powerCenterX = powerX + MidWidth / 2f;

                DrawLink(g, new PointF(srcX + SrcWidth, 42f), new PointF(srcX + SrcWidth + 19f, 42f),
                    new PointF(rxX, 58f), links.RfConnected, links.RfFlowing);
                DrawLink(g, new PointF(srcX + SrcWidth, 98f), new PointF(srcX + SrcWidth + 19f, 98f),
                    new PointF(rxX, 84f), links.LoConnected, links.LoFlowing);
                DrawLink(g, new PointF(rxX + MidWidth, 70f), new PointF(rxX + MidWidth + 18f, 70f),
                    new PointF(saX, 70f), links.SaConnected, links.SaFlowing);
                // 供电连线：电源节点上边 → 接收机节点底边，两端水平中心一致（powerCenterX）。
                // 终点必须精确落在节点边缘（rxY + NodeHeight），否则箭头会停在半空看起来没连上；
                // 与其它链路一样走贝塞尔曲线，视觉风格保持统一。
                float rxBottom = 49f + NodeHeight;
                float powerTop = 118f;
                DrawLink(g, new PointF(powerCenterX, powerTop),
                    new PointF(powerCenterX, (powerTop + rxBottom) / 2f),
                    new PointF(powerCenterX, rxBottom), links.PowerConnected, links.PowerFlowing);

                // 节点副标题反映该节点自身的真实状态：
                // RF/LO = 输出开/输出关；接收机 = 串口在线；频谱仪 = VISA 在线；电源 = 供电中/未供电。
                // 注意：副标题文案必须由真实状态推导，不能硬编码——否则会出现
                // "已关闭输出但节点仍写输出开"的假状态。
                DrawNode(g, new RectangleF(srcX, 22f, SrcWidth, 40f), "RF 信号源",
                    _rfOutputOn ? "输出开" : "输出关",
                    _rfState, _rfOutputOn);
                DrawNode(g, new RectangleF(srcX, 80f, SrcWidth, 40f), "LO 信号源",
                    _loOutputOn ? "输出开" : "输出关",
                    _loState, _loOutputOn);
                DrawNode(g, new RectangleF(rxX, 49f, MidWidth, NodeHeight), "接收机", "串口在线",
                    _receiverState, _receiverState == InstrumentLinkState.Ready);
                DrawNode(g, new RectangleF(saX, 49f, MidWidth, NodeHeight), "频谱仪", "VISA 在线",
                    _spectrumState, _spectrumState == InstrumentLinkState.Ready);

                // 电源节点：与接收机同 x 同宽，完全对齐
                string powerDetail = _powerOn ? "供电中" : "未供电";
                DrawNode(g, new RectangleF(powerX, 118f, MidWidth, NodeHeight), "直流电源", powerDetail,
                    _powerState, _powerOn);

                DrawFooter(g);
            }
            finally
            {
                g.Restore(saved);
            }
        }

        private void DrawHeader(Graphics g)
        {
            using (Font titleFont = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold))
            using (Font countFont = new Font("Consolas", 9f, FontStyle.Bold))
            using (StringFormat sf = new StringFormat(StringFormatFlags.NoWrap))
            using (SolidBrush titleBrush = new SolidBrush(HeaderText))
            using (SolidBrush activeBrush = new SolidBrush(LinkActive))
            using (SolidBrush faultBrush = new SolidBrush(FaultBorder))
            {
                sf.Alignment = StringAlignment.Near;
                sf.LineAlignment = StringAlignment.Center;
                g.DrawString("信号链路", titleFont, titleBrush, new RectangleF(10f, 0f, 140f, 20f), sf);

                int readyCount = CountReady();
                int faultCount = CountFault();

                // 右上角状态摘要：故障优先于就绪计数，故障时显示红色数量。
                string summary;
                SolidBrush summaryBrush;
                if (faultCount > 0)
                {
                    summary = string.Format("⚠ {0} 台故障", faultCount);
                    summaryBrush = faultBrush;
                }
                else
                {
                    summary = string.Format("{0} / 5 就绪", readyCount);
                    summaryBrush = readyCount == TotalNodeCount ? activeBrush : titleBrush;
                }

                sf.Alignment = StringAlignment.Far;
                g.DrawString(summary, countFont, summaryBrush, new RectangleF(231f, 0f, 130f, 20f), sf);
            }
        }

        /// <summary>
        /// 绘制二次贝塞尔链路。
        /// <para><b>两个维度分开表达</b>（这是本控件的核心语义约定）：</para>
        /// <list type="bullet">
        /// <item><b>虚实 + 颜色 = 连接是否建立</b>：两端仪表都就绪 → 亮绿虚线；
        /// 否则灰色实线。断开接收机串口 → 全部变灰实线；关掉信号源输出
        /// <b>不会</b>影响虚线，因为连接确实还在。</item>
        /// <item><b>流动动画 = 有信号/电在跑</b>：供电中 + 输出开 + 两端就绪 → 虚线流动；
        /// 仅就绪而未供电/未开输出 → 静态虚线。</item>
        /// </list>
        /// <para>这样"连上了"与"在跑"是两件独立的事，关掉输出不会把连接状态一起抹掉，
        /// 动画也不会在无信号时误导操作者。</para>
        /// </summary>
        /// <param name="connected">两端仪表是否都已就绪（决定虚实与颜色）。</param>
        /// <param name="flowing">是否有信号/电正在通过（决定虚线是否流动）。</param>
        private void DrawLink(Graphics g, PointF start, PointF control, PointF end, bool connected, bool flowing)
        {
            Color color = connected ? LinkActive : LinkIdle;

            using (GraphicsPath path = new GraphicsPath())
            using (Pen pen = new Pen(color, 2f))
            using (SolidBrush headBrush = new SolidBrush(color))
            {
                path.AddBezier(start, control, control, end);

                if (connected)
                {
                    // 已连接即用虚线（表示"这是一条已建立的链路"）
                    pen.DashStyle = DashStyle.Custom;
                    pen.DashPattern = new float[] { 5f, 7f };
                    // 有信号/电在跑时才推进虚线偏移，形成流动效果
                    pen.DashOffset = flowing ? _dashOffset : 0f;
                }

                g.DrawPath(pen, path);

                // 箭头方向取贝塞尔末端切线（end - control），保证与连线走向一致
                double angle = Math.Atan2(end.Y - control.Y, end.X - control.X);
                DrawArrowHead(g, end, angle, headBrush);
            }
        }

        private static void DrawArrowHead(Graphics g, PointF tip, double angleRadians, Brush brush)
        {
            const float length = 8f;
            const float halfWidth = 4.5f;

            PointF baseCenter = new PointF(
                tip.X - (float)(Math.Cos(angleRadians) * length),
                tip.Y - (float)(Math.Sin(angleRadians) * length));

            double perp = angleRadians + Math.PI / 2.0;
            PointF p1 = new PointF(
                baseCenter.X + (float)(Math.Cos(perp) * halfWidth),
                baseCenter.Y + (float)(Math.Sin(perp) * halfWidth));
            PointF p2 = new PointF(
                baseCenter.X - (float)(Math.Cos(perp) * halfWidth),
                baseCenter.Y - (float)(Math.Sin(perp) * halfWidth));

            using (GraphicsPath head = new GraphicsPath())
            {
                head.AddPolygon(new PointF[] { tip, p1, p2 });
                g.FillPath(brush, head);
            }
        }

        /// <summary>
        /// 按三态绘制仪表节点框。
        /// <para>故障态：红底红边 + 描边加粗 + 名称前加⚠，副标题固定显示"连接异常"
        /// （不显示供电/输出等可能已过时的状态信息）。</para>
        /// <para>节点高度自适应：≥36 时标题与副标题分两行，<36 时标题与副标题挤在一行。</para>
        /// </summary>
        private void DrawNode(Graphics g, RectangleF rect, string title, string readyDetail,
            InstrumentLinkState state, bool outputOn)
        {
            Color fill, border, titleColor, detailColor;

            switch (state)
            {
                case InstrumentLinkState.Ready:
                    fill = ReadyFill;
                    border = ReadyBorder;
                    titleColor = ReadyTitle;
                    detailColor = outputOn ? ReadyBorder : ReadyDetail;
                    break;

                case InstrumentLinkState.Fault:
                    fill = FaultFill;
                    border = FaultBorder;
                    titleColor = FaultTitle;
                    detailColor = FaultDetail;
                    break;

                default:
                    fill = IdleFill;
                    border = IdleBorder;
                    titleColor = IdleTitle;
                    detailColor = IdleDetail;
                    break;
            }

            // 故障时副标题一律显示"连接异常"；就绪时显示调用方给的语义（如"供电中"/"输出开"）；
            // 未连接统一显示"未连接"。
            string detail = state == InstrumentLinkState.Fault
                ? "连接异常"
                : (state == InstrumentLinkState.Ready ? readyDetail : "未连接");

            bool twoLine = rect.Height >= 36f;
            // 故障态标题前加 ⚠ 前缀，即使节点框较窄也能第一眼识别
            string displayTitle = state == InstrumentLinkState.Fault ? "⚠ " + title : title;

            using (GraphicsPath box = CreateRoundedRect(rect, twoLine ? 8f : 6f))
            using (SolidBrush fillBrush = new SolidBrush(fill))
            using (Pen borderPen = new Pen(border, state == InstrumentLinkState.Fault ? 1.6f : 1f))
            using (Font titleFont = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold))
            using (Font detailFont = new Font("Microsoft YaHei UI", 8f, FontStyle.Regular))
            using (StringFormat sf = new StringFormat(StringFormatFlags.NoWrap))
            using (SolidBrush titleBrush = new SolidBrush(titleColor))
            using (SolidBrush detailBrush = new SolidBrush(detailColor))
            {
                g.FillPath(fillBrush, box);
                g.DrawPath(borderPen, box);

                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;

                if (twoLine)
                {
                    // 两行布局：标题居中于上半，副标题居中于下半
                    float titleHeight = rect.Height * 0.54f;
                    g.DrawString(displayTitle, titleFont, titleBrush,
                        new RectangleF(rect.X, rect.Y + 1f, rect.Width, titleHeight), sf);
                    g.DrawString(detail, detailFont, detailBrush,
                        new RectangleF(rect.X, rect.Y + rect.Height * 0.50f, rect.Width, rect.Height * 0.48f), sf);
                }
                else
                {
                    // 紧凑布局：标题与副标题并排显示，"标题 · 副标题"
                    using (Font inlineFont = new Font("Microsoft YaHei UI", 8f, FontStyle.Bold))
                    using (SolidBrush inlineBrush = new SolidBrush(state == InstrumentLinkState.Idle ? titleColor : detailColor))
                    {
                        g.DrawString(displayTitle + " · " + detail, inlineFont, inlineBrush,
                            new RectangleF(rect.X, rect.Y, rect.Width, rect.Height), sf);
                    }
                }
            }
        }

        private void DrawFooter(Graphics g)
        {
            // 不画分隔线，文字直接贴底：少一条线，节点区与底部文字区视觉上不再被切分。
            using (Font font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Regular))
            using (StringFormat sf = new StringFormat(StringFormatFlags.NoWrap))
            using (SolidBrush brush = new SolidBrush(CaptionText))
            {
                sf.Alignment = StringAlignment.Near;
                // 底部对齐而非居中：8.5pt 字体的实际行高略大于12 像素，
                // 若用 Center 会在框上下两侧溢出，顶部被控件边界裁掉一截。
                // Far 让文字紧贴框底，顶部留足余量，文字完整可见。
                sf.LineAlignment = StringAlignment.Far;
                // 文字框贴底：y=172、高 18，底部即 190=DesignHeight
                g.DrawString(BuildSummary(), font, brush, new RectangleF(10f, 172f, DesignWidth - 20f, 18f), sf);
            }
        }

        private string BuildSummary()
        {
            int fault = CountFault();
            if (fault > 0)
            {
                return string.Format("⚠ {0} 台仪表异常，请查看日志确认原因", fault);
            }

            int ready = CountReady();
            if (ready < TotalNodeCount)
            {
                return string.Format("{0} / {1} 就绪 · 请先连接全部仪表与接收机串口", ready, TotalNodeCount);
            }
            if (!_powerOn)
            {
                return "全部在线 · 点击“开启供电”建立测试链路";
            }
            // 注：此处不再判断 RF/LO 输出是否开启。
            // 输出状态已由节点副标题（"输出开"/"输出关"）与链路连线亮度表达，重复描述反而冗余；
            // 且原文案"暂不可测试"是主观判断——软件并未阻止运行，实际照跑照出结果，容易误导。
            return "链路贯通 · 可开始测试";
        }

        private int CountReady()
        {
            int count = 0;
            if (_rfState == InstrumentLinkState.Ready) count++;
            if (_loState == InstrumentLinkState.Ready) count++;
            if (_receiverState == InstrumentLinkState.Ready) count++;
            if (_spectrumState == InstrumentLinkState.Ready) count++;
            if (_powerState == InstrumentLinkState.Ready) count++;
            return count;
        }

        private int CountFault()
        {
            int count = 0;
            if (_rfState == InstrumentLinkState.Fault) count++;
            if (_loState == InstrumentLinkState.Fault) count++;
            if (_receiverState == InstrumentLinkState.Fault) count++;
            if (_spectrumState == InstrumentLinkState.Fault) count++;
            if (_powerState == InstrumentLinkState.Fault) count++;
            return count;
        }

        private static GraphicsPath CreateRoundedRect(RectangleF rect, float radius)
        {
            float diameter = radius * 2f;
            if (diameter > rect.Width) diameter = rect.Width;
            if (diameter > rect.Height) diameter = rect.Height;

            GraphicsPath path = new GraphicsPath();
            path.AddArc(rect.X, rect.Y, diameter, diameter, 180f, 90f);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270f, 90f);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0f, 90f);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90f, 90f);
            path.CloseFigure();
            return path;
        }

        // ============================================================
        // 动画生命周期：仅在存在活动链路时运行，无链路立即停止
        // ============================================================

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // 句柄就绪时缓存页面真实底色（此时父容器链已完全挂载），随后启动动画
            _effectiveBackColor = ResolveBackground();
            SyncAnimationState();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            _animationTimer.Stop();
            base.OnHandleDestroyed(e);
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            SyncAnimationState();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _animationTimer.Tick -= OnAnimationTick;
                _animationTimer.Stop();
                _animationTimer.Dispose();
            }
            base.Dispose(disposing);
        }

        /// <summary>
        /// 依据"是否存在活动链路"启停动画计时器。
        /// <para>判据直接取自 <see cref="ComputeLinkStates"/>——与 OnPaint 画虚线用的是同一份，
        /// 因此"虚线在动"与"图上画的是流动态"永远一致，不会两处判据各自漂移。</para>
        /// </summary>
        private void SyncAnimationState()
        {
            // 计时器在构造函数末尾才创建；setter 可能在构造完成前被调用（设计器赋值），
            // 此时直接返回，避免空引用。
            if (_animationTimer == null)
            {
                return;
            }

            bool needAnimation = ComputeLinkStates().AnyFlowing;

            if (needAnimation && !_animationTimer.Enabled && !IsDisposed && IsHandleCreated)
            {
                _animationTimer.Start();
            }
            else if (!needAnimation && _animationTimer.Enabled)
            {
                _animationTimer.Stop();
            }
        }

        private void OnAnimationTick(object sender, EventArgs e)
        {
            // 不在此处按 _powerOn 硬停止：动画的启停统一由 SyncAnimationState 的
            // needAnimation 判据决定，避免两套判据各说各话。
            // （"未供电但仪表仍连着"时链路是静态虚线——虚线样式要保留，只是不流动。）
            _dashOffset -= 1.6f;
            if (_dashOffset <= -24f)
            {
                _dashOffset = 0f;
            }
            Invalidate();
            SyncAnimationState();
        }
    }
}