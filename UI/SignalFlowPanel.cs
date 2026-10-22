using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace A205AutoTestSystem.UI
{
    /// <summary>
    /// 接收机测试信号链路拓扑面板（纯 GDI+ 自绘，无图表控件依赖）。
    /// <para>按测试信号实际流向绘制汇聚拓扑：</para>
    /// <list type="bullet">
    /// <item>RF 信号源 → 接收机（射频输入）</item>
    /// <item>LO 信号源 → 接收机（本振注入）</item>
    /// <item>接收机 → 频谱仪（IF 输出观测）</item>
    /// </list>
    /// <para>节点就绪状态与链路易通性完全由外部推送（见各类<see cref="Ready"/> 属性），
    /// 本控件不主动查询任何仪表或串口，也不持有 Timer 轮询；
    /// 唯一的 <see cref="System.Windows.Forms.Timer"/> 仅用于链路流动虚线的重绘动画，
    /// 不做任何 I/O，且无活动链路时自动停止。</para>
    /// <para>绘制坐标系为固定的 <c>371×190</c> 逻辑单位，再按实际控件尺寸等比缩放并居中，
    /// 以适配高 DPI 下 WinForms 自动缩放带来的控件尺寸变化。</para>
    /// </summary>
    public class SignalFlowPanel : Control
    {
        // ---- 逻辑设计尺寸（等比缩放基准，与 Designer 中的实际尺寸解耦）----
        private const float DesignWidth = 371f;
        private const float DesignHeight = 190f;

        // ---- 配色 ----
        private static readonly Color NodeReadyFill = Color.FromArgb(232, 245, 238);
        private static readonly Color NodeReadyBorder = Color.FromArgb(0, 138, 68);
        private static readonly Color NodeReadyTitle = Color.FromArgb(0, 77, 38);
        private static readonly Color NodeReadyDetail = Color.FromArgb(45, 110, 80);

        private static readonly Color NodeIdleFill = Color.FromArgb(246, 246, 247);
        private static readonly Color NodeIdleBorder = Color.FromArgb(203, 203, 210);
        private static readonly Color NodeIdleTitle = Color.FromArgb(122, 122, 132);
        private static readonly Color NodeIdleDetail = Color.FromArgb(150, 150, 158);

        private static readonly Color LinkActive = Color.FromArgb(0, 150, 80);
        private static readonly Color LinkIdle = Color.FromArgb(205, 205, 212);

        private static readonly Color HeaderText = Color.FromArgb(60, 60, 68);
        private static readonly Color CaptionText = Color.FromArgb(112, 112, 122);
        private static readonly Color DividerLine = Color.FromArgb(224, 224, 228);

        // ---- 动画状态 ----
        private float _dashOffset;
        private readonly System.Windows.Forms.Timer _animationTimer;

        // ---- 外部推送的状态 ----
        private bool _rfReady;
        private bool _loReady;
        private bool _receiverReady;
        private bool _spectrumReady;
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
            BackColor = SystemColors.Control;
            // 自绘控件不参与 Tab 导航，避免抢占界面键盘焦点
            TabStop = false;
            _animationTimer = new System.Windows.Forms.Timer { Interval = 70 };
            _animationTimer.Tick += OnAnimationTick;
        }

        // ============================================================
        // 状态属性：外部状态变化时直接赋值，控件自行重绘
        // ============================================================

        /// <summary>RF 信号源是否已连接。</summary>
        public bool RfReady
        {
            get { return _rfReady; }
            set { _rfReady = value; Invalidate(); }
        }

        /// <summary>LO 信号源是否已连接。</summary>
        public bool LoReady
        {
            get { return _loReady; }
            set { _loReady = value; Invalidate(); }
        }

        /// <summary>接收机串口是否已打开。</summary>
        public bool ReceiverReady
        {
            get { return _receiverReady; }
            set { _receiverReady = value; Invalidate(); }
        }

        /// <summary>频谱仪是否已连接。</summary>
        public bool SpectrumReady
        {
            get { return _spectrumReady; }
            set { _spectrumReady = value; Invalidate(); }
        }

        /// <summary>RF 信号源射频输出是否已开启（:OUTP 1）。</summary>
        public bool RfOutputOn
        {
            get { return _rfOutputOn; }
            set { _rfOutputOn = value; Invalidate(); }
        }

        /// <summary>LO 信号源射频输出是否已开启（:OUTP 1）。</summary>
        public bool LoOutputOn
        {
            get { return _loOutputOn; }
            set { _loOutputOn = value; Invalidate(); }
        }

        /// <summary>直流电源 CH2 是否正在供电。供电是整条链路的总开关。</summary>
        public bool PowerOn
        {
            get { return _powerOn; }
            set { _powerOn = value; Invalidate(); }
        }

        // ============================================================
        // 绘制
        // ============================================================

        /// <summary>使用父容器底色填充，避免重绘时残留上一帧残影。</summary>
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using (SolidBrush brush = new SolidBrush(BackColor))
            {
                e.Graphics.FillRectangle(brush, ClientRectangle);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.Clear(BackColor);

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
                bool rfLink = _powerOn && _rfReady && _rfOutputOn;
                bool loLink = _powerOn && _loReady && _loOutputOn;
                bool saLink = _powerOn && _receiverReady && _spectrumReady;

                DrawHeader(g);
                DrawLink(g, new PointF(99f, 54f), new PointF(118f, 54f), new PointF(135f, 74f), rfLink);
                DrawLink(g, new PointF(99f, 114f), new PointF(118f, 114f), new PointF(135f, 96f), loLink);
                DrawLink(g, new PointF(231f, 85f), new PointF(251f, 85f), new PointF(267f, 85f), saLink);

                DrawNode(g, new RectangleF(0f, 32f, 99f, 44f), "RF 信号源", "-20 dBm · 输出开", _rfReady, _rfOutputOn);
                DrawNode(g, new RectangleF(0f, 92f, 99f, 44f), "LO 信号源", "3 dBm · 输出开", _loReady, _loOutputOn);
                DrawNode(g, new RectangleF(139f, 62f, 92f, 46f), "接收机", "串口在线", _receiverReady, _receiverReady);
                DrawNode(g, new RectangleF(271f, 62f, 100f, 46f), "频谱仪", "VISA 在线", _spectrumReady, _spectrumReady);

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
            using (SolidBrush countBrush = new SolidBrush(LinkActive))
            {
                sf.Alignment = StringAlignment.Near;
                sf.LineAlignment = StringAlignment.Center;
                g.DrawString("信号链路", titleFont, titleBrush, new RectangleF(0f, 0f, 140f, 20f), sf);

                int readyCount = CountReady();
                sf.Alignment = StringAlignment.Far;
                g.DrawString(
                    string.Format("{0} / 4 就绪", readyCount),
                    countFont,
                    readyCount == 4 ? countBrush : titleBrush,
                    new RectangleF(231f, 0f, 140f, 20f),
                    sf);
            }
        }

        /// <summary>绘制二次贝塞尔链路；active 时使用流动虚线并带箭头。</summary>
        private void DrawLink(Graphics g, PointF start, PointF control, PointF end, bool active)
        {
            Color color = active ? LinkActive : LinkIdle;

            using (GraphicsPath path = new GraphicsPath())
            using (Pen pen = new Pen(color, 2f))
            using (SolidBrush headBrush = new SolidBrush(color))
            {
                path.AddBezier(start, control, control, end);

                if (active)
                {
                    pen.DashStyle = DashStyle.Custom;
                    pen.DashPattern = new float[] { 5f, 7f };
                    pen.DashOffset = _dashOffset;
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

        /// <summary>绘制仪表节点框：就绪时绿底描边，未就绪时灰化；输出开启时副标题转为绿色。</summary>
        private void DrawNode(Graphics g, RectangleF rect, string title, string readyDetail, bool ready, bool outputOn)
        {
            Color fill = ready ? NodeReadyFill : NodeIdleFill;
            Color border = ready ? NodeReadyBorder : NodeIdleBorder;
            Color titleColor = ready ? NodeReadyTitle : NodeIdleTitle;
            Color detailColor = !ready
                ? NodeIdleDetail
                : (outputOn ? NodeReadyBorder : NodeReadyDetail);

            using (GraphicsPath box = CreateRoundedRect(rect, 8f))
            using (SolidBrush fillBrush = new SolidBrush(fill))
            using (Pen borderPen = new Pen(border, 1f))
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
                g.DrawString(title, titleFont, titleBrush, new RectangleF(rect.X, rect.Y + 6f, rect.Width, 16f), sf);
                g.DrawString(
                    ready ? readyDetail : "未连接",
                    detailFont,
                    detailBrush,
                    new RectangleF(rect.X, rect.Y + 23f, rect.Width, 14f),
                    sf);
            }
        }

        private void DrawFooter(Graphics g)
        {
            using (Pen divider = new Pen(DividerLine, 1f))
            using (Font font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Regular))
            using (StringFormat sf = new StringFormat(StringFormatFlags.NoWrap))
            using (SolidBrush brush = new SolidBrush(CaptionText))
            {
                g.DrawLine(divider, 0f, 152f, DesignWidth, 152f);

                sf.Alignment = StringAlignment.Near;
                sf.LineAlignment = StringAlignment.Center;
                g.DrawString(BuildSummary(), font, brush, new RectangleF(0f, 158f, 371f, 18f), sf);
            }
        }

        private string BuildSummary()
        {
            int ready = CountReady();
            if (ready < 4)
            {
                return string.Format("{0} / 4 就绪 · 请先连接全部仪表与接收机串口", ready);
            }
            if (!_powerOn)
            {
                return "全部在线 · 点击“开启供电”建立测试链路";
            }
            if (!_rfOutputOn || !_loOutputOn)
            {
                return "已供电 · 信号源输出未开启，暂不可测试";
            }
            return "链路贯通 · 可开始测试";
        }

        private int CountReady()
        {
            int count = 0;
            if (_rfReady) count++;
            if (_loReady) count++;
            if (_receiverReady) count++;
            if (_spectrumReady) count++;
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
        /// <see cref="Invalidate"/> 会连带触发 <see cref="OnVisibleChanged"/> 之外的路径，
        /// 故在重绘入口与状态变化后都可能调用，统一收敛到此方法。
        /// </summary>
        private void SyncAnimationState()
        {
            bool needAnimation = _powerOn
                && ((_rfReady && _rfOutputOn) || (_loReady && _loOutputOn) || (_receiverReady && _spectrumReady));

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
            // 链路可能已全部断开但计时器尚未停止，兜底再确认一次
            if (!_powerOn)
            {
                _animationTimer.Stop();
                return;
            }
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