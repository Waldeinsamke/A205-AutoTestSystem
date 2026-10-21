using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Forms;
using A205AutoTestSystem.Common;
using A205AutoTestSystem.InstrumentControl;
using A205AutoTestSystem.InstrumentControl.TestItems;

namespace A205AutoTestSystem.UI
{
    /// <summary>
    /// 带内虚假信号 —— 手动选频点窗体。
    /// <para><b>为什么需要它：</b>带内虚假信号测试要求在 RF 信号源输出端<b>人工装低通滤波器（LPF）</b>，
    /// 每个频点对应的滤波器不同，因此换频点必须"停下 → 人工换滤波器 → 再继续"，
    /// 无法做成全自动连续测试。本窗体就是这条人工干预的入口。</para>
    /// <para><b>交互流程：</b></para>
    /// <list type="number">
    ///   <item>点击『带内虚假信号』行的 ▶ 运行 → 弹出本窗体（模态，锁住主窗体）</item>
    ///   <item>操作者装好对应频点的低通滤波器 → 点击该频点按钮 → 窗体隐藏（不关闭），
    ///         测试开始跑这一个频点（约 10 s）</item>
    ///   <item>该频点跑完 → 窗体重新弹出，对应按钮下方显示该频点结果（Pass 绿 / Fail 红），
    ///         操作者换下一个滤波器</item>
    ///   <item>9 个频点全部点完 → 窗体自动关闭，本次运行结束，报表一次性拿到 9 条结果</item>
    /// </list>
    /// <para><b>关闭窗体 / 点『取消测试』= 放弃剩余频点</b>，已测完的频点结果仍保留在报表中。</para>
    /// <para>窗体为模态（<see cref="FormDialog"/> + <see cref="FormBorderStyle.FixedDialog"/>），
    /// 避免操作者在测试跑动期间去改仪表设置。所有控件在代码中创建，不走 Designer。</para>
    /// <para><b>线程约束：</b>本窗体所有方法只能在 UI 线程调用。
    /// 测试项侧不使用 <c>ConfigureAwait(false)</c>，保证调用方始终在 UI 线程。</para>
    /// </summary>
    public sealed class InBandSpuriousFreqForm : Form
    {
        // ============================================================
        // 常量
        // ============================================================

        /// <summary>总频点数。必须与 <see cref="InBandSpuriousTest.DefaultTestFreqsMHz"/> 数量一致。</summary>
        private const int TotalFreqCount = 9;

        // ============================================================
        // 字段
        // ============================================================

        /// <summary>9 个频点的按钮，key = 频率 MHz。</summary>
        private readonly Dictionary<double, Button> _freqButtons =
            new Dictionary<double, Button>();

        /// <summary>结果标签，key = 频率 MHz，显示该频点实测值。</summary>
        private readonly Dictionary<double, Label> _resultLabels =
            new Dictionary<double, Label>();

        /// <summary>
        /// 当前这一轮"等操作者点频点"的完成源。按钮点击时 TrySetResult，
        /// 测试项侧 await 它拿到频率。
        /// </summary>
        private TaskCompletionSource<double> _selectionTcs;

        /// <summary>本次运行是否已取消（点取消 / 关窗 / 全部测完）。</summary>
        private bool _cancelled;

        /// <summary>本次运行已完成的频点数。</summary>
        private int _completedCount;

        /// <summary>窗体 owner，用于居中显示。由 <c>AutoTestPanel</c> 注入。</summary>
        public IWin32Window OwnerForm { get; set; }

        private readonly Label _lblPrompt;
        private readonly Label _lblStatus;
        private readonly Button _btnCancel;

        private static readonly Color ResultPassColor = Color.LimeGreen;
        private static readonly Color ResultFailColor = Color.Red;
        private static readonly Color ResultPendingColor = Color.Gray;

        // ============================================================
        // 构造
        // ============================================================

