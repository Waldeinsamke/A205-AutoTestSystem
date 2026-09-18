using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using A205AutoTestSystem.Common;
using A205AutoTestSystem.InstrumentControl;
using A205AutoTestSystem.InstrumentControl.TestItems;

namespace A205AutoTestSystem.UI
{
    /// <summary>
    /// 自动测试面板（M6 阶段实现）。
    /// <para>布局（严格按 spec §5.3）：</para>
    /// <list type="number">
    /// <item>测试配置区：通道 / 模式 / 温度下拉 + 初始化测试环境 / 加载测试项</item>
    /// <item>测试项面板：列出每个测试项 + 单独运行按钮 + 状态显示</item>
    /// <item>进度与日志区：进度条 + 当前步骤 + RichTextBox 日志 + 清除按钮</item>
    /// </list>
    /// <para>控件创建已移至 <c>AutoTestPanel.Designer.cs</c> 的 <c>InitializeComponent()</c>，
    /// 可在 VS 设计器中可视化编辑。</para>
    /// <para>Fake 注入策略（M6 阶段）：</para>
    /// <list type="bullet">
    /// <item>"初始化测试环境"按钮按下时，重置 <c>VisaBaseInstrument.DefaultTransportFactory</c> 为
    ///       <c>addr => new FakeVisaInstrument(addr)</c>，使生产类（SignalGenerator / SpectrumAnalyzer 等）
    ///       直接通过工厂获得伪传输，避免反射注入。</item>
    /// <item>用户切到真机阶段时，应使用原始 <c>NationalInstrumentsVisaTransport</c> 工厂；
    ///       M6 阶段 UI 仅在演示路径上覆盖，不持久化。</item>
    /// </list>
    /// </summary>
    public partial class AutoTestPanel : UserControl
    {
        // ============================================================
        // 业务对象（保留 _ 前缀；非控件字段，Designer 不感知）
        // ============================================================
        private SignalGenerator _rf;
        private SignalGeneratorLO _lo;
        private SpectrumAnalyzer _sa;
        private readonly AutoTestEngine _engine = new AutoTestEngine();

        private readonly List<ITestItem> _tests = new List<ITestItem>();
        private readonly List<Button> _runButtons = new List<Button>();
        private readonly List<Label> _statusLabels = new List<Label>();

        // 动态测试项行（绝对定位）：记录 AddTestRow 创建的 5 个控件，便于统一清理
        private readonly List<Control> _dynamicRowControls = new List<Control>();

        // 抑制初始化阶段事件回调（避免在构建控件时立刻触发副作用）
        private bool _initialized;
        private bool _instrumentsReady;

        // 状态颜色常量（不写进 .resx，保持代码可读性）
        private static readonly Color StatusColorReady = Color.Gray;
        private static readonly Color StatusColorRunning = Color.DodgerBlue;
        private static readonly Color StatusColorPass = Color.LimeGreen;
        private static readonly Color StatusColorFail = Color.Red;

        // ============================================================
        // 构造与生命周期
        // ============================================================

        public AutoTestPanel()
        {
            InitializeComponent();        // ← Designer 生成（参见 Designer.cs）
            InitializeDefaults();
            // WireEvents() 不需要：Designer 内已订阅
            _initialized = true;

            // 订阅全局 Logger 与引擎日志
            Logger.MessageLogged += OnLogMessage;
            _engine.LogEmitted += OnEngineLog;
        }

        /// <summary>
        /// 重写 Dispose，避免页面关闭后仍持有事件订阅造成泄漏。
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { Logger.MessageLogged -= OnLogMessage; } catch { }
                try { _engine.LogEmitted -= OnEngineLog; } catch { }

                try { _rf?.Dispose(); } catch { }
                try { _lo?.Dispose(); } catch { }
                try { _sa?.Dispose(); } catch { }
                _rf = null;
                _lo = null;
                _sa = null;

