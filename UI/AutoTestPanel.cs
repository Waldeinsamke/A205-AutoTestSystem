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

        /// <summary>
        /// Mock 模式标志：由『Mock 加载』按钮置 true；用于无真机连接时预览 DGV 行为。
        /// <para>为 true 时：</para>
        /// <list type="bullet">
        ///   <item><see cref="LoadDefaultTestsToGrid"/> 走 <see cref="TestCatalog.CreateMockItems(TemperatureRange)"/>（纯 Stub，不发任何命令）</item>
        ///   <item>切温度/切通道仍触发 DGV 重建（即使 _instrumentsReady=false）</item>
        ///   <item>点『加载测试』成功后置 false（恢复真资源路径）</item>
        /// </list>
        /// </summary>
        private bool _isMockMode;

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
            // 仅在资源已就绪（三仪表已绑定）或 mock 模式时重建：构造期或切 Tab 期间
            // _instrumentsReady 还是 false，没必要重建。
            if (_instrumentsReady || _isMockMode)
            {
                LoadDefaultTestsToGrid();
            }
        }

        /// <summary>
        /// AutoTestPanel.cmbTemp 选中变化：按"测试温度"分支。
        /// <para>行为（与切通道一致，差别在"切温度会发硬件命令"）：</para>
        /// <list type="number">
        ///   <item>如果接收机已连接且串口已 Open，向接收机发送 <see cref="ReceiverSerialPort.SendTemperatureCommand"/>。</item>
        ///   <item>资源已就绪（三仪表已绑定）时，调 <see cref="LoadDefaultTestsToGrid"/> 重建 DGV。</item>
        /// </list>
        /// <para>cmbTemp 数据源（构造期）："常温" / "高温" / "低温"——索引 0/1/2 对应 <see cref="TemperatureRange.Normal/High/Low"/>。</para>
        /// </summary>
        private void OnAutoTestCmbTempChanged(object sender, EventArgs e)
        {
            // 构造期 InitializeDefaults 内触发时 _receiverPanel / _receiver 都可能为 null，安全跳过。
            if (_receiverPanel == null) return;

            int newIndex = cmbTemp.SelectedIndex;
            if (newIndex < 0) return;

            // 把 cmbTemp 索引映射到 TemperatureRange 枚举（数据源 0/1/2 = Normal/High/Low）。
            TemperatureRange temperature;
            switch (newIndex)
            {
                case 0: temperature = TemperatureRange.Normal; break;
                case 1: temperature = TemperatureRange.High; break;
                case 2: temperature = TemperatureRange.Low; break;
                default: return;
            }

            // 1) 给接收机发温度命令（仅在串口已 Open 时；同通道一样做 null/IsOpen 防护）
            if (_receiverPanel._receiver != null && _receiverPanel._receiver.IsOpen)
            {
                try
                {
                    _receiverPanel._receiver.SendTemperatureCommand(temperature);
                    Logger.Log($"[AutoTestPanel] cmbTemp → {temperature}，已发送接收机温度命令。");
                }
                catch (Exception ex)
                {
                    // 硬件命令失败只记日志，不弹窗——切温度本身不应该被硬件错误阻塞 UI 刷新
                    Logger.Log("[AutoTestPanel] 发送接收机温度命令失败: " + ex.Message);
                }
            }

            // 2) 同步 ReceiverControlPanel 的温度 RadioButton 状态（pnlTempGroup 内的 rbTempNormal/High/Low）。
            //    这些 RadioButton 没有 CheckedChanged 事件订阅（温度命令只在『设置』按钮里发），
            //    所以仅赋值不会重复发命令，也不会触发联动逻辑。
            if (_receiverPanel.RbTempNormal != null
                && _receiverPanel.RbTempHigh != null
                && _receiverPanel.RbTempLow != null)
            {
                bool wantNormal = (temperature == TemperatureRange.Normal);
                bool wantHigh   = (temperature == TemperatureRange.High);
                bool wantLow    = (temperature == TemperatureRange.Low);

                // 防自触发：状态完全相同就不赋值（参考 OnAutoTestCmbChannelChanged 的写法）
                if (_receiverPanel.RbTempNormal.Checked != wantNormal) _receiverPanel.RbTempNormal.Checked = wantNormal;
                if (_receiverPanel.RbTempHigh.Checked   != wantHigh)   _receiverPanel.RbTempHigh.Checked   = wantHigh;
                if (_receiverPanel.RbTempLow.Checked    != wantLow)    _receiverPanel.RbTempLow.Checked    = wantLow;
            }

            // 3) 资源已就绪或 mock 模式时重建 DGV（与切通道一致）
            if (_instrumentsReady || _isMockMode)
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
        /// "加载测试"按钮：从 <see cref="_receiverPanel"/> 拉取已连接的真机三仪表，
        /// 推送到本面板的 _rf / _lo / _sa 字段。
        /// <para>前置条件：</para>
        /// <list type="bullet">
        ///   <item><see cref="_receiverPanel"/> 必须已注入（由 MainForm.OnLoad 注入）</item>
        ///   <item>三仪表（RF/LO/SA）必须已 Connect</item>
        ///   <item>接收机串口必须已 Open</item>
        /// </list>
        /// <para>成功：仅在 <see cref="lblCurrentStep"/> 显示提示；不弹窗。</para>
        /// <para>失败：弹 <see cref="MessageBox"/> 提示具体原因。</para>
        /// </summary>
        private void OnLoadTestClicked(object sender, EventArgs e)
        {
            if (_receiverPanel == null)
            {
                MessageBox.Show(this,
                    "接收机控制面板未注入（MainForm 启动时漏 AttachToReceiver），请联系开发者。",
                    "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // 1) 三仪表就绪检查
            if (!_receiverPanel.AreInstrumentsReady)
            {
                MessageBox.Show(this,
                    "请先在『接收机控制』页一键连好三台仪表（RF/LO/SA 状态灯全绿）。",
                    "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 2) 接收机串口就绪检查
            if (!_receiverPanel.IsReceiverReady)
            {
                MessageBox.Show(this,
                    "请先在『接收机控制』页『串口连接』区连接收机串口。",
                    "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 3) 推送三仪表到本面板
            try
            {
                ApplyReceiverResources(
                    _receiverPanel.RfGenerator,
                    _receiverPanel.LoGenerator,
                    _receiverPanel.SpectrumAnalyzerInstance);

                // 成功：仅状态栏提示，不弹窗。同时清除 mock 模式（恢复真资源路径）。
                _isMockMode = false;
                UpdateStep("已加载真机测试资源（60+3 模式测试项已就绪，可点 ▶ 运行 或 一键运行）");
                Logger.Log("[AutoTestPanel] 加载测试成功。");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "加载测试资源失败：" + ex.Message,
                    "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// "Mock 加载"按钮：跳过三仪表 + 接收机串口的就绪检查，
        /// 直接按当前温度档位加载 <see cref="TestCatalog.CreateMockItems"/> 的纯 Stub 测试项列表。
        /// <para>用途：无真机连接时验证"按温度分测试项 + 切温度重建 DGV"的 UI 行为。
        /// 点"运行"会立即返回"未实现"Fail 结果，不会发任何 SCPI/串口命令。</para>
        /// <para>要点：</para>
        /// <list type="bullet">
        ///   <item>不检查三仪表/串口——故意跳过</item>
        ///   <item>不向接收机发任何命令</item>
        ///   <item>置 <see cref="_isMockMode"/> = true，让切温度/切通道在 mock 下也重建 DGV</item>
        /// </list>
        /// </summary>
        private void OnMockLoadClicked(object sender, EventArgs e)
        {
            _isMockMode = true;
            LoadDefaultTestsToGrid();
            UpdateStep("Mock 模式：已加载按当前温度档位的 Stub 测试项（不发任何命令，点运行返回『未实现』Fail）");
            Logger.Log("[AutoTestPanel] Mock 加载成功，进入 mock 模式。");
        }

        /// <summary>
        /// 把"已在接收机控制页连好的真机三仪表"推送到本面板的 _rf / _lo / _sa 字段。
        /// <para>由 <see cref="OnLoadTestClicked"/> 调用。推送完成后立即调 <see cref="LoadDefaultTestsToGrid"/>
        /// 走 CreateProduction 路径（receiver.IsOpen=true 且三仪表 IsConnected=true → 加载 60+3 模式测试项）。</para>
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
        /// <para>三种分支：</para>
        /// <list type="bullet">
        /// <item>Mock 模式（<see cref="_isMockMode"/>）：走 <see cref="TestCatalog.CreateMockItems(TemperatureRange)"/>，
        ///       纯 Stub 列表，不依赖真资源。</item>
        /// <item>真资源模式（receiver + 三仪表都就绪）：走 <see cref="TestCatalog.CreateProduction(ReceiverSerialPort, SignalGenerator, SignalGeneratorLO, SpectrumAnalyzer, int, TemperatureRange)"/>
        ///       （模式×频点遍历 + 按温度分支）。</item>
        /// <item>否则走 <see cref="TestCatalog.CreateDefault"/>（M6 兼容：单点，不分温度）。</item>
        /// </list>
        /// </summary>
        private void LoadDefaultTestsToGrid()
        {
            _engine.Clear();
            ClearTestRows();

            IReadOnlyList<ITestItem> items;
            int mode = cmbMode.SelectedIndex + 1;  // 0/1/2 → 1/2/3
            // 温度：cmbTemp 索引 0/1/2 → Normal/High/Low；索引无效时退回常温
            TemperatureRange temperature;
            switch (cmbTemp.SelectedIndex)
            {
                case 1: temperature = TemperatureRange.High; break;
                case 2: temperature = TemperatureRange.Low; break;
                default: temperature = TemperatureRange.Normal; break;
            }

            // Mock 模式：跳过资源分支，按当前温度档位加载纯 Stub 列表
            if (_isMockMode)
            {
                items = TestCatalog.CreateMockItems(temperature);
            }
            // 真资源分支：receiver + 三仪表都就绪 → 走全功能；否则单点兼容
            else if (_receiverPanel != null
                && _receiverPanel._receiver != null
                && _receiverPanel._receiver.IsOpen
                && _rf != null && _rf.IsConnected
                && _lo != null && _lo.IsConnected
                && _sa != null && _sa.IsConnected)
            {
                int channel = _receiverPanel.SelectedChannel;
                if (channel < 1 || channel > 8) channel = 1;
                items = TestCatalog.CreateProduction(
                    _receiverPanel._receiver, _rf, _lo, _sa, channel, temperature);
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