        public InBandSpuriousFreqForm()
        {
            Text = "带内虚假信号 —— 请选择测试频点";
            // 非模态窗体 + ShowInTaskbar=false → 任务栏没有按钮，切到别的程序就找不回来。
            // TopMost 保证它浮在最上层（不影响拖动，仍可自由移到不挡主窗体的位置）；
            // 初始位置由 PositionBesideOwner() 贴到主窗体旁边。
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(560, 470);
            Font = new Font("微软雅黑", 9F);

            var lblTitle = new Label
            {
                Text = "带内虚假信号测试（Mode3，9 个频点）",
                Font = new Font("微软雅黑", 11F, FontStyle.Bold),
                AutoSize = false,
                Bounds = new Rectangle(16, 14, 520, 24)
            };
            Controls.Add(lblTitle);

            _lblPrompt = new Label
            {
                ForeColor = Color.FromArgb(60, 60, 60),
                AutoSize = false,
                Bounds = new Rectangle(16, 42, 528, 90)
            };
            Controls.Add(_lblPrompt);

            // ---- 9 个频点按钮：3 行 × 3 列 ----
            const int btnW = 160;
            const int btnH = 56;
            const int gapX = 20;
            const int gapY = 26;
            const int startX = 22;
            const int startY = 140;

            IReadOnlyList<double> freqs = InBandSpuriousTest.DefaultTestFreqsMHz;
            for (int i = 0; i < freqs.Count; i++)
            {
                double freqMHz = freqs[i];
                int col = i % 3;
                int row = i / 3;

                var btn = new Button
                {
                    Text = freqMHz.ToString("F0", CultureInfo.InvariantCulture) + " MHz",
                    Bounds = new Rectangle(startX + col * (btnW + gapX), startY + row * (btnH + gapY), btnW, btnH),
                    Font = new Font("微软雅黑", 11F, FontStyle.Bold),
                    Tag = freqMHz,
                    BackColor = Color.White,
                    UseVisualStyleBackColor = false
                };
                btn.Click += OnFreqButtonClicked;
                Controls.Add(btn);
                _freqButtons[freqMHz] = btn;

                // 按钮下方的小标签：显示该频点实测结果
                var lblResult = new Label
                {
                    Text = "未测",
                    Font = new Font("微软雅黑", 8.5F),
                    ForeColor = ResultPendingColor,
                    TextAlign = ContentAlignment.MiddleCenter,
                    AutoSize = false,
                    Bounds = new Rectangle(
                        startX + col * (btnW + gapX), startY + row * (btnH + gapY) + btnH + 2,
                        btnW, 18)
                };
                Controls.Add(lblResult);
                _resultLabels[freqMHz] = lblResult;
            }

            // ---- 底部：状态 + 按钮 ----
            _lblStatus = new Label
            {
                Text = string.Format(
                    CultureInfo.InvariantCulture,
                    "已完成 0/{0} 个频点。", TotalFreqCount),
                AutoSize = false,
                Bounds = new Rectangle(16, 398, 528, 22),
                ForeColor = Color.FromArgb(60, 60, 60)
            };
            Controls.Add(_lblStatus);

            _btnCancel = new Button
            {
                Text = "取消测试",
                Bounds = new Rectangle(360, 424, 172, 32),
                Font = new Font("微软雅黑", 9.5F)
            };
            _btnCancel.Click += OnCancelClicked;
            Controls.Add(_btnCancel);

            // ESC 不直接关窗：取消必须走 btnCancel，保证 _cancelled 与 _selectionTcs 一致被处理
            AcceptButton = null;
            CancelButton = null;

            UpdatePrompt();
        }

        // ============================================================
        // 对外接口（供 InBandSpuriousTest 回调，全部在 UI 线程调用）
        // ============================================================

        /// <summary>
        /// 等待操作者点击某个频点按钮，返回被选中的频率（MHz）。
        /// <para><b>窗体是非模态且常驻显示的</b>（<see cref="ShowInTaskbar"/> = false、首次
        /// <c>Show()</c> 后不再隐藏），本方法只负责"挂一个完成源 + await"，
        /// 不做 <c>ShowDialog</c>：WinForms 禁止对已可见窗体再次 <c>ShowDialog</c>
        /// （抛"已经可见的窗体不能显示为模式对话框"），且 ShowDialog 的 Hide/Close
        /// 语义会让第二轮空返回。改用非模态后不存在这两个问题。</para>
        /// <para><b>为什么非模态也能收到按钮点击：</b>测试项内部全是 <c>await Task.Delay(...)</c>，
        /// 没有阻塞线程，WinForms 消息循环持续泵消息，因此窗体按钮事件照常触发。</para>
        /// </summary>
        /// <exception cref="OperationCanceledException">操作者取消或剩余频点已全部测完。</exception>
        public async Task<double> SelectFrequencyAsync()
        {
            if (_cancelled || _completedCount >= TotalFreqCount)
            {
                throw new OperationCanceledException("操作者已取消带内虚假信号测试（或全部频点已测完）。");
            }

            // 首次进入时显示窗体（非模态），之后一直保持可见
            if (!Visible && !IsDisposed)
            {
                PositionBesideOwner();
                Show();
            }

            // 抢回焦点与前台：操作者可能中途切到别的程序，
            // 每轮开始都把本窗体拉到前台，避免"找不到窗体"。
            BringToFront();
            Activate();
            UpdatePrompt();

            // 复位上一轮遗留的禁用状态，让按钮重新可点
            foreach (var b in _freqButtons.Values) b.Enabled = true;
            _btnCancel.Enabled = true;

            _selectionTcs = new TaskCompletionSource<double>();

            try
            {
                return await _selectionTcs.Task;
            }
            finally
            {
                _selectionTcs = null;
            }
        }

