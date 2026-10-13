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
    /// <item>测试配置区：通道 / 模式 / 温度下拉 + 加载测试项</item>
    /// <item>测试项面板：列出每个测试项 + 单独运行按钮 + 状态显示</item>
    /// <item>进度与日志区：进度条 + 当前步骤 + RichTextBox 日志 + 清除按钮</item>
    /// </list>
    /// <para>控件创建已移至 <c>AutoTestPanel.Designer.cs</c> 的 <c>InitializeComponent()</c>，
    /// 可在 VS 设计器中可视化编辑。</para>
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
        /// AutoTestPanel.cmbChannel 选中变化：单向同步到 ReceiverControlPanel.cmbChannel。
        /// <para>不反向同步（ReceiverControlPanel.cmbChannel 改变时不动 AutoTestPanel）——
        /// ReceiverControlPanel 是"权威数据源"，AutoTestPanel 只是快捷入口。</para>
        /// <para>切通道后**重建测试项清单**：原 GainTest._channel 是构造期锁定的，
        /// 切通道不重建的话 DGV 行的 Tag 仍指向旧 channel 的 test 实例，
        /// 再次运行时仍发旧 channel 命令。重建让 row.Tag 指向新 channel 的 test。</para>
        /// </summary>
        private void OnAutoTestCmbChannelChanged(object sender, EventArgs e)
        {
            // 构造期 InitializeDefaults 内触发时 _receiverPanel 还是 null，安全跳过。
            if (_receiverPanel == null || _receiverPanel.cmbChannel == null) return;

            int newIndex = cmbChannel.SelectedIndex;
            // 已经在同一索引就不重复赋值（避免无谓的二次触发）
            if (_receiverPanel.cmbChannel.SelectedIndex == newIndex) return;

            _receiverPanel.cmbChannel.SelectedIndex = newIndex;
            // 后续 OnChannelSelected 由 ReceiverControlPanel 自己订阅，会自动切矩阵。

            // 通道变了 → 重建测试项清单，让 DGV 行的 row.Tag 指向新 channel 的 test。
            // 仅在资源已就绪（三仪表已绑定）时重建：构造期或切 Tab 期间
            // _instrumentsReady 还是 false，没必要重建。
            if (_instrumentsReady)
            {
                LoadDefaultTestsToGrid();
            }
        }



        /// <summary>
        /// 由 MainForm.OnLoad 调用，把 ReceiverControlPanel 注入本面板，
        /// 共享 _receiver / 三仪表 / _matrix 资源。
        /// <para>若此时 receiver 串口尚未打开，会自动回退到 M6 兼容单点测试。</para>
        /// </summary>
        public void AttachToReceiver(ReceiverControlPanel receiverPanel)
        {
            _receiverPanel = receiverPanel;
        }

        /// <summary>
        /// 由 <see cref="ReceiverControlPanel.OnLoadTestClicked"/> 调用，
        /// 把"已在接收机控制页连好的真机三仪表"推送到本面板的 _rf / _lo / _sa 字段。
        /// <para>推送完成后立即调 <see cref="LoadDefaultTestsToGrid"/> 走 CreateProduction 路径
        /// （receiver.IsOpen=true 且三仪表 IsConnected=true → 加载 60+3 模式测试项）。</para>
        /// <para>调用方负责先确保三仪表 IsConnected + receiver.IsOpen；本方法不做就绪检查。</para>
        /// </summary>
        public void ApplyReceiverResources(
            SignalGenerator rf,
            SignalGeneratorLO lo,
            SpectrumAnalyzer sa)
        {
            if (rf == null) throw new ArgumentNullException(nameof(rf));
            if (lo == null) throw new ArgumentNullException(nameof(lo));
            if (sa == null) throw new ArgumentNullException(nameof(sa));

            // 释放旧实例（避免泄漏真机 VISA 句柄）
            try { _rf?.Dispose(); } catch { }
            try { _lo?.Dispose(); } catch { }
            try { _sa?.Dispose(); } catch { }

            _rf = rf;
            _lo = lo;
            _sa = sa;
            _instrumentsReady = true;
            Logger.Log("[AutoTestPanel] 已绑定真机三仪表（来自 ReceiverControlPanel）。");

            // 资源已就绪——重新走资源分支判断，receiver IsOpen 决定走 CreateProduction 还是 fallback
            LoadDefaultTestsToGrid();
        }

        /// <summary>
        /// 把默认测试项清单加载到 DataGridView。
        /// 由 <see cref="ApplyReceiverResources"/> 在三仪表 Connect() 成功后调用。
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

            // 文本列统一居中对齐（单元格 + 表头）
            var centerTextStyle = new DataGridViewCellStyle
            {
                Alignment = DataGridViewContentAlignment.MiddleCenter
            };
            var centerHeaderStyle = new DataGridViewCellStyle
            {
                Alignment = DataGridViewContentAlignment.MiddleCenter,
                Font = new Font("微软雅黑", 9F, FontStyle.Bold)
            };

            // 序号
            var colIdx = new DataGridViewTextBoxColumn
            {
                Name = "colIndex",
                HeaderText = "#",
                Width = 40,
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = centerTextStyle,
                HeaderCell = { Style = centerHeaderStyle }
            };
            dgvTests.Columns.Add(colIdx);

            // 测试项目
            var colName = new DataGridViewTextBoxColumn
            {
                Name = "colName",
                HeaderText = "测试项目",
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = centerTextStyle,
                HeaderCell = { Style = centerHeaderStyle }
            };
            dgvTests.Columns.Add(colName);

            // 单位
            var colUnit = new DataGridViewTextBoxColumn
            {
                Name = "colUnit",
                HeaderText = "单位",
                Width = 60,
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = centerTextStyle,
                HeaderCell = { Style = centerHeaderStyle }
            };
            dgvTests.Columns.Add(colUnit);

            // 阈值区间
            var colLimit = new DataGridViewTextBoxColumn
            {
                Name = "colLimit",
                HeaderText = "阈值区间",
                Width = 140,
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = centerTextStyle,
                HeaderCell = { Style = centerHeaderStyle }
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
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = centerTextStyle,
                HeaderCell = { Style = centerHeaderStyle }
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
