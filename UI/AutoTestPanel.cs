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
using A205AutoTestSystem.ReceiverControl;

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

        // M6.x：保存 ReceiverControlPanel 引用，便于在 LoadDefaultTestsToGrid
        // 中检测 receiver 是否已连接，决定走 CreateProduction（模式×频点遍历）
        // 还是 CreateDefault（M6 兼容：单点）。
        private ReceiverControlPanel _receiverPanel;

        private readonly List<ITestItem> _tests = new List<ITestItem>();

        // 抑制初始化阶段事件回调（避免在构建控件时立刻触发副作用）
        private bool _initialized;
        private bool _instrumentsReady;

        // 是否正在"一键运行"，用于抑制子项的某些交互（如禁用 grpTestConfig 上的非必要按钮）
        private bool _runAllInProgress;

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
            ConfigureDgvColumns();       // DataGridView 列初始化（运行期构造）
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

                // M6.x：Fake 流程下构造一个 Mock 接收机注入到 ReceiverControlPanel._receiver，
                // 让 LoadDefaultTestsToGrid 的资源分支判断能走通（receiver.IsOpen=true 且 IsMock=true），
                // 从而加载 60 点遍历的 GainTest。
                if (_receiverPanel != null)
                {
                    try { _receiverPanel._receiver?.Close(); } catch { }
                    try { _receiverPanel._receiver?.Dispose(); } catch { }
                    var mockReceiver = new ReceiverSerialPort
                    {
                        IsMock = true,
                        SendIntervalMs = 0,           // Mock 模式不真发，省 sleep
                        InterSequenceIntervalMs = 0
                    };
                    _receiverPanel._receiver = mockReceiver;
                    Logger.Log("[AutoTestPanel] Mock ReceiverSerialPort 已注入（IsMock=true），GainTest 将按 60 点循环跑。");
                }

                // 三仪表就绪后立即把默认测试项清单加载到 DataGridView，
                // 不再需要"加载测试项"按钮。
                LoadDefaultTestsToGrid();

                MessageBox.Show(this,
                    "测试环境初始化完成（FakeInstrument）。\r\n" +
                    $"已默认加载 {_tests.Count} 项测试项，可点击 ▶ 运行或『一键运行』。",
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

            // SpectrumAnalyzer.ReadPeakAmplitude 调用 STAT ON / MAX:PEAK / MAX:PEAK:SEARCH / Y?，
            // 默认响应是空字符串；预先注入一个有效的 dBm 值（-10 dBm，符合 RF @ -20 dBm + Gain ≈ 10dB 的预期）。
            var rfTransport = transportField.GetValue(rf) as FakeVisaInstrument;
            var loTransport = transportField.GetValue(lo) as FakeVisaInstrument;
            var saTransport = transportField.GetValue(sa) as FakeVisaInstrument;

            if (rfTransport != null)
            {
                // 频率相关的查询无需设置（SG 只发命令）
            }
            if (saTransport != null)
            {
                // SpectrumAnalyzer 实际发出的命令是 MARK1（前缀 CALC:MARK1:MAX:PEAK:SEARCH 与 CALC:MARK1:Y?），
                // FakeVisaInstrument.ReadString 以"最后一次 Write 的命令"为字典 key，
                // 这里必须与 SpectrumAnalyzer.MeasureMarkerPeak / ParseMarkerY 发出的命令字串完全一致。
                saTransport.SetResponse(":CALC:MARK1:MAX:PEAK:SEARCH", "");
                // 给 SA 一个"合理"幅度响应：
                //   第一次 ReadPeakAmplitude（GainTest）→ -10 dBm
                //   后续扫描点（BandwidthTest）→ 也用 -10 dBm；简化版
                saTransport.SetResponse(":CALC:MARK1:Y?", "-10.00");
            }
        }

        /// <summary>
        /// 由 MainForm.OnLoad 调用，把 ReceiverControlPanel 注入本面板，
        /// 共享 _receiver / 三仪表 / _matrix 资源。
        /// <para>在 <see cref="OnInitEnvClicked"/> 的 Fake 初始化路径之前调用；
        /// 若此时 receiver 串口尚未打开，会自动回退到 M6 兼容单点测试。</para>
        /// </summary>
        public void AttachToReceiver(ReceiverControlPanel receiverPanel)
        {
            _receiverPanel = receiverPanel;
        }

        /// <summary>
        /// 初始化测试环境完成后，加载默认测试项清单到 DataGridView。
        /// 由 OnInitEnvClicked 在三仪表 Connect() 成功后调用。
        /// <para>资源分支：</para>
        /// <list type="bullet">
        /// <item>若 <see cref="_receiverPanel"/> 已注入且 receiver 串口已 Open，
        ///       走 <see cref="TestCatalog.CreateProduction"/>（模式×频点遍历）。</item>
        /// <item>否则走 <see cref="TestCatalog.CreateDefault"/>（M6 兼容：单点）。</item>
        /// </list>
        /// </summary>
        private void LoadDefaultTestsToGrid()
        {
            _engine.Clear();
            ClearTestRows();

            IReadOnlyList<ITestItem> items;
            int mode = cmbMode.SelectedIndex + 1;  // 0/1/2 → 1/2/3

            // 资源分支：receiver + 三仪表都就绪 → 走全功能；否则单点兼容
            if (_receiverPanel != null
                && _receiverPanel._receiver != null
                && _receiverPanel._receiver.IsOpen
                && _rf != null && _rf.IsConnected
                && _lo != null && _lo.IsConnected
                && _sa != null && _sa.IsConnected)
            {
                int channel = _receiverPanel.SelectedChannel;
                if (channel < 1 || channel > 8) channel = 1;
                items = TestCatalog.CreateProduction(
                    _receiverPanel._receiver, _rf, _lo, _sa, channel);
            }
            else
            {
                items = TestCatalog.CreateDefault(_rf, _lo, _sa, mode);
            }

            foreach (var t in items)
            {
                _tests.Add(t);
                AddTestRowToGrid(t);
            }
            foreach (var t in items)
            {
                _engine.RegisterTest(t);
            }

            // 加载完成后允许一键运行
            btnRunAll.Enabled = true;
            UpdateProgress(0, $"已加载 {_tests.Count} 个测试项。");
        }

        // ============================================================
        // DataGridView 测试项表（替换原"动态测试项行 + 绝对定位"实现）
        // ============================================================

        // 列下标常量（与 ConfigureDgvColumns 中加入顺序保持一致）
        private const int ColIndex = 0;     // #
        private const int ColName = 1;      // 测试项目
        private const int ColUnit = 2;      // 单位
        private const int ColLimit = 3;     // 阈值区间
        private const int ColAction = 4;    // 操作（按钮列）
        private const int ColStatus = 5;    // 状态

        /// <summary>
        /// 配置 DataGridView 列。Designer 中只声明了 dgvTests 实例，
        /// 列与列样式在运行期构造，便于在代码里集中维护。
        /// </summary>
        private void ConfigureDgvColumns()
        {
            dgvTests.Columns.Clear();
            dgvTests.AutoGenerateColumns = false;
            dgvTests.AllowUserToResizeColumns = false;

            // 行高 28px（约可容 9 行可视）
            dgvTests.RowTemplate.Height = 28;

            // 整体表头字体加粗
            dgvTests.ColumnHeadersDefaultCellStyle.Font =
                new Font("微软雅黑", 9F, FontStyle.Bold);
            dgvTests.ColumnHeadersDefaultCellStyle.Alignment =
                DataGridViewContentAlignment.MiddleCenter;

            // 序号
            var colIdx = new DataGridViewTextBoxColumn
            {
                Name = "colIndex",
                HeaderText = "#",
                Width = 40,
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            dgvTests.Columns.Add(colIdx);

            // 测试项目
            var colName = new DataGridViewTextBoxColumn
            {
                Name = "colName",
                HeaderText = "测试项目",
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            dgvTests.Columns.Add(colName);

            // 单位
            var colUnit = new DataGridViewTextBoxColumn
            {
                Name = "colUnit",
                HeaderText = "单位",
                Width = 60,
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            dgvTests.Columns.Add(colUnit);

            // 阈值区间
            var colLimit = new DataGridViewTextBoxColumn
            {
                Name = "colLimit",
                HeaderText = "阈值区间",
                Width = 140,
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            dgvTests.Columns.Add(colLimit);

            // 操作（按钮列）
            var colAction = new DataGridViewButtonColumn
            {
                Name = "colAction",
                HeaderText = "操作",
                Text = "▶ 运行",
                UseColumnTextForButtonValue = true,
                Width = 80,
                FlatStyle = FlatStyle.Standard,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            dgvTests.Columns.Add(colAction);

            // 状态
            var colStatus = new DataGridViewTextBoxColumn
            {
                Name = "colStatus",
                HeaderText = "状态",
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            dgvTests.Columns.Add(colStatus);

            // 列宽策略——剩余空间给项目名和状态列
            dgvTests.Columns[ColName].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            dgvTests.Columns[ColStatus].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            dgvTests.Columns[ColStatus].FillWeight = 150;     // 状态列权重更大
            dgvTests.Columns[ColName].FillWeight = 100;
        }

        /// <summary>
        /// 清理所有测试项行（DGV 行 + _tests 列表）。同时复位一键运行按钮。
        /// </summary>
        private void ClearTestRows()
        {
            dgvTests.Rows.Clear();
            _tests.Clear();
            btnRunAll.Enabled = false;
        }

        /// <summary>
        /// 向 DataGridView 添加一行测试项。
        /// </summary>
        private void AddTestRowToGrid(ITestItem test)
        {
            int rowIndex = dgvTests.Rows.Add(
                (dgvTests.Rows.Count + 1).ToString(CultureInfo.InvariantCulture),  // #
                test.TestName,                                                     // 测试项目
                test.Unit,                                                         // 单位
                FormatLimitText(test),                                             // 阈值区间
                "▶ 运行",                                                          // 操作（按钮文本）
                "就绪"                                                             // 状态
            );
            var row = dgvTests.Rows[rowIndex];
            // 状态列默认灰色
            row.Cells[ColStatus].Style.ForeColor = StatusColorReady;
            // 把 ITestItem 引用挂到 Tag 上，运行按钮事件回调时通过 Tag 取回
            row.Tag = test;
        }

        /// <summary>
        /// 把测试项的阈值区间格式化为 "[下限, 上限]"。
        /// </summary>
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
        /// DataGridView 单元格点击事件（捕获操作列按钮）。
        /// </summary>
        private async void OnDgvTestsCellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            if (e.ColumnIndex != ColAction) return;
            if (_runAllInProgress) return;     // 一键运行期间忽略手动运行

            await RunSingleTestAsync(e.RowIndex);
        }

        /// <summary>
        /// 单条测试项的执行逻辑（共用：手动按行触发 + 一键运行逐条调用）。
        /// <para>入口会修改行内"操作"列与"状态"列文案，运行结束还原。</para>
        /// </summary>
        private async Task RunSingleTestAsync(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= dgvTests.Rows.Count) return;
            var row = dgvTests.Rows[rowIndex];
            var test = row.Tag as ITestItem;
            if (test == null) return;

            var statusCell = row.Cells[ColStatus];
            var actionCell = row.Cells[ColAction];

            actionCell.Value = "运行中…";
            statusCell.Value = "运行中…";
            statusCell.Style.ForeColor = StatusColorRunning;

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
                    statusCell.Value = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0} ({1} {2})",
                        result.Judgment, valStr, result.Unit);
                    statusCell.Style.ForeColor = result.Judgment == "Pass" ? StatusColorPass : StatusColorFail;

                    // M7：测试项 ExecuteAsync 内部已经 Record 到 TestReportManager；
                    // 这里只要 PendingRowCount > 0 就允许导出。
                    EnableExportButtonIfNeeded();
                }
                else
                {
                    statusCell.Value = "异常：结果为 null";
                    statusCell.Style.ForeColor = StatusColorFail;
                }
            }
            catch (Exception ex)
            {
                statusCell.Value = "异常: " + ex.Message;
                statusCell.Style.ForeColor = StatusColorFail;
            }
            finally
            {
                actionCell.Value = "▶ 运行";
            }
        }

        /// <summary>
        /// "一键运行"按钮：依次执行所有测试项，进度条按总项数推进。
        /// </summary>
        private async void OnRunAllClicked(object sender, EventArgs e)
        {
            if (_runAllInProgress) return;
            if (dgvTests.Rows.Count == 0) return;

            _runAllInProgress = true;
            btnRunAll.Enabled = false;
            btnInitEnv.Enabled = false;

            int total = dgvTests.Rows.Count;
            try
            {
                UpdateProgress(0, $"一键运行：共 {total} 项");
                for (int i = 0; i < total; i++)
                {
                    UpdateProgress(
                        (int)Math.Round(100.0 * i / total),
                        $"一键运行 {i + 1}/{total}");
                    await RunSingleTestAsync(i);
                }
                UpdateProgress(100, $"一键运行完成：{total}/{total}");
            }
            finally
            {
                _runAllInProgress = false;
                btnRunAll.Enabled = true;
                btnInitEnv.Enabled = true;
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
