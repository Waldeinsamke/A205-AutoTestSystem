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

        // 保存 ReceiverControlPanel 引用，便于在 LoadDefaultTestsToGrid 中检测 receiver 是否已连接，
        // 决定能否走 CreateProduction（模式×频点遍历）——资源不全则不加载测试项。
        private ReceiverControlPanel _receiverPanel;

        private readonly List<ITestItem> _tests = new List<ITestItem>();

        private bool _instrumentsReady;

        // 是否正在"一键运行"，用于抑制子项的某些交互（如禁用 grpTestConfig 上的非必要按钮）
        private bool _runAllInProgress;

        /// <summary>单条运行中的 CancellationTokenSource（用于"■ 停止"按钮取消当前任务）。</summary>
        private CancellationTokenSource _runCts;

        /// <summary>是否有"单条运行"在执行（与 _runAllInProgress 互斥）。</summary>
        private bool _runInProgress;

        /// <summary>当前正在运行的行索引（_runInProgress=true 时有效；否则 -1）。
        /// 用于 OnDgvTestsCellContentClick 区分"点当前行■ 停止"和"点其他行"，避免误取消。</summary>
        private int _runningRowIndex = -1;

        // 状态颜色常量（不写进 .resx，保持代码可读性）
        private static readonly Color StatusColorReady = Color.Gray;
        private static readonly Color StatusColorRunning = Color.DodgerBlue;
        private static readonly Color StatusColorPass = Color.LimeGreen;
        private static readonly Color StatusColorFail = Color.Red;

        // 运行中"其它行按钮 disabled"灰化色（DataGridViewButtonColumn 没有 Enabled 属性，
        // 用 ReadOnly=true + 这三个色模拟"真 disable"的视觉效果）
        private static readonly Color ActionDisabledFore = Color.FromArgb(160, 160, 160);
        private static readonly Color ActionDisabledBack = Color.FromArgb(245, 245, 245);

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

            // 3) 资源已就绪时重建 DGV（与切通道一致）
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

                // 成功：仅状态栏提示，不弹窗。

                // 4) 关闭两台信号源的 RF 输出——确保自动测试开始前频谱仪没有任何显示，
                //    避免之前残留的载波 / LO 漏信号被误判。失败仅记日志，不弹窗 / 不中断加载流程。
                TurnOffBothSignalGenerators();

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
        /// 关闭 RF / LO 两台信号源的 RF 输出。
        /// <para>目的：自动测试开始前让频谱仪不显示任何信号，
        /// 避免上次残留的载波 / LO 漏信号被误判为频谱异常。</para>
        /// <para>失败容忍：仪器为 null、未连接、或命令抛错 → 仅记日志，不弹窗、不中断加载流程。</para>
        /// </summary>
        private void TurnOffBothSignalGenerators()
        {
            if (_rf != null && _rf.IsConnected)
            {
                try
                {
                    _rf.SetOutputState(false);
                    Logger.Log("[AutoTestPanel] 已关闭 RF 信号源输出。");
                }
                catch (Exception ex)
                {
                    Logger.Log("[AutoTestPanel] 关闭 RF 信号源输出失败: " + ex.Message);
                }
            }

            if (_lo != null && _lo.IsConnected)
            {
                try
                {
                    _lo.SetOutputState(false);
                    Logger.Log("[AutoTestPanel] 已关闭 LO 信号源输出。");
                }
                catch (Exception ex)
                {
                    Logger.Log("[AutoTestPanel] 关闭 LO 信号源输出失败: " + ex.Message);
                }
            }
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
        /// <para>真资源模式（receiver + 三仪表都就绪）：走 <see cref="TestCatalog.CreateProduction(ReceiverSerialPort, SignalGenerator, SignalGeneratorLO, SpectrumAnalyzer, int, TemperatureRange)"/>
        /// （模式×频点遍历 + 按温度分支）。资源不全则不加载任何测试项 + 提示原因。</para>
        /// </summary>
        private void LoadDefaultTestsToGrid()
        {
            _engine.Clear();
            ClearTestRows();

            IReadOnlyList<ITestItem> items = null;
            // 温度：cmbTemp 索引 0/1/2 → Normal/High/Low；索引无效时退回常温
            TemperatureRange temperature;
            switch (cmbTemp.SelectedIndex)
            {
                case 1: temperature = TemperatureRange.High; break;
                case 2: temperature = TemperatureRange.Low; break;
                default: temperature = TemperatureRange.Normal; break;
            }

            // H-6 修复：先一次性快照全部资源状态（receiver + 三仪表），
            // 后续分支判断统一使用这份快照，避免分支间状态分散读取导致
            // "DGV 重建时看到的资源"与"测试项实际执行时看到的资源"不一致。
            ResourceSnapshot snap = SnapshotResources();

            if (snap.AllProductionReady)
            {
                int channel = _receiverPanel.SelectedChannel;
                if (channel < 1 || channel > 8) channel = 1;
                items = TestCatalog.CreateProduction(
                    snap.Receiver, _rf, _lo, _sa, channel, temperature);
            }
            else
            {
                // 资源不全（receiver 串口未开 / 接收机控制面板未注入 / 某台仪表未连接），
                // 不再回退到单点兼容清单——直接不加载任何测试项 + 把"为什么不满足"写到 UI + 日志。
                string reason = string.IsNullOrEmpty(snap.Reason) ? "资源未就绪" : snap.Reason;
                Logger.Log("[AutoTestPanel] 资源不全，不加载测试项：" + reason);
                UpdateStep("资源未就绪：" + reason);
                btnRunAll.Enabled = false;
                return;
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

        /// <summary>
        /// 资源快照：在方法入口一次性采集 receiver 串口 + 三仪表的就绪状态，
        /// 确保后续分支判断基于同一时刻的状态，避免"读 receiver 引用 → 读 IsConnected"
        /// 跨多步时被另一控件异步改动（参见审查报告 H-6）。
        /// </summary>
        private ResourceSnapshot SnapshotResources()
        {
            ResourceSnapshot s = new ResourceSnapshot();

            if (_receiverPanel == null)
            {
                s.Receiver = null;
                s.ReceiverOpen = false;
                s.RfReady = _rf != null && _rf.IsConnected;
                s.LoReady = _lo != null && _lo.IsConnected;
                s.SaReady = _sa != null && _sa.IsConnected;
                s.Reason = "接收机控制面板未注入";
                return s;
            }

            s.Receiver = _receiverPanel._receiver;
            s.ReceiverOpen = s.Receiver != null && s.Receiver.IsOpen;

            // 三仪表实时查询（IsConnected 是 VISA 内部实时探测，未连接/被外部断开均反映为 false）
            s.RfReady = _rf != null && _rf.IsConnected;
            s.LoReady = _lo != null && _lo.IsConnected;
            s.SaReady = _sa != null && _sa.IsConnected;

            // 复用纯判定逻辑（独立文件 ResourceSnapshotLogic.cs，
            // 可被 Tests/AutoTest.Tests 链接源文件直接覆盖所有组合，
            // 无需实例化 UserControl 也无需引入 System.Windows.Forms）。
            ResourceSnapshotDecision decision = ResourceSnapshotLogic.EvaluateReadiness(
                s.Receiver != null, s.ReceiverOpen, s.RfReady, s.LoReady, s.SaReady);

            s.Reason = decision.Reason;
            s.AllProductionReady = decision.AllReady;
            return s;
        }

        /// <summary>
        /// 资源就绪状态快照（仅供 SnapshotResources 内部使用）。
        /// <para>WinForms 单 UI 线程下，方法内的多次字段读取对该操作而言是原子的——
        /// 不存在真正的多线程交错，问题是分散读取带来的"先后两步之间资源状态被
        /// 另一控件改动"的逻辑竞态（H-6）。本结构体把 6 个分散判断折叠为 1 次快照。</para>
        /// </summary>
        internal struct ResourceSnapshot
        {
            public ReceiverSerialPort Receiver;   // 可能为 null
            public bool ReceiverOpen;
            public bool RfReady;
            public bool LoReady;
            public bool SaReady;
            public bool AllProductionReady;       // 由 ResourceSnapshotLogic.EvaluateReadiness 填充
            public string Reason;                 // 不满足时的具体原因；为空表示全就绪
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

        /// <summary>"操作"列的引用。运行中改单 cell.Value 切"▶ 运行" ↔ "■ 停止"，
        /// 其它行 cell.ReadOnly=true（disable、变灰、不可点）。
        /// 之所以升为字段，是因为要在 RunSingleTestAsync 入口访问列实例。</summary>
        private DataGridViewButtonColumn colAction;

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
            colAction = new DataGridViewButtonColumn
            {
                Name = "colAction",
                HeaderText = "操作",
                Text = "▶ 运行",          // 列默认文字（运行入口可单独覆盖单 cell.Value）
                UseColumnTextForButtonValue = false,   // 单 cell.Value 生效（运行中只改当前行 Value = "■ 停止"，其它行保持 "▶ 运行"）
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
        /// <para>按钮文字 = "▶ 运行" → 调 <see cref="RunSingleTestAsync"/> 开始运行；
        /// 按钮文字 = "■ 停止" → 取消当前 CancellationTokenSource（对应行正在运行）。</para>
        /// </summary>
        private async void OnDgvTestsCellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            if (e.ColumnIndex != ColAction) return;
            if (_runAllInProgress) return;     // 一键运行期间忽略手动运行

            // 单条运行中：只允许点当前行"■ 停止"取消；其它行已 ReadOnly=true（视觉 disabled），
            // 此处再兜一层——非当前行的点击一律 return，不动 cts，避免误停当前测试。
            if (_runInProgress)
            {
                if (e.RowIndex == _runningRowIndex
                    && _runCts != null && !_runCts.IsCancellationRequested)
                {
                    _runCts.Cancel();
                    UpdateStep("正在停止测试…");
                }
                return;
            }

            await RunSingleTestAsync(e.RowIndex);
        }

        /// <summary>
        /// 切换"运行中" / "非运行中"状态下各入口的可用性。
        /// <para>disable 范围：一键运行按钮 + 加载测试按钮 + 温度/通道/模式下拉框。
        /// 防止"运行中再点运行"或"运行中切温/通道"导致紊乱。</para>
        /// </summary>
        private void SetRunButtonsEnabledState(bool enabled)
        {
            if (btnRunAll != null) btnRunAll.Enabled = enabled;
            if (btnLoadTest != null) btnLoadTest.Enabled = enabled;
            if (cmbTemp != null) cmbTemp.Enabled = enabled;
            if (cmbChannel != null) cmbChannel.Enabled = enabled;
            if (cmbMode != null) cmbMode.Enabled = enabled;
        }

        /// <summary>
        /// 单条测试项的执行逻辑（共用：手动按行触发 + 一键运行逐条调用）。
        /// <para>入口会修改行内"操作"列与"状态"列文案，运行结束还原。</para>
        /// <para>支持"■ 停止"按钮：构造 CancellationTokenSource，点击停止时取消，
        /// 测试项内部 <c>ct.ThrowIfCancellationRequested()</c> 抛出 OperationCanceledException，
        /// finally 块中 RF/LO 输出仍会兜底关闭。</para>
        /// </summary>
        private async Task RunSingleTestAsync(int rowIndex, bool isFromRunAll = false)
        {
            if (rowIndex < 0 || rowIndex >= dgvTests.Rows.Count) return;
            var row = dgvTests.Rows[rowIndex];
            var test = row.Tag as ITestItem;
            if (test == null) return;

            // 防重入：手动点行（默认 isFromRunAll=false）时拦截 _runInProgress。
            // 一键运行（isFromRunAll=true）跳过此检查——因为一键运行在 _runAllInProgress==true 期间
            // 循环调本方法，如果拦截会全部 return，循环空跑 + 进度条瞬间 100%。
            if (!isFromRunAll && _runInProgress) return;

            var statusCell = row.Cells[ColStatus];
            var actionCell = row.Cells[ColAction];

            // 进入运行态：构造 cts、置位、按钮文字改"停止"、disable 其他入口
            _runInProgress = true;
            _runningRowIndex = rowIndex;
            _runCts = new CancellationTokenSource();
            // 仅手动单条运行时 disable 一键运行 / 加载 / 切温/通道；
            // 一键运行期间（isFromRunAll=true）跳过 disable 一键运行按钮——
            // 该按钮此时已被 OnRunAllClicked 改成"■ 停止测试"，必须保持可点，
            // 否则单条测试项进出 finally 会反复 disable/enable，造成按钮灰抖动。
            if (!isFromRunAll)
            {
                SetRunButtonsEnabledState(false);
            }
            else
            {
                // 一键运行期间只 disable 加载 / 切温/通道（不禁掉"■ 停止测试"按钮）
                if (btnLoadTest != null) btnLoadTest.Enabled = false;
                if (cmbTemp != null) cmbTemp.Enabled = false;
                if (cmbChannel != null) cmbChannel.Enabled = false;
                if (cmbMode != null) cmbMode.Enabled = false;
            }

            // 仅当前行按钮变"■ 停止"；其它所有行按钮 ReadOnly=true + 灰化，
            // 视觉上模拟"真 disabled"（DataGridViewButtonColumn 没有 Enabled 属性）
            actionCell.Value = "■ 停止";
            for (int i = 0; i < dgvTests.Rows.Count; i++)
            {
                if (i == rowIndex) continue;
                var cell = dgvTests.Rows[i].Cells[ColAction];
                cell.ReadOnly = true;
                cell.Style.ForeColor = ActionDisabledFore;
                cell.Style.BackColor = ActionDisabledBack;
                cell.Style.SelectionBackColor = ActionDisabledBack;
                cell.Style.SelectionForeColor = ActionDisabledFore;
            }

            statusCell.Value = "运行中…";
            statusCell.Style.ForeColor = StatusColorRunning;

            bool wasCancelled = false;
            try
            {
                var progress = new Progress<string>(msg => UpdateStep(msg));
                TestResult result = await test.ExecuteAsync(progress, _runCts.Token)
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
            catch (OperationCanceledException)
            {
                wasCancelled = true;
                statusCell.Value = "已停止";
                statusCell.Style.ForeColor = StatusColorReady;   // 灰色
            }
            catch (Exception ex)
            {
                statusCell.Value = "异常: " + ex.Message;
                statusCell.Style.ForeColor = StatusColorFail;
            }
            finally
            {
                // 还原 UI：当前行按钮文字改回"▶ 运行"；其它行 ReadOnly + 灰化色还原。
                // finally 块自身必须被保护——面板已 Dispose / DGV 已释放时，直接访问
                // actionCell.Value / dgvTests.Rows 会抛 ObjectDisposedException，
                // 不仅打断 finally，还可能遮蔽主流程的真实异常。
                TryRestoreRowVisuals(actionCell, rowIndex);

                // 释放/复位 cts：手动单条运行时安全释放；一键运行期间**保留 cts 与 IsCancellationRequested**，
                // 这样下一项进入前能从 _runCts.IsCancellationRequested 读到取消信号并立即 break，
                // 而不会因为 cts 被重建而丢信号、下项继续跑。
                // 释放/复位 cts：手动单条运行时安全释放；一键运行期间**保留 cts 与 IsCancellationRequested**，
                // 这样下一项进入前能从 _runCts.IsCancellationRequested 读到取消信号并立即 break，
                // 而不会因为 cts 被重建而丢信号、下项继续跑。
                if (!isFromRunAll)
                {
                    _runCts?.Dispose();
                    _runCts = null;
                }
                _runInProgress = false;
                _runningRowIndex = -1;
                // 对称恢复：单条手动运行恢复一键运行按钮；一键运行期间一键按钮保持可点（文字由 OnRunAllClicked 管）
                if (!isFromRunAll)
                {
                    SetRunButtonsEnabledState(true);
                }
                else
                {
                    if (btnLoadTest != null) btnLoadTest.Enabled = true;
                    if (cmbTemp != null) cmbTemp.Enabled = true;
                    if (cmbChannel != null) cmbChannel.Enabled = true;
                    if (cmbMode != null) cmbMode.Enabled = true;
                }

                if (wasCancelled)
                {
                    UpdateStep("测试已停止（手动取消）");
                }
            }
        }

        /// <summary>
        /// "一键运行"按钮：依次执行所有测试项，进度条按总项数推进。
        /// </summary>
        private async void OnRunAllClicked(object sender, EventArgs e)
        {
            // 一键运行已开始时，再次点击 = "停止测试"按钮 → 取消 _runCts 让循环 break
            if (_runAllInProgress)
            {
                if (_runCts != null && !_runCts.IsCancellationRequested)
                {
                    _runCts.Cancel();
                    UpdateStep("正在停止一键运行…");
                }
                return;
            }
            if (dgvTests.Rows.Count == 0) return;

            _runAllInProgress = true;
            btnRunAll.Text = "■ 停止测试";   // 同一个按钮变"停止"语义

            int total = dgvTests.Rows.Count;
            try
            {
                UpdateProgress(0, $"一键运行：共 {total} 项");
                for (int i = 0; i < total; i++)
                {
                    // 循环前后各检查一次：取消请求（来自一键停止按钮 / DGV 单条停止）→ 跳出
                    if (_runCts != null && _runCts.IsCancellationRequested) break;
                    UpdateProgress(
                        (int)Math.Round(100.0 * i / total),
                        $"一键运行 {i + 1}/{total}");
                    // isFromRunAll=true：绕过 _runInProgress 拦截，否则循环会空跑
                    await RunSingleTestAsync(i, isFromRunAll: true);
                    if (_runCts != null && _runCts.IsCancellationRequested) break;
                }
                bool aborted = _runCts != null && _runCts.IsCancellationRequested;
                UpdateProgress(
                    aborted ? (int)Math.Round(100.0 * _runningRowIndex / Math.Max(total, 1)) : 100,
                    aborted
                        ? "一键运行已中止（用户取消）"
                        : $"一键运行完成：{total}/{total}");
            }
            finally
            {
                _runAllInProgress = false;
                // 一键运行退出时释放 cts（单条路径里 RunSingleTestAsync 自己已释放，
                // 但 for-break / 异常路径可能没释放，这里兜底）
                try { _runCts?.Dispose(); } catch { }
                _runCts = null;
                btnRunAll.Text = "一键运行";
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
        /// finally 块专用：把 DGV 当前行按钮文字还原成"▶ 运行"，其它行还原 ReadOnly 与默认色。
        /// 任何 UI 访问全部用 IsDisposed / IsHandleCreated 守卫，单行失败不影响其它行，
        /// 整个 finally 自身抛错会被吞掉并写日志——避免 finally 异常遮蔽主流程真实异常。
        /// </summary>
        private void TryRestoreRowVisuals(DataGridViewCell actionCell, int rowIndex)
        {
            try
            {
                if (dgvTests == null || dgvTests.IsDisposed || !dgvTests.IsHandleCreated)
                {
                    return;
                }

                // 当前行按钮文字还原
                if (actionCell != null)
                {
                    try
                    {
                        actionCell.Value = "▶ 运行";
                    }
                    catch (Exception ex)
                    {
                        // cell 可能已被 Dispose，单 cell 失败不影响其它行
                        Logger.Log("[AutoTestPanel] finally 还原当前行按钮文字失败: " + ex.Message);
                    }
                }

                // 其它行 ReadOnly + 灰化色还原
                int rowCount;
                try { rowCount = dgvTests.Rows.Count; }
                catch (Exception ex)
                {
                    Logger.Log("[AutoTestPanel] finally 读取 Rows.Count 失败: " + ex.Message);
                    return;
                }

                for (int i = 0; i < rowCount; i++)
                {
                    DataGridViewCell cell;
                    try
                    {
                        cell = dgvTests.Rows[i].Cells[ColAction];
                    }
                    catch (Exception ex)
                    {
                        // 单行访问失败跳过
                        Logger.Log("[AutoTestPanel] finally 访问行 " + i + " 失败: " + ex.Message);
                        continue;
                    }
                    if (cell == null) continue;

                    try
                    {
                        cell.ReadOnly = false;
                        cell.Style.ForeColor = dgvTests.DefaultCellStyle.ForeColor;
                        cell.Style.BackColor = dgvTests.DefaultCellStyle.BackColor;
                        cell.Style.SelectionBackColor = dgvTests.DefaultCellStyle.SelectionBackColor;
                        cell.Style.SelectionForeColor = dgvTests.DefaultCellStyle.SelectionForeColor;
                    }
                    catch (Exception ex)
                    {
                        // 单 cell 写样式失败跳过
                        Logger.Log("[AutoTestPanel] finally 还原行 " + i + " 样式失败: " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                // finally 自身的兜底：异常写日志但绝不抛出
                Logger.Log("[AutoTestPanel] finally 整体异常: " + ex.Message);
            }
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
