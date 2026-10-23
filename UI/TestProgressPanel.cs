using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace A205AutoTestSystem.UI
{
    /// <summary>
    /// 测试进度面板（纯 GDI+ 自绘，无图表控件依赖）。
    /// <para>显示当前批次的测试进度：进度环（含失败区间红弧）、总项数 / 通过 / 失败 /
    /// 本轮耗时四个数字块，以及底部当前测试项。所有状态由
    /// <see cref="ReceiverControlPanel.UpdateTestProgress"/> 推送，
    /// 本控件不主动查询任何资源，也不做 I/O。</para>
    /// <para><b>三态语义</b>：</para>
    /// <list type="bullet">
    /// <item><see cref="ProgressState.Idle"/>：尚未加载测试项，整块退为弱提示文案，
    /// 不显示 0%——把"还没开始"与"进度为零"区分开，避免误判为卡死。</item>
    /// <item><see cref="ProgressState.Running"/>：进度环按已测比例增长，徽章呼吸。</item>
    /// <item><see cref="ProgressState.Finished"/> / <see cref="ProgressState.Aborted"/>：
    /// 徽章与文案切换，数字冻结；失败数大于零时失败数转警示色。</item>
    /// </list>
    /// <para>唯一的 <see cref="System.Windows.Forms.Timer"/> 仅用于运行态徽章呼吸动画，
    /// 无运行态时自动停止，不做任何 I/O。</para>
    /// <para>绘制按实际控件尺寸自适应（不使用固定缩放基准），保证数字位置稳定；
    /// 环的外缘直径取设计值与内容区高度的较小者，数字块在内容区装不下时整体不画——
    /// 宁可留白，也不裁切字形。</para>
    /// </summary>
    public class TestProgressPanel : Control
    {
        // ============================================================
        // 布局常量（设计基准 449x153，实际按控件尺寸自适应）
        //
        // 纵向预算（153px 全高）：
        //   标题行    6 .. 25     （10.5pt 粗体行高 19px）
        //   内容区   32 .. 121    （进度环与 2x2 数字块并排，二者同高）
        //   分隔线   127
        //   页脚行   130 .. 146   （9pt 行高 16px，底部留 7px 余量）
        // ============================================================

        /// <summary>标题绘制上偏移。</summary>
        private const float TitleTop = 6f;

        /// <summary>内容区（进度环 / 数字块）顶部偏移。</summary>
        private const float BlockTop = 32f;

        /// <summary>单个数字块高度（设计基准）。</summary>
        private const float BlockHeight = 42f;

        /// <summary>两行数字块之间的间距。</summary>
        private const float BlockGap = 5f;

        /// <summary>
        /// 数字块的最小可用高度。低于此值不再绘制数字块内容（宁可留白也不裁字）。
        /// <para>该值由内容实际所需高度反推：标签行高 16px + 数值行高 21px + 上下留白 4+4 = 45px，
        /// 再留 3px 容差，故取 48px。</para>
        /// </summary>
        private const float MinBlockHeight = 48f;

        /// <summary>底部页脚区总高（分隔线 + 文字 + 下边距）。</summary>
        private const float FooterZone = 26f;

        /// <summary>进度环描边直径（外缘，设计基准）。</summary>
        private const float DesignRingOuter = 81f;

        /// <summary>环描边宽度（设计基准）。</summary>
        private const float DesignRingThickness = 9f;

        // ============================================================
        // 配色（与 SignalFlowPanel 同族，便于整页视觉统一）
        // ============================================================

        private static readonly Color CardFill = Color.FromArgb(250, 250, 251);
        private static readonly Color CardBorder = Color.FromArgb(214, 214, 220);
        private static readonly Color BlockFill = Color.FromArgb(243, 243, 246);
        private static readonly Color DividerColor = Color.FromArgb(226, 226, 232);

        private static readonly Color HeaderTextColor = Color.FromArgb(45, 45, 52);
        private static readonly Color CaptionTextColor = Color.FromArgb(122, 122, 132);
        private static readonly Color MutedTextColor = Color.FromArgb(150, 150, 158);

        /// <summary>进度弧主色（品牌绿）。</summary>
        private static readonly Color ProgressColor = Color.FromArgb(0, 150, 80);

        /// <summary>进度环底槽色。</summary>
        private static readonly Color TrackColor = Color.FromArgb(214, 238, 224);

        /// <summary>失败区间弧色（红）。</summary>
        private static readonly Color FailArcColor = Color.FromArgb(197, 48, 38);

        private static readonly Color PassTextColor = Color.FromArgb(0, 138, 68);
        private static readonly Color FailTextColor = Color.FromArgb(197, 48, 38);
        private static readonly Color WarnTextColor = Color.FromArgb(191, 130, 0);

        private static readonly Color BadgeFill = Color.FromArgb(226, 246, 235);
        private static readonly Color BadgeIdleFill = Color.FromArgb(242, 242, 245);
        private static readonly Color BadgeWarnFill = Color.FromArgb(253, 240, 222);
        private static readonly Color BadgeDot = Color.FromArgb(0, 150, 80);
        private static readonly Color BadgeDotIdle = Color.FromArgb(160, 160, 168);
        private static readonly Color BadgeDotWarn = Color.FromArgb(219, 130, 0);

        // ============================================================
        // 字体
        // ============================================================

        private readonly Font _titleFont = new Font("微软雅黑", 10.5F, FontStyle.Bold);
        private readonly Font _bodyFont = new Font("微软雅黑", 9F);

        /// <summary>
        /// 环内大号百分比字体。16pt 下"100%"宽约 47px，仍小于环内圈净空 63px。
        /// </summary>
        private readonly Font _metricFont = new Font("Consolas", 16F, FontStyle.Bold);

        private readonly Font _badgeFont = new Font("微软雅黑", 8.5F);

        // ============================================================
        // 运行状态
        // ============================================================

        /// <summary>控件显示状态。</summary>
        public enum ProgressState
        {
            /// <summary>尚未加载测试项。</summary>
            Idle = 0,

            /// <summary>一键运行中。</summary>
            Running = 1,

            /// <summary>一键运行正常完成。</summary>
            Finished = 2,

            /// <summary>一键运行被用户中止.</summary>
            Aborted = 3
        }

        private ProgressState _state = ProgressState.Idle;
        private int _totalCount;
        private int _completedCount;
        private int _passedCount;
        private int _failedCount;
        private TimeSpan _elapsed;
        private string _currentItem = string.Empty;
        private string _idleHint = "未加载测试项";

        /// <summary>呼吸动画相位（弧度）。</summary>
        private float _breathPhase;

        private readonly System.Windows.Forms.Timer _animationTimer;
        private Color _effectiveBackColor = Color.White;

        public TestProgressPanel()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw,
                true);
            // 不透明底色，与父容器（ReceiverControlPanel，已设为白色）保持一致。
            // 原因同 SignalFlowPanel：TabPage 在 UseVisualStyleBackColor = true 时
            // 使用视觉样式渲染，不支持子控件透明背景，否则会渲染为纯黑。
            BackColor = Color.White;
            // 自绘控件不参与 Tab 导航，避免抢占界面键盘焦点
            TabStop = false;

            _animationTimer = new System.Windows.Forms.Timer { Interval = 120 };
            _animationTimer.Tick += OnAnimationTick;
        }

        // ============================================================
        // 外部推送接口
        // ============================================================

        /// <summary>
        /// 推入一次进度快照。
        /// <para>由 <c>AutoTestPanel</c> 在 <c>UpdateProgress</c> 与判重处调用；
        /// 本方法已在 UI 线程上（<c>UpdateProgress</c> 内部已做 BeginInvoke 切换），
        /// 无需再处理跨线程。</para>
        /// </summary>
        /// <param name="state">显示状态。</param>
        /// <param name="total">总项数。</param>
        /// <param name="completed">已测完项数。</param>
        /// <param name="passed">通过数。</param>
        /// <param name="failed">失败数。</param>
        /// <param name="elapsed">本轮耗时。</param>
        /// <param name="currentItem">当前测试项描述（可为空）。</param>
        public void PushProgress(
            ProgressState state,
            int total,
            int completed,
            int passed,
            int failed,
            TimeSpan elapsed,
            string currentItem)
        {
            _state = state;
            _totalCount = Math.Max(0, total);
            _completedCount = Math.Max(0, completed);
            _passedCount = Math.Max(0, passed);
            _failedCount = Math.Max(0, failed);
            _elapsed = elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
            _currentItem = currentItem ?? string.Empty;

            if (state == ProgressState.Running)
            {
                if (!_animationTimer.Enabled && !IsDisposed && IsHandleCreated)
                {
                    _animationTimer.Start();
                }
            }
            else
            {
                _animationTimer.Stop();
            }

            Invalidate();
        }

        /// <summary>
        /// 推入"已加载但未运行"的状态：显示总项数，进度环保持空槽。
        /// </summary>
        public void PushLoaded(int total)
        {
            PushProgress(ProgressState.Idle, total, 0, 0, 0, TimeSpan.Zero, string.Empty);
        }

        /// <summary>空闲态提示文案（如"资源未就绪：频谱仪未连接"）。</summary>
        public void PushIdleHint(string hint)
        {
            _idleHint = string.IsNullOrEmpty(hint) ? "尚未加载测试项" : hint;
            Invalidate();
        }

        // ============================================================
        // 绘制
        // ============================================================

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

            // 显式铺底：确保重绘动画中即使帧不完整，底色也始终正确
            using (SolidBrush brush = new SolidBrush(_effectiveBackColor))
            {
                g.FillRectangle(brush, ClientRectangle);
            }

            // 整块卡面（浅底 + 细边），弱于上方两个 GroupBox，不与主区域争视觉
            using (SolidBrush fill = new SolidBrush(CardFill))
            {
                g.FillRectangle(fill, 0, 0, Width, Height);
            }
            using (Pen pen = new Pen(CardBorder))
            {
                g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }

            if (_state == ProgressState.Idle && _totalCount == 0)
            {
                DrawIdleState(g);
            }
            else
            {
                DrawHeader(g);
                DrawRing(g);
                DrawBlocks(g);
                DrawFooter(g);
            }

            base.OnPaint(e);
        }

        /// <summary>空闲态：整块退为弱提示，不显示 0%。</summary>
        private void DrawIdleState(Graphics g)
        {
            string sub = _totalCount > 0
                ? "共 " + _totalCount + " 个测试项，等待开始"
            : "加载后显示测试进度";

            using (SolidBrush brush = new SolidBrush(CaptionTextColor))
            {
                g.DrawString(_idleHint, _bodyFont, brush, 16, Height / 2f - 26);
                g.DrawString(sub, _bodyFont, brush, 16, Height / 2f - 2);
            }
        }

        /// <summary>标题 + 右上角状态徽章。</summary>
        private void DrawHeader(Graphics g)
        {
            using (SolidBrush brush = new SolidBrush(HeaderTextColor))
            {
                g.DrawString("测试进度", _titleFont, brush, 14, TitleTop);
            }

            // 徽章：宽 72、高 20，与标题同一行（垂直居中对齐）
            const float badgeWidth = 72f;
            const float badgeHeight = 20f;
            float badgeX = Width - badgeWidth - 14f;
            float badgeY = TitleTop + (19f - badgeHeight) / 2f - 1f;

            bool warn = _state == ProgressState.Finished && _failedCount > 0;
            Color badgeBg = warn ? BadgeWarnFill
                : (_state == ProgressState.Idle ? BadgeIdleFill : BadgeFill);
            Color dotColor = warn ? BadgeDotWarn
                : (_state == ProgressState.Idle ? BadgeDotIdle : BadgeDot);
            Color textColor = warn ? WarnTextColor
                : (_state == ProgressState.Idle ? MutedTextColor : PassTextColor);

            string badgeText;
            switch (_state)
            {
                case ProgressState.Running:
                    badgeText = "运行中";
                    break;
                case ProgressState.Finished:
                    badgeText = "完成";
                    break;
                case ProgressState.Aborted:
                    badgeText = "已中止";
                    break;
                default:
                    badgeText = "待运行";
                    break;
            }

            using (GraphicsPath path = CreateRoundedRect(badgeX, badgeY, badgeWidth, badgeHeight, 10f))
            using (SolidBrush fill = new SolidBrush(badgeBg))
            {
                g.FillPath(fill, path);
            }

            // 运行态呼吸：点位透明度随相位变化；非运行态恒为满不透明
            float alpha = 1f;
            if (_state == ProgressState.Running)
            {
                alpha = 0.45f + 0.55f * (0.5f + 0.5f * (float)Math.Sin(_breathPhase));
            }
            using (SolidBrush dot = new SolidBrush(Color.FromArgb(
                (int)(255f * alpha), dotColor.R, dotColor.G, dotColor.B)))
            {
                g.FillEllipse(dot, badgeX + 9f, badgeY + 6.5f, 7f, 7f);
            }

            using (SolidBrush brush = new SolidBrush(textColor))
            {
                g.DrawString(badgeText, _badgeFont, brush, badgeX + 21f, badgeY + 2f);
            }
        }

        /// <summary>
        /// 进度环：绿色主弧 + 失败区间红弧 + 底槽。
        /// <para>环自 12 点方向起顺时针。失败段绘制在已测区间之内的末端，
        /// 使"失败集中于哪段进度"一眼可辨。</para>
        /// <para>环用<b>外缘</b>直径定位（而非中心线直径），确保描边不会越出内容区，
        /// 从而与标题行、页脚行保持固定间隙。</para>
        /// </summary>
        private void DrawRing(Graphics g)
        {
            // 内容区可用高度：整高 - 标题 - 内容区顶 - 页脚区
            float contentHeight = Height - BlockTop - FooterZone;
            if (contentHeight <= 0f)
            {
                return;
            }

            // 外缘直径取「设计值」与「内容区高度」的较小者，保证描边完整落在内容区内
            float outer = Math.Min(DesignRingOuter, contentHeight);
            float thickness = Math.Min(DesignRingThickness, outer / 6f);
            // 中心线直径 = 外缘 - 描边宽（DrawArc 的 rect 用中心线）
            float centerLine = outer - thickness;
            // 内圈净空直径 = 中心线 - 描边宽，即文字可用的正方形区域边长
            float innerDiameter = centerLine - thickness;

            float cx = 14f + outer / 2f;
            float cy = BlockTop + contentHeight / 2f;

            RectangleF ringRect = new RectangleF(
                cx - centerLine / 2f, cy - centerLine / 2f, centerLine, centerLine);

            float total = _totalCount > 0 ? _totalCount : 1;
            float completedRatio = Math.Min(1f, (float)_completedCount / total);
            float failedRatio = Math.Min(completedRatio, (float)_failedCount / total);

            // 底槽（整圈）
            using (Pen track = new Pen(TrackColor, thickness))
            {
                track.StartCap = LineCap.Round;
                track.EndCap = LineCap.Round;
                g.DrawArc(track, ringRect, 0, 360f);
            }

            if (completedRatio > 0f)
            {
                using (Pen pen = new Pen(ProgressColor, thickness))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    g.DrawArc(pen, ringRect, -90f, 360f * completedRatio);

                    // 失败段覆盖在主弧已测区间的末端
                    if (_failedCount > 0)
                    {
                        pen.Color = FailArcColor;
                        g.DrawArc(pen, ringRect,
                            -90f + 360f * (completedRatio - failedRatio),
                            360f * failedRatio);
                    }
                }
            }

            // 环内读数：单行大号百分比，精确居中于环心。
            // <para>不使用 DrawString(text, font, brush, x, y) 定位——那个重载的 x/y
            // 是文字外接框左上角，受字体 ascent/descent 影响，数字字形（无降部）
            // 的视觉重心并不在行高正中。这里改为给文字分配一个以环心为中心的
            // 正方形区域，由 StringFormat 做水平和垂直居中，由 GDI+ 按真实字形度量
            // 计算，视觉中心才能精确落在环心。</para>
            // <para>Completed/total 分数已由右侧「总项数 + 通过 + 失败」两块完整表达，
            // 环内不再重复显示，避免小字灰度成为视觉噪音。</para>
            // 2px 下移：Consolas 粗体数字为等高字形，墨迹重心偏上，补偿其视觉偏移。
            DrawCenteredText(g, FormatPercent(completedRatio), _metricFont,
                _failedCount > 0 ? FailArcColor : PassTextColor,
                cx, cy + 2f, innerDiameter);
        }

        /// <summary>
        /// 在以 <paramref name="centerX"/> / <paramref name="centerY"/> 为中心的
        /// 正方形区域内水平+垂直居中绘制一行文本。
        /// </summary>
        private void DrawCenteredText(Graphics g, string text, Font font, Color color,
            float centerX, float centerY, float regionSize)
        {
            float half = regionSize / 2f;
            RectangleF region = new RectangleF(
                centerX - half, centerY - half, regionSize, regionSize);

            using (SolidBrush brush = new SolidBrush(color))
            using (StringFormat format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                FormatFlags = StringFormatFlags.NoWrap,
                Trimming = StringTrimming.None
            })
            {
                g.DrawString(text, font, brush, region, format);
            }
        }

        /// <summary>右侧 2x2 数字块。</summary>
        private void DrawBlocks(Graphics g)
        {
            float left = 100f;
            const float gapX = 6f;
            float blockWidth = (Width - left - 14f - gapX) / 2f;

            if (blockWidth < 60f)
            {
                // 宽度极端压缩：退化为单列 4 行兜底布局
                DrawBlocksSingleColumn(g, left);
                return;
            }

            // 数字块高度固定为设计值：内部标签+数值共需 45px，压缩会导致字形被裁。
// 内容区装不下两行时直接不画（宁可留白也不裁字）。
            float contentHeight = Height - BlockTop - FooterZone;
            if (contentHeight < BlockHeight * 2f + BlockGap)
            {
                return;
            }

            float[] colLefts = { left, left + blockWidth + gapX };

            for (int row = 0; row < 2; row++)
            {
                for (int col = 0; col < 2; col++)
                {
                    int index = row * 2 + col;
                    RectangleF rect = new RectangleF(
                        colLefts[col], BlockTop + row * (BlockHeight + BlockGap),
                        blockWidth, BlockHeight);

                    using (SolidBrush fill = new SolidBrush(BlockFill))
                    {
                        g.FillRectangle(fill, rect);
                    }

                    string label;
                    string value;
                    Color valueColor;
                    switch (index)
                    {
                        case 0:
                            label = "总项数";
                            value = _totalCount.ToString(CultureInfo.InvariantCulture);
                            valueColor = HeaderTextColor;
                            break;
                        case 1:
                            label = "通过";
                            value = _passedCount.ToString(CultureInfo.InvariantCulture);
                            valueColor = _passedCount > 0 ? PassTextColor : MutedTextColor;
                            break;
                        case 2:
                            label = "失败";
                            value = _failedCount.ToString(CultureInfo.InvariantCulture);
                            valueColor = _failedCount > 0 ? FailTextColor : MutedTextColor;
                            break;
                        default:
                            label = "本轮耗时";
                            value = FormatElapsed(_elapsed);
                            valueColor = HeaderTextColor;
                            break;
                    }

                    using (SolidBrush labelBrush = new SolidBrush(CaptionTextColor))
                    {
                        g.DrawString(label, _bodyFont, labelBrush, rect.Left + 10f, rect.Top + 6f);
                    }
                    using (SolidBrush valueBrush = new SolidBrush(valueColor))
                    {
                        g.DrawString(value, _metricFont, valueBrush, rect.Left + 10f, rect.Top + 21f);
                    }
                }
            }
        }

        /// <summary>宽度极端压缩时的单列兜底布局。</summary>
        private void DrawBlocksSingleColumn(Graphics g, float left)
        {
            float blockWidth = Width - left - 14f;
            if (blockWidth <= 0f)
            {
                return;
            }

            float contentHeight = Height - BlockTop - FooterZone;
            int maxRows = (int)Math.Floor((contentHeight + BlockGap) / (BlockHeight + BlockGap));
            if (maxRows <= 0)
            {
                return;
            }

            for (int i = 0; i < 4 && i < maxRows; i++)
            {
                RectangleF rect = new RectangleF(
                    left, BlockTop + i * (BlockHeight + BlockGap), blockWidth, BlockHeight);

                using (SolidBrush fill = new SolidBrush(BlockFill))
                {
                    g.FillRectangle(fill, rect);
                }

                string label;
                string value;
                switch (i)
                {
                    case 0:
                        label = "总项数";
                        value = _totalCount.ToString(CultureInfo.InvariantCulture);
                        break;
                    case 1:
                        label = "通过";
                        value = _passedCount.ToString(CultureInfo.InvariantCulture);
                        break;
                    case 2:
                        label = "失败";
                        value = _failedCount.ToString(CultureInfo.InvariantCulture);
                        break;
                    default:
                        label = "本轮耗时";
                        value = FormatElapsed(_elapsed);
                        break;
                }

                using (SolidBrush labelBrush = new SolidBrush(CaptionTextColor))
                {
                    g.DrawString(label, _bodyFont, labelBrush, rect.Left + 10f, rect.Top + 6f);
                }
                using (SolidBrush valueBrush = new SolidBrush(HeaderTextColor))
                {
                    g.DrawString(value, _metricFont, valueBrush, rect.Left + 10f, rect.Top + 21f);
                }
            }
        }

        /// <summary>底部当前测试项行。</summary>
        private void DrawFooter(Graphics g)
        {
            // 分隔线位于页脚区顶部，其下留出文字行 + 底部余量
            float dividerY = Height - FooterZone;
            if (dividerY <= BlockTop)
            {
                return;
            }

            using (Pen pen = new Pen(DividerColor))
            {
                g.DrawLine(pen, 14f, dividerY, Width - 14f, dividerY);
            }

            // 文字基线：分隔线下 3px 起，9pt 行高 16px，
            // 底部余量 = FooterZone - 3 - 16 = 7px，避免字形下缘被控件边界裁切
            float textTop = dividerY + 3f;

            using (SolidBrush labelBrush = new SolidBrush(CaptionTextColor))
            {
                g.DrawString("当前项", _bodyFont, labelBrush, 14f, textTop);
            }

            if (_currentItem.Length == 0)
            {
                using (SolidBrush brush = new SolidBrush(MutedTextColor))
                {
                    g.DrawString("-", _bodyFont, brush, 66f, textTop);
                }
                return;
            }

            // 超长文本按可用宽度截断，避免越界绘制到卡面外
            string text = _currentItem;
            SizeF size = g.MeasureString(text, _bodyFont);
            float maxWidth = Width - 66f - 14f;
            if (size.Width > maxWidth)
            {
                while (text.Length > 1 && g.MeasureString(text + "..", _bodyFont).Width > maxWidth)
                {
                    text = text.Substring(0, text.Length - 1);
                }
                text = text + "..";
            }

            using (SolidBrush brush = new SolidBrush(HeaderTextColor))
            {
                g.DrawString(text, _bodyFont, brush, 66f, textTop);
            }
        }

        // ============================================================
        // 格式化
        // ============================================================

        /// <summary>进度百分比文本（如 "64%"）。</summary>
        private static string FormatPercent(float ratio)
        {
            int percent = (int)Math.Round(Math.Max(0f, Math.Min(1f, ratio)) * 100f);
            return percent.ToString(CultureInfo.InvariantCulture) + "%";
        }

        /// <summary>耗时文本：不足 1 小时用 mm:ss，否则用 h:mm:ss。</summary>
        private static string FormatElapsed(TimeSpan elapsed)
        {
            if (elapsed.TotalHours >= 1d)
            {
                return string.Format(CultureInfo.InvariantCulture,
                    "{0}:{1:00}:{2:00}", (int)elapsed.TotalHours, elapsed.Minutes, elapsed.Seconds);
            }
            return string.Format(CultureInfo.InvariantCulture,
                "{0:00}:{1:00}", (int)elapsed.TotalMinutes, elapsed.Seconds);
        }

        /// <summary>生成圆角矩形路径。</summary>
        private static GraphicsPath CreateRoundedRect(float x, float y, float width, float height, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float d = radius * 2f;
            if (d > width)
            {
                d = width;
            }
            if (d > height)
            {
                d = height;
            }

            path.AddArc(x, y, d, d, 180, 90);
            path.AddArc(x + width - d, y, d, d, 270, 90);
            path.AddArc(x + width - d, y + height - d, d, d, 0, 90);
            path.AddArc(x, y + height - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        // ============================================================
        // 背景色与动画生命周期
        // ============================================================

        /// <summary>
        /// 取实际绘制底色：直接采用父容器（<c>ReceiverControlPanel</c>）的 <c>BackColor</c>。
        /// 父容器已在 Designer 中显式设为白色，作为整页底色的唯一来源。
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

        /// <summary>重新取色并刷新。仅在背景确实变化时重绘，避免无谓闪烁。</summary>
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

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
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

                _titleFont.Dispose();
                _bodyFont.Dispose();
                _metricFont.Dispose();
                _badgeFont.Dispose();
            }
            base.Dispose(disposing);
        }

        /// <summary>仅在运行态启动呼吸动画，其余状态立即停止。</summary>
        private void SyncAnimationState()
        {
            if (_state == ProgressState.Running && !IsDisposed && IsHandleCreated)
            {
                if (!_animationTimer.Enabled)
                {
                    _animationTimer.Start();
                }
            }
            else if (_animationTimer.Enabled)
            {
                _animationTimer.Stop();
            }
        }

        private void OnAnimationTick(object sender, EventArgs e)
        {
            // 状态可能已切走但计时器尚未停止，兜底再确认一次
            if (_state != ProgressState.Running)
            {
                _animationTimer.Stop();
                return;
            }
            _breathPhase += 0.22f;
            if (_breathPhase > Math.PI * 2f)
            {
                _breathPhase = 0f;
            }
            Invalidate();
        }
    }
}