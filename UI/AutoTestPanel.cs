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
    /// <para>Fake 注入策略（M6 阶段）：</para>
    /// <list type="bullet">
    /// <item>"初始化测试环境"按钮按下时，重置 <c>VisaBaseInstrument.DefaultTransportFactory</c> 为
    ///       <c>addr => new FakeVisaInstrument(addr)</c>，使生产类（SignalGenerator / SpectrumAnalyzer 等）
    ///       直接通过工厂获得伪传输，避免反射注入。</item>
    /// <item>用户切到真机阶段时，应使用原始 <c>NationalInstrumentsVisaTransport</c> 工厂；
    ///       M6 阶段 UI 仅在演示路径上覆盖，不持久化。</item>
    /// </list>
    /// </summary>
    public class AutoTestPanel : UserControl
    {
        // ============================================================
        // 控件字段（按区域分组）
        // ============================================================

        // ---- 测试配置区
        private ComboBox _cmbChannel;
        private ComboBox _cmbMode;
        private ComboBox _cmbTemp;
        private Button _btnInitEnv;
        private Button _btnLoadTests;
        private Button _btnExportExcel;

        // ---- 测试项面板
        private TableLayoutPanel _pnlTestsLayout;

        // ---- 进度与日志区
        private ProgressBar _progressBar;
        private Label _lblCurrentStep;
        private RichTextBox _rtbLog;
        private Button _btnClearLog;

        // ============================================================
        // 业务对象
        // ============================================================
        private SignalGenerator _rf;
        private SignalGeneratorLO _lo;
        private SpectrumAnalyzer _sa;
        private readonly AutoTestEngine _engine = new AutoTestEngine();

        private readonly List<ITestItem> _tests = new List<ITestItem>();
        private readonly List<Button> _runButtons = new List<Button>();
        private readonly List<Label> _statusLabels = new List<Label>();

        // 抑制初始化阶段事件回调（避免在构建控件时立刻触发副作用）
        private bool _initialized;
        private bool _instrumentsReady;

        private static readonly Color StatusColorReady = Color.Gray;
        private static readonly Color StatusColorRunning = Color.DodgerBlue;
        private static readonly Color StatusColorPass = Color.LimeGreen;
        private static readonly Color StatusColorFail = Color.Red;

        // ============================================================
        // 构造与生命周期
        // ============================================================

        public AutoTestPanel()
        {
            Dock = DockStyle.Fill;
            AutoScaleMode = AutoScaleMode.Font;
            BuildUi();
            InitializeDefaults();
            WireEvents();
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
            }
            base.Dispose(disposing);
        }

        // ============================================================
        // UI 构建
        // ============================================================

        private void BuildUi()
        {
            // 顶层布局：3 行 1 列，前两行 AutoSize，最后一行 Fill
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                AutoSize = false,
                Padding = new Padding(8)
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            root.Controls.Add(BuildConfigGroup(),      0, 0);
            root.Controls.Add(BuildTestItemsGroup(),   0, 1);
            root.Controls.Add(BuildProgressLogGroup(), 0, 2);

            Controls.Add(root);
        }

        // ------------------------------------------------------------
        // 测试配置区
        // ------------------------------------------------------------
        private GroupBox BuildConfigGroup()
        {
            var group = new GroupBox
            {
                Text = "测试配置",
                AutoSize = true,
                Dock = DockStyle.Top,
                Padding = new Padding(10, 5, 10, 10)
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 8,
                RowCount = 2,
                AutoSize = true
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            // 第 2 行只放「导出 Excel」按钮，需要一个占位的 AutoSize 行
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // 通道
            _cmbChannel = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Dock = DockStyle.Fill
            };
            for (int ch = 1; ch <= 8; ch++) _cmbChannel.Items.Add("通道" + ch);

            // 模式
            _cmbMode = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Dock = DockStyle.Fill
            };
            _cmbMode.Items.AddRange(new object[] { "Mode1", "Mode2", "Mode3" });

            // 温度
            _cmbTemp = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Dock = DockStyle.Fill
            };
            _cmbTemp.Items.AddRange(new object[] { "常温", "高温", "低温" });

            _btnInitEnv = new Button { Text = "初始化测试环境", Dock = DockStyle.Fill };
            _btnLoadTests = new Button { Text = "加载所有测试项", Dock = DockStyle.Fill };

            layout.Controls.Add(new Label { Text = "通道：", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            layout.Controls.Add(_cmbChannel, 1, 0);
            layout.Controls.Add(new Label { Text = "模式：", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 2, 0);
            layout.Controls.Add(_cmbMode, 3, 0);
            layout.Controls.Add(new Label { Text = "温度：", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 4, 0);
            layout.Controls.Add(_cmbTemp, 5, 0);
            layout.Controls.Add(_btnInitEnv, 6, 0);
            layout.Controls.Add(_btnLoadTests, 7, 0);

            // M7：「导出 Excel」按钮（M7 阶段首版）
            // 默认禁用——只有出现至少一条测试结果后才启用，避免被误点。
            // 横跨第 2 行的全部 8 列。
            _btnExportExcel = new Button
            {
                Text = "导出 Excel（M7）",
                Dock = DockStyle.Fill,
                Enabled = false
            };
            // 通过 SetColumnSpan 让按钮横跨全部 8 列。
            layout.Controls.Add(_btnExportExcel, 0, 1);
            layout.SetColumnSpan(_btnExportExcel, 8);

            group.Controls.Add(layout);
            return group;
        }

        // ------------------------------------------------------------
        // 测试项面板
        // ------------------------------------------------------------
        private GroupBox BuildTestItemsGroup()
        {
            var group = new GroupBox
            {
                Text = "测试项目",
                AutoSize = true,
                Dock = DockStyle.Top,
                Padding = new Padding(10, 5, 10, 10)
            };

            // TableLayoutPanel 5 列：名称 / 操作 / 单位 / 阈值 / 状态
            _pnlTestsLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 5,
                RowCount = 1, // 表头 1 行；加载测试项时动态 AddRow
                AutoSize = true
            };
            _pnlTestsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130F));
            _pnlTestsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110F));
            _pnlTestsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60F));
            _pnlTestsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220F));
            _pnlTestsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _pnlTestsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // 表头
            AddHeaderCell(_pnlTestsLayout, "测试项目", 0);
            AddHeaderCell(_pnlTestsLayout, "操作",     1);
            AddHeaderCell(_pnlTestsLayout, "单位",     2);
            AddHeaderCell(_pnlTestsLayout, "阈值区间", 3);
            AddHeaderCell(_pnlTestsLayout, "状态",     4);

            group.Controls.Add(_pnlTestsLayout);
            return group;
        }

        private static void AddHeaderCell(TableLayoutPanel layout, string text, int col)
        {
            layout.Controls.Add(new Label
            {
                Text = text,
                Dock = DockStyle.Fill,
                Font = new Font("Microsoft YaHei", 9F, FontStyle.Bold)
            }, col, 0);
        }

        // ------------------------------------------------------------
        // 进度与日志区
        // ------------------------------------------------------------
        private GroupBox BuildProgressLogGroup()
        {
            var group = new GroupBox
            {
                Text = "进度与日志",
                Dock = DockStyle.Fill,
                Padding = new Padding(10, 5, 10, 10)
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3
            };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            _progressBar = new ProgressBar
            {
                Dock = DockStyle.Fill,
                Minimum = 0,
                Maximum = 100,
                Value = 0
            };

            _lblCurrentStep = new Label
            {
                Text = "当前步骤：（无）",
                Dock = DockStyle.Fill,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.DarkBlue
            };

            var logInner = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1
            };
            logInner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            logInner.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90F));

            _rtbLog = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                WordWrap = true,
                BackColor = Color.Black,
                ForeColor = Color.LightGreen,
                Font = new Font("Consolas", 9F, FontStyle.Regular),
                DetectUrls = false
            };

            var clearHost = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2
            };
            clearHost.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            clearHost.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            _btnClearLog = new Button { Text = "清除", Dock = DockStyle.Fill };
            clearHost.Controls.Add(new Label(), 0, 0);
            clearHost.Controls.Add(_btnClearLog, 0, 1);

            logInner.Controls.Add(_rtbLog, 0, 0);
            logInner.Controls.Add(clearHost, 1, 0);

            layout.Controls.Add(_progressBar, 0, 0);
            layout.Controls.Add(_lblCurrentStep, 0, 1);
            layout.Controls.Add(logInner, 0, 2);

            group.Controls.Add(layout);
            return group;
        }

        // ============================================================
        // 默认值 & 事件绑定
        // ============================================================

        private void InitializeDefaults()
        {
            _cmbChannel.SelectedIndex = 0;
            _cmbMode.SelectedIndex = 0;
            _cmbTemp.SelectedIndex = 0;
        }

        private void WireEvents()
        {
            _btnInitEnv.Click += OnInitEnvClicked;
            _btnLoadTests.Click += OnLoadTestsClicked;
            _btnExportExcel.Click += OnExportExcelClicked;
            _btnClearLog.Click += (s, e) =>
            {
                if (_rtbLog != null && !_rtbLog.IsDisposed) _rtbLog.Clear();
            };
        }

        // ============================================================
        // 事件处理
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

            // 创建两个测试项（M6 阶段首批）
            var gain = new GainTest(
                rfGenerator: _rf,
                loGenerator: _lo,
                spectrumAnalyzer: _sa,
                rfFreqHz: 100e6,
                rfPowerDbm: -20,
                loFreqHz: 70e6,
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

        /// <summary>
        /// 清理所有测试项行（保留表头 row=0）。
        /// </summary>
        private void ClearTestRows()
        {
            // 清掉表头之后的所有控件
            for (int r = _pnlTestsLayout.RowCount - 1; r >= 1; r--)
            {
                _pnlTestsLayout.RowStyles.RemoveAt(r);
                for (int c = 0; c < _pnlTestsLayout.ColumnCount; c++)
                {
                    var ctrl = _pnlTestsLayout.GetControlFromPosition(c, r);
                    if (ctrl != null)
                    {
                        _pnlTestsLayout.Controls.Remove(ctrl);
                        ctrl.Dispose();
                    }
                }
            }
            _pnlTestsLayout.RowCount = 1;

            _tests.Clear();
            _runButtons.Clear();
            _statusLabels.Clear();
        }

        /// <summary>
        /// 动态新增一行：名称 + 运行按钮 + 单位 + 阈值 + 状态。
        /// </summary>
        private void AddTestRow(ITestItem test)
        {
            int rowIndex = _pnlTestsLayout.RowCount;
            _pnlTestsLayout.RowCount = rowIndex + 1;
            _pnlTestsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            int idx = _tests.IndexOf(test);
            // 注意：IndexOf 应该总是 -1（添加前）；但传 idx 是数据驱动，捕获正确索引
            int actualIdx = rowIndex - 1;

            var lblName = new Label
            {
                Text = test.TestName,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoSize = false
            };

            var btnRun = new Button
            {
                Text = "▶ 运行",
                Dock = DockStyle.Fill
            };
            int captured = actualIdx;
            btnRun.Click += async (s, args) => await OnRunTestClickedAsync(captured);
            _runButtons.Add(btnRun);

            var lblUnit = new Label
            {
                Text = test.Unit,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };

            var lblLimit = new Label
            {
                Text = FormatLimitText(test),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };

            var lblStatus = new Label
            {
                Text = "就绪",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = StatusColorReady,
                Font = new Font(Font, FontStyle.Bold)
            };
            _statusLabels.Add(lblStatus);

            _pnlTestsLayout.Controls.Add(lblName,   0, rowIndex);
            _pnlTestsLayout.Controls.Add(btnRun,    1, rowIndex);
            _pnlTestsLayout.Controls.Add(lblUnit,   2, rowIndex);
            _pnlTestsLayout.Controls.Add(lblLimit,  3, rowIndex);
            _pnlTestsLayout.Controls.Add(lblStatus, 4, rowIndex);
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

                if (_btnExportExcel != null && !_btnExportExcel.IsDisposed)
                {
                    _btnExportExcel.Enabled = false;   // 导出后清空按钮，等待下次结果
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
            if (_btnExportExcel == null || _btnExportExcel.IsDisposed) return;
            if (TestReportManager.Instance.HasPendingResults)
            {
                _btnExportExcel.Enabled = true;
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
            if (_progressBar != null && !_progressBar.IsDisposed)
            {
                _progressBar.Value = Math.Max(0, Math.Min(100, percent));
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
            if (_lblCurrentStep != null && !_lblCurrentStep.IsDisposed && step != null)
            {
                _lblCurrentStep.Text = "当前步骤：" + step;
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
            if (_rtbLog == null || _rtbLog.IsDisposed)
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
            if (_rtbLog.Lines.Length > 500)
            {
                _rtbLog.Clear();
            }
            _rtbLog.AppendText(line + Environment.NewLine);
            _rtbLog.SelectionStart = _rtbLog.Text.Length;
            _rtbLog.ScrollToCaret();
        }
    }
}