        /// <summary>
        /// 测试项测完一个频点后回调本方法，更新该频点的结果标签 / 按钮配色 / 完成计数。
        /// <para>若这是最后一个频点，自动关窗（让 <see cref="SelectFrequencyAsync"/> 之前的等待方返回）。</para>
        /// </summary>
        public void NotifyFreqCompleted(double freqMHz, TestResult result)
        {
            if (IsDisposed) return;

            if (_resultLabels.TryGetValue(freqMHz, out var lbl))
            {
                if (result == null || double.IsNaN(result.Value))
                {
                    lbl.Text = "N/A";
                    lbl.ForeColor = ResultFailColor;
                }
                else
                {
                    lbl.Text = string.Format(
                        CultureInfo.InvariantCulture, "{0:F2} dBm  {1}", result.Value, result.Judgment);
                    lbl.ForeColor = result.Judgment == "Pass" ? ResultPassColor : ResultFailColor;
                }
            }

            bool pass = result != null && result.Judgment == "Pass";
            if (_freqButtons.TryGetValue(freqMHz, out var btn))
            {
                btn.BackColor = pass
                    ? Color.FromArgb(220, 245, 220)
                    : Color.FromArgb(250, 225, 225);
            }

            _completedCount++;
            _lblStatus.Text = string.Format(
                CultureInfo.InvariantCulture,
                "已完成 {0}/{1} 个频点。{2}",
                _completedCount, TotalFreqCount,
                _completedCount >= TotalFreqCount
                    ? "全部频点测完，正在关闭…"
                    : "请更换低通滤波器后，点击下一个频点。");

            Logger.Log(string.Format(
                CultureInfo.InvariantCulture,
                "[InBandSpuriousFreqForm] {0:F0} MHz 完成：{1}（{2}/{3}）",
                freqMHz,
                result == null ? "N/A" : result.Judgment,
                _completedCount, TotalFreqCount));

            if (_completedCount >= TotalFreqCount)
            {
                // 全部测完：置取消标志并关窗，让测试项的循环自然结束
                _cancelled = true;
                Close();
            }
        }

        // ============================================================
        // 事件
        // ============================================================

        private void OnFreqButtonClicked(object sender, EventArgs e)
        {
            if (!(sender is Button btn)) return;

            // 没有待完成的完成源 = 当前没有"等选频点"的轮次（测试跑动中 / 已结束）
            TaskCompletionSource<double> tcs = _selectionTcs;
            if (tcs == null)
            {
                MessageBox.Show(this,
                    "上一个频点还没测完，请稍候。",
                    "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            double freqMHz = (double)btn.Tag;
            _lblPrompt.Text = string.Format(
                CultureInfo.InvariantCulture,
                "正在测试 {0:F0} MHz…请保持滤波器不动，等待本窗口提示换滤波器。", freqMHz);

            // 禁用所有按钮与取消键，防止测试跑动期间误点（窗体本身保持显示）
            foreach (var b in _freqButtons.Values) b.Enabled = false;
            _btnCancel.Enabled = false;

            // 交回频率给测试项（不 Hide 窗体）
            tcs.TrySetResult(freqMHz);
        }

        private void OnCancelClicked(object sender, EventArgs e)
        {
            _cancelled = true;
            // 若测试项正等着选频点，让它以取消语义退出
            _selectionTcs?.TrySetCanceled();
            Close();
        }

        /// <summary>标题栏 X / Esc：与"取消测试"同语义。</summary>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _cancelled = true;
            _selectionTcs?.TrySetCanceled();
            // 撤掉置顶，避免本窗体关闭后主窗体仍被"永远置顶"影响
            TopMost = false;
            base.OnFormClosing(e);
        }

        /// <summary>
        /// 把窗体摆到主窗体右侧（贴边不重叠），屏幕放不下时退回主窗体居中。
        /// <para>只在首次显示前调用一次；之后操作者可自由拖动，本方法不再干预。</para>
        /// <para>用 <c>Screen.WorkingArea</c> 而非 <c>Owner.Bounds</c>：
        /// 主窗体可能被最小化或部分移出屏幕，按 owner 坐标算会把窗体放到屏幕外。</para>
        /// </summary>
        private void PositionBesideOwner()
        {
            const int gap = 12;

            Rectangle area = Screen.PrimaryScreen.WorkingArea;

            Form owner = OwnerForm as Form;
            int x;
            if (owner != null)
            {
                x = owner.Right + gap;
                // 主窗体右侧放不下时，改为盖在主窗体右侧一半的位置（仍保证完全在屏幕内）
                if (x + Width > area.Right)
                {
                    x = owner.Right - Width / 2;
                }
            }
            else
            {
                x = area.Right - Width - gap;
            }

            // 任何分支都要夹回屏幕内
            x = Math.Max(area.Left, Math.Min(x, area.Right - Width));

            int y = area.Top + gap;

            Location = new Point(x, y);
        }

        /// <summary>刷新顶部提示，告知当前是第几个频点、该怎么操作。</summary>
        private void UpdatePrompt()
        {
            _lblPrompt.Text = string.Format(
                CultureInfo.InvariantCulture,
                "第 {0} / {1} 个频点\r\n" +
                "    1) 在 RF 信号源输出端装上本频点对应的低通滤波器；\r\n" +
                "    2) 确认滤波器接好后，点击下方对应频点按钮开始测试；\r\n" +
                "    3) 每个频点测完（约 10 秒），请更换低通滤波器端口后点击下一频点。",
                _completedCount + 1, TotalFreqCount);
        }
    }
}