                // Designer 创建的控件容器
                if (components != null)
                {
                    components.Dispose();
                }
            }
            base.Dispose(disposing);
        }

        // ============================================================
        // 默认值（控件字段名已去除 _ 前缀）
        // ============================================================

        private void InitializeDefaults()
        {
            // 通道下拉 8 项（设计器 InitializeComponent 内不允许循环，故在此运行时填充）
            for (int ch = 1; ch <= 8; ch++)
            {
                cmbChannel.Items.Add("通道" + ch);
            }
            cmbChannel.SelectedIndex = 0;
            // cmbMode / cmbTemp.Items 在运行时填充（依需要放在这里）
            cmbMode.Items.AddRange(new object[] { "Mode1", "Mode2", "Mode3" });
            cmbMode.SelectedIndex = 0;
            cmbTemp.Items.AddRange(new object[] { "常温", "高温", "低温" });
            cmbTemp.SelectedIndex = 0;
        }

        // ============================================================
        // 事件处理（Designer 中已统一订阅 this.btnXxx.Click += OnXxxClicked;）
        // ============================================================

        /// <summary>
        /// "初始化测试环境"按钮。
        /// <para>M6 阶段：把 <c>VisaBaseInstrument.DefaultTransportFactory</c> 重置为
        /// <c>addr => new FakeVisaInstrument(addr)</c>，让三仪表在生产路径下走伪传输。
        /// 真机阶段应在 Program.Main 注册 <c>NationalInstrumentsVisaTransport</c>，
        /// 切到真机时不要点击本按钮（否则全局静态工厂被覆盖）。</para>
        /// </summary>
        private void OnInitEnvClicked(object sender, EventArgs e)
        {
            try
            {
                // 释放旧实例
                try { _rf?.Dispose(); } catch { }
                try { _lo?.Dispose(); } catch { }
                try { _sa?.Dispose(); } catch { }

                // M6 阶段：注册 FakeVisaInstrument 工厂
                // 注意：这是全局静态，多窗口/多实例会相互影响——见 review-notes 担忧。
                Func<string, IVisaTransport> fakeFactory = addr => new FakeVisaInstrument(addr);
                VisaBaseInstrument.DefaultTransportFactory = fakeFactory;

                const string rfAddr = "TCPIP0::rf-fake::INSTR";
                const string loAddr = "TCPIP0::lo-fake::INSTR";
                const string saAddr = "TCPIP0::sa-fake::INSTR";

                _rf = new SignalGenerator(rfAddr);
                _lo = new SignalGeneratorLO(loAddr);
                _sa = new SpectrumAnalyzer(saAddr);

                _rf.Connect();
                _lo.Connect();
                _sa.Connect();

                _instrumentsReady = true;

                // 配置伪传输的常见响应（让 ReadPeakAmplitude 返回合理值）
                InjectFakeDefaults(_rf, _lo, _sa);

                MessageBox.Show(this,
                    "测试环境初始化完成（FakeInstrument）。\r\n可点击『加载所有测试项』。",
                    "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                _instrumentsReady = false;
                MessageBox.Show(this, "初始化失败: " + ex.Message, "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// 为 FakeVisaInstrument 注入常用的命令响应，便于 UI 演示有合理的测量值。
        /// </summary>
        private static void InjectFakeDefaults(SignalGenerator rf, SignalGeneratorLO lo, SpectrumAnalyzer sa)
        {
            var transportField = typeof(VisaBaseInstrument).GetField(
                "_transport",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (transportField == null) return;

            // SignalGenerator：只发命令，无需 ReadString
            // SignalGeneratorLO：同样

            // SpectrumAnalyzer.ReadPeakAmplitude 调用 CALC:MARK:MAX + CALC:MARK:Y?
            // 默认响应是空字符串；预先注入一个有效的 dBm 值（-10 dBm，符合 RF @ -20 dBm + Gain ≈ 10dB 的预期）
            var rfTransport = transportField.GetValue(rf) as FakeVisaInstrument;
            var loTransport = transportField.GetValue(lo) as FakeVisaInstrument;
            var saTransport = transportField.GetValue(sa) as FakeVisaInstrument;

            if (rfTransport != null)
            {
                // 频率相关的查询无需设置（SG 只发命令）
            }
            if (saTransport != null)
            {
                saTransport.SetResponse("CALC:MARK:MAX", "");
                // 给 SA 一个"合理"幅度响应：
                //   第一次 ReadPeakAmplitude（GainTest）→ -10 dBm
                //   后续扫描点（BandwidthTest）→ 也用 -10 dBm；简化版
                saTransport.SetResponse("CALC:MARK:Y?", "-10.00");
            }
        }

        /// <summary>
        /// "加载所有测试项"按钮。
        /// </summary>
        private void OnLoadTestsClicked(object sender, EventArgs e)
        {
            if (!_initialized) return;
            if (!_instrumentsReady)
            {
                MessageBox.Show(this, "请先初始化测试环境。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 清空旧测试项
            _engine.Clear();
            ClearTestRows();

            // M6 阶段首批：模式从 UI 当前选择读取（SelectedIndex 0/1/2 → Mode1/2/3）；
            // LO 由 LoFrequencyRule 按（RF、模式）动态计算（1/2 → RF+3670MHz，3 → RF+4350MHz）
            int mode = cmbMode.SelectedIndex + 1;

            // 创建两个测试项（M6 阶段首批）
            var gain = new GainTest(
                rfGenerator: _rf,
                loGenerator: _lo,
                spectrumAnalyzer: _sa,
                rfFreqHz: 100e6,
                rfPowerDbm: -20,
                mode: mode,
                loPowerDbm: 0,
                ifFreqHz: 70e6);

            var bw = new BandwidthTest(
                rfGenerator: _rf,
                spectrumAnalyzer: _sa,
                centerRfFreqHz: 100e6,
                rfPowerDbm: -20,
                ifFreqHz: 70e6,
                scanPoints: 11);

            _engine.RegisterTest(gain);
            _engine.RegisterTest(bw);

            _tests.Add(gain);
            _tests.Add(bw);

            // 动态添加行
            foreach (var t in _tests)
            {
                AddTestRow(t);
            }

            UpdateProgress(0, $"已加载 {_tests.Count} 个测试项。");
            MessageBox.Show(this, $"已加载 {_tests.Count} 个测试项。", "提示",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ============================================================
        // 动态测试项行（绝对定位；列坐标与 Designer 中表头 5 列一一对应）
        // ============================================================

        private const int RowOriginY = 55;          // 第一行顶部 Y
        private const int RowHeight = 34;           // 行高
        private const int DesignItemsGroupHeight = 134;
        private static readonly int[] ColumnX = { 14, 220, 318, 396, 604 };
        private static readonly int[] ColumnWidth = { 200, 90, 70, 200, 400 };
        private static readonly Point DesignProgressLocation = new Point(8, 256);

        /// <summary>
        /// 清理所有测试项行（保留表头），并复位 GroupBox 高度与进度区位置。
        /// </summary>
        private void ClearTestRows()
        {
            foreach (Control c in _dynamicRowControls)
            {
                grpTestItems.Controls.Remove(c);
                c.Dispose();
            }
            _dynamicRowControls.Clear();

            // 复位为设计器中的初始布局
            grpTestItems.Height = DesignItemsGroupHeight;
            grpProgressLog.Location = DesignProgressLocation;

            _tests.Clear();
            _runButtons.Clear();
            _statusLabels.Clear();
        }

        /// <summary>
        /// 动态新增一行：名称 + 运行按钮 + 单位 + 阈值 + 状态（绝对定位）。
        /// </summary>
        private void AddTestRow(ITestItem test)
        {
            // 以已存在的运行按钮数作为 0 起始行号（调用顺序保证与 _tests 一致）
            int rowIndex = _runButtons.Count;
            int y = RowOriginY + rowIndex * RowHeight;

            var lblName = new Label
            {
                Text = test.TestName,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoSize = false,
                Location = new Point(ColumnX[0], y + 3),
                Size = new Size(ColumnWidth[0], 28)
            };

            var btnRun = new Button
            {
                Text = "▶ 运行",
                Location = new Point(ColumnX[1], y + 2),
                Size = new Size(ColumnWidth[1], 28)
            };
            int captured = rowIndex;
            btnRun.Click += async (s, args) => await OnRunTestClickedAsync(captured);
            _runButtons.Add(btnRun);

            var lblUnit = new Label
            {
                Text = test.Unit,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoSize = false,
                Location = new Point(ColumnX[2], y + 3),
                Size = new Size(ColumnWidth[2], 28)
            };

            var lblLimit = new Label
            {
                Text = FormatLimitText(test),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoSize = false,
                Location = new Point(ColumnX[3], y + 3),
                Size = new Size(ColumnWidth[3], 28)
            };

            var lblStatus = new Label
            {
                Text = "就绪",
                TextAlign = ContentAlignment.MiddleLeft,
                AutoSize = false,
                ForeColor = StatusColorReady,
                Font = new Font(Font, FontStyle.Bold),
                Location = new Point(ColumnX[4], y + 3),
                Size = new Size(ColumnWidth[4], 28)
            };
            _statusLabels.Add(lblStatus);

            grpTestItems.Controls.Add(lblName);
            grpTestItems.Controls.Add(btnRun);
            grpTestItems.Controls.Add(lblUnit);
            grpTestItems.Controls.Add(lblLimit);
            grpTestItems.Controls.Add(lblStatus);
            _dynamicRowControls.AddRange(new Control[] { lblName, btnRun, lblUnit, lblLimit, lblStatus });

            // 按行数扩展"测试项目"分组高度，并把"进度与日志"分组整体下移
            int neededHeight = RowOriginY + (rowIndex + 1) * RowHeight + 11;
            if (neededHeight > grpTestItems.Height)
            {
                grpTestItems.Height = neededHeight;
            }
            grpProgressLog.Location = new Point(8, grpTestItems.Bottom + 8);
        }

        private static string FormatLimitText(ITestItem test)
        {
            string lo = test.LowerLimit.HasValue
                ? test.LowerLimit.Value.ToString("F2", CultureInfo.InvariantCulture)
                : "-∞";
            string hi = test.UpperLimit.HasValue
                ? test.UpperLimit.Value.ToString("F2", CultureInfo.InvariantCulture)
                : "+∞";
            return $"[{lo}, {hi}]";
        }

        /// <summary>
        /// 单条测试项的运行按钮回调（异步）。
        /// </summary>
        private async Task OnRunTestClickedAsync(int testIndex)
        {
            if (_tests == null || testIndex >= _tests.Count)
            {
                return;
            }

            var test = _tests[testIndex];
            var btn = _runButtons[testIndex];
            var lbl = _statusLabels[testIndex];

            btn.Enabled = false;
            lbl.Text = "运行中…";
            lbl.ForeColor = StatusColorRunning;

            try
            {
                var progress = new Progress<string>(msg => UpdateStep(msg));
                // M6 阶段固定 CancellationToken.None；M6.x 阶段接入 Cancel 按钮。
                TestResult result = await test.ExecuteAsync(progress, CancellationToken.None)
                                              .ConfigureAwait(true);

                if (result != null)
                {
                    string valStr = double.IsNaN(result.Value) || double.IsInfinity(result.Value)
                        ? "N/A"
                        : result.Value.ToString("F2", CultureInfo.InvariantCulture);
                    lbl.Text = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0} ({1} {2})",
                        result.Judgment, valStr, result.Unit);
                    lbl.ForeColor = result.Judgment == "Pass" ? StatusColorPass : StatusColorFail;

                    // M7：测试项 ExecuteAsync 内部已经 Record 到 TestReportManager；
                    // 这里只要 PendingRowCount > 0 就允许导出。
                    EnableExportButtonIfNeeded();
                }
                else
                {
                    lbl.Text = "异常：结果为 null";
                    lbl.ForeColor = StatusColorFail;
                }
            }
            catch (Exception ex)
            {
                lbl.Text = "异常: " + ex.Message;
                lbl.ForeColor = StatusColorFail;
            }
            finally
            {
                btn.Enabled = true;
            }
        }

        /// <summary>
        /// "导出 Excel" 按钮（m7 阶段新增）。
        /// <para>把当前批次的全部测试结果一次性落盘为
        /// <c>bin\reports\A205_测试报告_yyyyMMdd_HHmmss.xlsx</c>。</para>
        /// <para>若当前没有任何结果，给出空提示。</para>
        /// </summary>
        private void OnExportExcelClicked(object sender, EventArgs e)
        {
            try
            {
                string path = TestReportManager.Instance.SaveCurrentReport();
                if (string.IsNullOrEmpty(path))
                {
                    MessageBox.Show(this,
                        "尚无任何测试结果可导出。\r\n请先在测试项目区点 ▶ 运行测试项。",
                        "导出",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                if (btnExportExcel != null && !btnExportExcel.IsDisposed)
                {
                    btnExportExcel.Enabled = false;   // 导出后清空按钮，等待下次结果
                }

                MessageBox.Show(this,
                    "已导出到：\r\n" + path,
                    "导出成功",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Logger.Log("[Export] 异常：" + ex.Message);
                MessageBox.Show(this,
                    "导出失败：" + ex.Message,
                    "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// 当 <see cref="TestReportManager.PendingRowCount"/> > 0 时启用导出按钮。
        /// </summary>
        private void EnableExportButtonIfNeeded()
        {
            if (btnExportExcel == null || btnExportExcel.IsDisposed) return;
            if (TestReportManager.Instance.HasPendingResults)
            {
                btnExportExcel.Enabled = true;
            }
        }

        /// <summary>
        /// 更新进度条 + 当前步骤。
        /// </summary>
        private void UpdateProgress(int percent, string step)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<int, string>(UpdateProgress), percent, step);
                return;
            }
            if (progressBar != null && !progressBar.IsDisposed)
            {
                progressBar.Value = Math.Max(0, Math.Min(100, percent));
            }
            UpdateStep(step);
        }

        private void UpdateStep(string step)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(UpdateStep), step);
                return;
            }
            if (lblCurrentStep != null && !lblCurrentStep.IsDisposed && step != null)
            {
                lblCurrentStep.Text = "当前步骤：" + step;
            }
        }

        // ============================================================
        // 日志回调
        // ============================================================

        private void OnEngineLog(string line)
        {
            // 引擎日志已经过 Logger.Log 写盘+Console；这里只更新当前步骤
            UpdateStep(line);
        }

        private void OnLogMessage(string line)
        {
            if (rtbLog == null || rtbLog.IsDisposed)
            {
                return;
            }
            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(new Action<string>(OnLogMessage), line);
                }
                catch { /* 控件已销毁 */ }
                return;
            }

            // 行数上限保护，避免长时间运行导致内存膨胀
            if (rtbLog.Lines.Length > 500)
            {
                rtbLog.Clear();
            }
            rtbLog.AppendText(line + Environment.NewLine);
            rtbLog.SelectionStart = rtbLog.Text.Length;
            rtbLog.ScrollToCaret();
        }

        /// <summary>
        /// "清除"按钮：清空通信日志 RichTextBox。
        /// </summary>
        private void OnBtnClearLogClicked(object sender, EventArgs e)
        {
            if (rtbLog != null && !rtbLog.IsDisposed)
            {
                rtbLog.Clear();
            }
        }
    }
}
