using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO.Ports;
using System.Windows.Forms;
using A205AutoTestSystem.Common;
using A205AutoTestSystem.InstrumentControl;
using A205AutoTestSystem.MatrixControl;
using A205AutoTestSystem.ReceiverControl;

namespace A205AutoTestSystem.UI
{
    /// <summary>
    /// 接收机控制面板（M5 阶段实现）。
    /// <para>布局（严格按 spec §5.2）：</para>
    /// <list type="number">
    /// <item>串口连接区（接收机 + 矩阵）</item>
    /// <item>接收机参数配置区（温度 / 通道 / 模式 / 频段 / AGC 衰减）</item>
    /// <item>仪表连接状态区（RF / LO / 频谱仪）</item>
    /// <item>通信日志区（RichTextBox + 清除）</item>
    /// </list>
    /// <para>控件创建已移至 <c>ReceiverControlPanel.Designer.cs</c> 的 <c>InitializeComponent()</c>，
    /// 可在 VS 设计器中可视化编辑。</para>
    /// </summary>
    public partial class ReceiverControlPanel : UserControl
    {
        // ============================================================
        // 业务对象（保留 _ 前缀；非控件字段，Designer 不感知）
        // M6.x 阶段：字段改为 internal，便于 MainForm / AutoTestPanel 共享资源
        // ============================================================
        internal ReceiverSerialPort _receiver;
        private readonly MatrixSerialPort _matrix = new MatrixSerialPort();

        internal SignalGenerator _rfGenerator;
        internal SignalGeneratorLO _loGenerator;
        internal SpectrumAnalyzer _spectrumAnalyzer;
        private ODP3063 _powerSupply;

        // 电源输出的软件侧状态：连接时不查询/不设置仪表，默认视为未输出（OFF）。
        // btnOpenPower 点击时联动：开机=电源ON→自动连接收机串口→RF/LO :OUTP 1；
        // 关机=RF/LO :OUTP 0→断开接收机串口→电源OFF。
        private bool _powerOutputOn = false;

        // 仪表控制区频谱仪 Toggle 按钮的本地状态：
        // 用于按钮 Text/BackColor 反映当前是否已开启。
        // UI 状态与仪表侧 SCPI 命令独立，断连时复位为 false。
        private bool _saMaxHoldOn = false;
        private bool _saPeakAutoOn = false;
        private bool _saTraceAvgOn = false;
        private bool _saMinHoldOn = false;

        // 频段 ComboBox 当前显示列表（依模式动态变化，由 RefreshBandCombo 写入）
        private IReadOnlyList<ModeBandTable.BandInfo> _currentBands;

        // 抑制初始化阶段事件回调（避免在构建控件时立刻触发副作用）
        private bool _initialized;

        // 仪表状态颜色常量（不写进 .resx，保持代码可读性）
        private static readonly Color StatusColorDisconnected = Color.Red;
        private static readonly Color StatusColorConnected = Color.LimeGreen;

        // 波特率下拉默认值
        private static readonly int[] BaudRateOptions = { 9600, 19200, 38400, 57600, 115200 };

        // 模式联动频谱仪参数（Hz）
        private const double SaSpanModeHz = 50_000_000.0;             // 所有模式 Span 均为 50 MHz
        private const double SaCenterFreqMode12Hz = 70_000_000.0;    // Mode1/2 中频 70 MHz
        private const double SaCenterFreqMode3Hz = 750_000_000.0;    // Mode3 中心 750 MHz

        // 模式联动信号源功率（dBm）
        private const double RfPowerMode13Dbm = -20.0;               // Mode1/Mode3 RF 功率
        private const double RfPowerMode2Dbm = -10.0;                // Mode2 RF 功率
        private const double LoPowerModeDbm = 3.0;                   // 三个模式 LO 均为 3 dBm

        // ============================================================
        // 构造与生命周期
        // ============================================================

        public ReceiverControlPanel()
        {
            InitializeComponent();        // ← Designer 生成（参见 Designer.cs）
            InitializeDefaults();
            // WireEvents() 不需要：Designer 内已订阅
            _initialized = true;

            // 订阅频段下拉框选中变化事件：选中时隐式切换 RF/LO 信号源频率
            cmbBand.SelectedIndexChanged += OnBandSelectionChanged;

            // 订阅全局 Logger，实时显示通信日志
            Logger.MessageLogged += OnLogMessage;
        }

        /// <summary>
        /// 重写 Dispose，避免页面关闭后仍持有 Logger 事件订阅造成泄漏。
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { Logger.MessageLogged -= OnLogMessage; }
                catch { /* 静默 */ }

                // 先退订仪表 ConnectionChanged 事件,避免 Dispose 阶段触发回调访问已释放控件。
                // (基类 VisaBaseInstrument.Dispose → Disconnect 不会触发 ConnectionChanged(false),
                //  但显式退订更稳:防止将来基类改了语义导致页面销毁后仍有事件穿透。)
                try { if (_rfGenerator != null) _rfGenerator.ConnectionChanged -= OnRfConnectionChanged; } catch { }
                try { if (_loGenerator != null) _loGenerator.ConnectionChanged -= OnLoConnectionChanged; } catch { }
                try { if (_spectrumAnalyzer != null) _spectrumAnalyzer.ConnectionChanged -= OnSaConnectionChanged; } catch { }
                try { if (_powerSupply != null) _powerSupply.ConnectionChanged -= OnPowerConnectionChanged; } catch { }

                // 关闭业务资源
                try { _receiver?.Close(); } catch { }
                try { _receiver?.Dispose(); } catch { }
                try { _matrix?.Disconnect(); } catch { }
                try { _rfGenerator?.Dispose(); } catch { }
                try { _loGenerator?.Dispose(); } catch { }
                try { _spectrumAnalyzer?.Dispose(); } catch { }
                try { _powerSupply?.Dispose(); } catch { }

                // Designer 创建的控件容器（容器内含 ComboBox 等需显式释放的资源）
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
            // 串口列表
            try
            {
                string[] ports = SerialPort.GetPortNames();
                if (ports != null && ports.Length > 0)
                {
                    Array.Sort(ports);
                    cmbReceiverPort.Items.AddRange(ports);
                    cmbMatrixPort.Items.AddRange(ports);
                    cmbReceiverPort.SelectedIndex = 0;
                    cmbMatrixPort.SelectedIndex = 0;
                }
                else
                {
                    cmbReceiverPort.Items.Add("(无)");
                    cmbMatrixPort.Items.Add("(无)");
                    cmbReceiverPort.SelectedIndex = 0;
                    cmbMatrixPort.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[UI] 获取串口列表失败：{ex.Message}");
            }

            // 波特率默认 115200（在 Designer 中已添加 Items）
            int idx115200 = cmbReceiverBaud.Items.IndexOf("115200");
            if (idx115200 >= 0) cmbReceiverBaud.SelectedIndex = idx115200;
            idx115200 = cmbMatrixBaud.Items.IndexOf("115200");
            if (idx115200 >= 0) cmbMatrixBaud.SelectedIndex = idx115200;

            // 参数默认
            rbTempNormal.Checked = true;
            // 通道下拉 8 项（设计器 InitializeComponent 内不允许循环，故在此运行时填充）
            for (int ch = 1; ch <= 8; ch++)
            {
                cmbChannel.Items.Add("通道" + ch);
            }
            cmbChannel.SelectedIndex = 0;
            rbMode1.Checked = true;
            // 触发频段下拉刷新
            RefreshBandCombo(ReceiverMode.Mode1);
            numAgcAtten.Value = 0;

            // 仪表圆点红色
            pnlRfStatus.BackColor = StatusColorDisconnected;
            pnlLoStatus.BackColor = StatusColorDisconnected;
            pnlSaStatus.BackColor = StatusColorDisconnected;
            pnlPowerStatus.BackColor = StatusColorDisconnected;
        }

        // ============================================================
        // 资源就绪状态（M6.x 阶段暴露给 AutoTestPanel 用）
        // ============================================================

        /// <summary>接收机串口是否已 Open。AutoTestPanel 用来决定能否走 CreateProduction。</summary>
        public bool IsReceiverReady => _receiver != null && _receiver.IsOpen;

        /// <summary>三台仪表（RF/LO/SA）是否都已 Connect。</summary>
        public bool AreInstrumentsReady =>
            _rfGenerator != null && _rfGenerator.IsConnected
            && _loGenerator != null && _loGenerator.IsConnected
            && _spectrumAnalyzer != null && _spectrumAnalyzer.IsConnected;

        /// <summary>当前已连接的 RF 信号源。供 AutoTestPanel 在『加载测试』时拉取推送；未连接返回 null。</summary>
        public SignalGenerator RfGenerator => _rfGenerator;

        /// <summary>当前已连接的 LO 信号源。供 AutoTestPanel 在『加载测试』时拉取推送；未连接返回 null。</summary>
        public SignalGeneratorLO LoGenerator => _loGenerator;

        /// <summary>当前已连接的频谱仪。供 AutoTestPanel 在『加载测试』时拉取推送；未连接返回 null。</summary>
        public SpectrumAnalyzer SpectrumAnalyzerInstance => _spectrumAnalyzer;

        /// <summary>"常温" RadioButton（pnlTempGroup 内）。供 AutoTestPanel 同步温度档位用。RadioButton 本身无 CheckedChanged 订阅，因此外部赋值不会重复触发『设置』按钮里的命令。</summary>
        public RadioButton RbTempNormal => rbTempNormal;

        /// <summary>"高温" RadioButton（pnlTempGroup 内）。供 AutoTestPanel 同步温度档位用。</summary>
        public RadioButton RbTempHigh => rbTempHigh;

        /// <summary>"低温" RadioButton（pnlTempGroup 内）。供 AutoTestPanel 同步温度档位用。</summary>
        public RadioButton RbTempLow => rbTempLow;

        /// <summary>当前 UI 上选中的通道号（1..8）。未选中时返回 1。</summary>
        public int SelectedChannel
        {
            get
            {
                if (cmbChannel == null || cmbChannel.SelectedIndex < 0) return 1;
                int ch = cmbChannel.SelectedIndex + 1;
                if (ch < 1 || ch > 8) return 1;
                return ch;
            }
        }

        // ============================================================
        // 频段 ComboBox 动态填充
        // ============================================================

        private void RefreshBandCombo(ReceiverMode mode)
        {
            cmbBand.BeginUpdate();
            try
            {
                cmbBand.Items.Clear();
                var bands = ModeBandTable.GetBands(mode);
                _currentBands = bands;
                foreach (var b in bands)
                {
                    cmbBand.Items.Add(b.DisplayName);
                }
                if (cmbBand.Items.Count > 0)
                {
                    cmbBand.SelectedIndex = 0;
                }
            }
            finally
            {
                cmbBand.EndUpdate();
            }
        }

        // ============================================================
        // 事件处理（Designer 中已统一订阅 this.btnXxx.Click += OnXxxClicked;）
        // ============================================================

        // -------- 串口
        private void OnReceiverConnectClicked(object sender, EventArgs e)
        {
            if (_receiver != null && _receiver.IsOpen)
            {
                MessageBox.Show(this, "接收机串口已处于连接状态。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try
            {
                string portName = cmbReceiverPort.SelectedItem == null ? null : cmbReceiverPort.SelectedItem.ToString();
                int baudRate = int.Parse(cmbReceiverBaud.Text);
                if (string.IsNullOrEmpty(portName) || portName == "(无)")
                {
                    MessageBox.Show(this, "请先选择有效的串口。", "提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                _receiver = new ReceiverSerialPort();
                _receiver.Open(portName, baudRate);

                Logger.Log($"[UI] Receiver connected on {portName} @ {baudRate}.");
            }
            catch (Exception ex)
            {
                _receiver = null;
                MessageBox.Show(this, "打开串口失败: " + ex.Message, "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnReceiverDisconnectClicked(object sender, EventArgs e)
        {
            try
            {
                _receiver?.Close();
                Logger.Log("[UI] Receiver serial closed.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _receiver = null;
            }
        }

        private void OnMatrixConnectClicked(object sender, EventArgs e)
        {
            try
            {
                string portName = cmbMatrixPort.SelectedItem == null ? null : cmbMatrixPort.SelectedItem.ToString();
                int baudRate = int.Parse(cmbMatrixBaud.Text);
                if (string.IsNullOrEmpty(portName) || portName == "(无)")
                {
                    MessageBox.Show(this, "请先选择有效的串口。", "提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                _matrix.Connect(portName, baudRate);
                // MatrixSerialPort 桩的 IsConnected 始终返回 false，
                // 因此用本地标志位来反映"UI 已请求连接"。
                // M3 协议到位后，可改为订阅 ConnectionChanged 事件。
                Logger.Log($"[UI] Matrix connect requested on {portName} @ {baudRate}.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "矩阵连接失败: " + ex.Message, "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnMatrixDisconnectClicked(object sender, EventArgs e)
        {
            try
            {
                _matrix.Disconnect();
                Logger.Log("[UI] Matrix serial closed.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // -------- 通道下拉切换（仅切换工装矩阵；接收机通道命令由 btnModeSet 随模式下发）
        private void OnChannelSelected(object sender, EventArgs e)
        {
            if (!_initialized) return; // 初始化阶段不触发切换

            int ch = cmbChannel.SelectedIndex + 1; // 0..7 → 1..8
            try
            {
                _matrix.SwitchChannel(ch);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "矩阵通道切换失败: " + ex.Message, "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                Logger.Log("[Error] 矩阵通道切换失败: " + ex.Message);
            }
        }

        // -------- 模式切换（联动频段下拉 + 频谱仪中心频率/Span + RF/LO 功率）
        private void OnModeChanged(object sender, EventArgs e)
        {
            if (!_initialized) return;
            if (!rbMode1.Checked && !rbMode2.Checked && !rbMode3.Checked) return;

            ReceiverMode mode;
            if (rbMode1.Checked) mode = ReceiverMode.Mode1;
            else if (rbMode2.Checked) mode = ReceiverMode.Mode2;
            else mode = ReceiverMode.Mode3;

            RefreshBandCombo(mode);
            ApplySpectrumAnalyzerForMode(mode);
            ApplySignalPowerForMode(mode);
        }

        /// <summary>
        /// 按当前工作模式联动设置频谱仪中心频率与扫频跨度：
        /// Mode1/2 → 70 MHz / 50 MHz；Mode3 → 750 MHz / 50 MHz。
        /// 频谱仪未连接时跳过；设置失败仅记录日志，不打断模式切换。
        /// </summary>
        private void ApplySpectrumAnalyzerForMode(ReceiverMode mode)
        {
            if (_spectrumAnalyzer == null || !_spectrumAnalyzer.IsConnected)
            {
                Logger.Log("[UI] 频谱仪未连接，跳过模式联动的中心频率/Span 设置");
                return;
            }

            double centerHz = mode == ReceiverMode.Mode3
                ? SaCenterFreqMode3Hz
                : SaCenterFreqMode12Hz;

            try
            {
                _spectrumAnalyzer.SetFrequencySpan(centerHz, SaSpanModeHz);
                double centerMHz = centerHz / 1_000_000.0;
                Logger.Log($"[UI] 频谱仪已按 {mode} 联动设置：中心 {centerMHz:F0} MHz，Span 50 MHz");
            }
            catch (Exception ex)
            {
                Logger.Log($"[UI] 频谱仪模式联动设置失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 按当前工作模式联动设置 RF/LO 信号源输出功率：
        /// Mode1/3 → RF -20 dBm；Mode2 → RF -10 dBm；LO 三个模式均为 3 dBm。
        /// 仅设置功率，不改变频率与输出开关；单台仪表未连接或设置失败只记录日志，不打断模式切换。
        /// </summary>
        private void ApplySignalPowerForMode(ReceiverMode mode)
        {
            double rfPowerDbm = mode == ReceiverMode.Mode2
                ? RfPowerMode2Dbm
                : RfPowerMode13Dbm;

            if (_rfGenerator == null || !_rfGenerator.IsConnected)
            {
                Logger.Log("[UI] RF 信号源未连接，跳过模式联动的 RF 功率设置");
            }
            else
            {
                try
                {
                    _rfGenerator.SetPower(rfPowerDbm);
                    Logger.Log($"[UI] RF 信号源已按 {mode} 联动设置功率 {rfPowerDbm} dBm");
                }
                catch (Exception ex)
                {
                    Logger.Log($"[UI] RF 信号源模式联动功率设置失败: {ex.Message}");
                }
            }

            if (_loGenerator == null || !_loGenerator.IsConnected)
            {
                Logger.Log("[UI] LO 信号源未连接，跳过模式联动的 LO 功率设置");
                return;
            }

            try
            {
                _loGenerator.SetPower(LoPowerModeDbm);
                Logger.Log($"[UI] LO 信号源已按 {mode} 联动设置功率 {LoPowerModeDbm} dBm");
            }
            catch (Exception ex)
            {
                Logger.Log($"[UI] LO 信号源模式联动功率设置失败: {ex.Message}");
            }
        }

        // -------- 设置按钮
        private void OnTempSetClicked(object sender, EventArgs e)
        {
            if (_receiver == null || !_receiver.IsOpen)
            {
                MessageBox.Show(this, "请先连接接收机串口。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            TemperatureRange range;
            if (rbTempNormal.Checked) range = TemperatureRange.Normal;
            else if (rbTempHigh.Checked) range = TemperatureRange.High;
            else range = TemperatureRange.Low;
            try
            {
                _receiver.SendTemperatureCommand(range);
            }
            catch (Exception ex)
            {
                Logger.Log("[Error] 温度命令发送失败: " + ex.Message);
                MessageBox.Show(this, ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnModeSetClicked(object sender, EventArgs e)
        {
            if (_receiver == null || !_receiver.IsOpen)
            {
                MessageBox.Show(this, "请先连接接收机串口。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            ReceiverMode mode;
            if (rbMode1.Checked) mode = ReceiverMode.Mode1;
            else if (rbMode2.Checked) mode = ReceiverMode.Mode2;
            else mode = ReceiverMode.Mode3;
            int ch = cmbChannel.SelectedIndex + 1; // 0..7 → 1..8
            try
            {
                // 先发送当前所选通道的通道切换命令，再发送模式切换命令序列
                _receiver.SendChannelThenModeCommand(ch, mode);
            }
            catch (Exception ex)
            {
                Logger.Log("[Error] 通道/模式命令发送失败: " + ex.Message);
                MessageBox.Show(this, ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // -------- 频段设置
        private void OnBandSetClicked(object sender, EventArgs e)
        {
            if (_receiver == null || !_receiver.IsOpen)
            {
                MessageBox.Show(this, "请先连接接收机串口。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (_currentBands == null || cmbBand.SelectedIndex < 0
                || cmbBand.SelectedIndex >= _currentBands.Count)
            {
                MessageBox.Show(this, "无可用频段。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            ReceiverMode mode;
            if (rbMode1.Checked) mode = ReceiverMode.Mode1;
            else if (rbMode2.Checked) mode = ReceiverMode.Mode2;
            else mode = ReceiverMode.Mode3;

            int bandIndex = _currentBands[cmbBand.SelectedIndex].BandIndex;

            try
            {
                _receiver.SendBandCommand(mode, bandIndex);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"发送频段命令失败：{ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnAgcSetClicked(object sender, EventArgs e)
        {
            if (_receiver == null || !_receiver.IsOpen)
            {
                MessageBox.Show(this, "请先连接接收机串口。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            int ch = cmbChannel.SelectedIndex + 1;
            int atten = (int)numAgcAtten.Value;
            try
            {
                _receiver.SendAttenuationCommand(ch, atten);
            }
            catch (Exception ex)
            {
                Logger.Log("[Error] 衰减命令发送失败: " + ex.Message);
                MessageBox.Show(this, ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // -------- 仪表连接（一键连接/一键断开编排）
        //
        // 约定：
        // - TryConnectXxx：已连接则跳过并返回 true；连接失败（地址空/异常）只记日志、不弹窗，
        //   失败时释放半成品对象并置 null、状态灯置红，返回 false，保证一键连接可继续下一台。
        // - DisconnectXxx：best-effort，异常只记日志，最终置 null、状态灯置红。

        private bool TryConnectRf()
        {
            // 已"声称连接"成功,但 IsConnected 字段在线缆掉了时不会自己变 false,
            // 必须主动 ping 一次(*IDN?)确认 session 还活着。探测失败 → 当作失联,
            // 走 DisconnectRf 释放旧对象,然后再走完整重连流程,避免 UI 卡"已连接"。
            if (_rfGenerator != null && _rfGenerator.IsConnected)
            {
                if (PingInstrument(_rfGenerator, "RF"))
                {
                    return true;
                }
                Logger.Log("[UI] RF 信号源 *IDN? 探测失败,按失联处理走重连。");
                DisconnectRf();
            }
            if (string.IsNullOrWhiteSpace(txtRfAddress.Text))
            {
                pnlRfStatus.BackColor = StatusColorDisconnected;
                Logger.Log("[Error] RF 连接失败: 地址为空。");
                return false;
            }
            try
            {
                _rfGenerator = new SignalGenerator(txtRfAddress.Text.Trim());
                _rfGenerator.ConnectionChanged += OnRfConnectionChanged;
                _rfGenerator.Connect();
                pnlRfStatus.BackColor = StatusColorConnected;
                Logger.Log("[UI] RF signal generator connected.");
                // 连接成功后确保调制输出保持关闭（CW 模式），失败仅日志
                DisableModulationQuiet("RF",
                    () => _rfGenerator.EnableModulation(false),
                    () => _rfGenerator.SetPulseModulation(false));
                return true;
            }
            catch (Exception ex)
            {
                pnlRfStatus.BackColor = StatusColorDisconnected;
                if (_rfGenerator != null)
                {
                    try { _rfGenerator.ConnectionChanged -= OnRfConnectionChanged; } catch { }
                    try { _rfGenerator.Dispose(); } catch { }
                }
                _rfGenerator = null;
                Logger.Log("[Error] RF 连接失败: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// RF 信号源 ConnectionChanged 事件回调：失联(IsConnected=false)时把状态灯变红,
        /// 避免线缆掉了但 UI 还显示绿色(原 IsConnected 字段不会自己变 false)。
        /// </summary>
        private void OnRfConnectionChanged(bool isConnected)
        {
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action<bool>(OnRfConnectionChanged), isConnected); }
                catch { /* 控件已销毁 */ }
                return;
            }
            if (pnlRfStatus != null && !pnlRfStatus.IsDisposed)
            {
                pnlRfStatus.BackColor = isConnected ? StatusColorConnected : StatusColorDisconnected;
            }
            Logger.Log($"[UI] RF 信号源连接状态变化: {(isConnected ? "已连接" : "已断开")}");
        }

        private void DisconnectRf()
        {
            if (_rfGenerator == null) return;
            try
            {
                try { _rfGenerator.ConnectionChanged -= OnRfConnectionChanged; } catch { }
                _rfGenerator.Disconnect();
                Logger.Log("[UI] RF signal generator disconnected.");
            }
            catch (Exception ex)
            {
                Logger.Log("[Error] RF 断开失败: " + ex.Message);
            }
            finally
            {
                try { _rfGenerator?.Dispose(); } catch { }
                _rfGenerator = null;
                if (pnlRfStatus != null && !pnlRfStatus.IsDisposed)
                {
                    pnlRfStatus.BackColor = StatusColorDisconnected;
                }
            }
        }

        private bool TryConnectLo()
        {
            // 已"声称连接"成功 → 先 *IDN? 探测,失败按失联处理走重连（参见 TryConnectRf 注释）
            if (_loGenerator != null && _loGenerator.IsConnected)
            {
                if (PingInstrument(_loGenerator, "LO"))
                {
                    return true;
                }
                Logger.Log("[UI] LO 信号源 *IDN? 探测失败,按失联处理走重连。");
                DisconnectLo();
            }
            if (string.IsNullOrWhiteSpace(txtLoAddress.Text))
            {
                pnlLoStatus.BackColor = StatusColorDisconnected;
                Logger.Log("[Error] LO 连接失败: 地址为空。");
                return false;
            }
            try
            {
                _loGenerator = new SignalGeneratorLO(txtLoAddress.Text.Trim());
                _loGenerator.ConnectionChanged += OnLoConnectionChanged;
                _loGenerator.Connect();
                pnlLoStatus.BackColor = StatusColorConnected;
                Logger.Log("[UI] LO signal generator connected.");
                // 连接成功后确保调制输出保持关闭（CW 模式），失败仅日志
                DisableModulationQuiet("LO",
                    () => _loGenerator.EnableModulation(false),
                    () => _loGenerator.SetPulseModulation(false));
                return true;
            }
            catch (Exception ex)
            {
                pnlLoStatus.BackColor = StatusColorDisconnected;
                if (_loGenerator != null)
                {
                    try { _loGenerator.ConnectionChanged -= OnLoConnectionChanged; } catch { }
                    try { _loGenerator.Dispose(); } catch { }
                }
                _loGenerator = null;
                Logger.Log("[Error] LO 连接失败: " + ex.Message);
                return false;
            }
        }

        /// <summary>LO 信号源 ConnectionChanged 回调（参见 OnRfConnectionChanged 注释）。</summary>
        private void OnLoConnectionChanged(bool isConnected)
        {
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action<bool>(OnLoConnectionChanged), isConnected); }
                catch { /* 控件已销毁 */ }
                return;
            }
            if (pnlLoStatus != null && !pnlLoStatus.IsDisposed)
            {
                pnlLoStatus.BackColor = isConnected ? StatusColorConnected : StatusColorDisconnected;
            }
            Logger.Log($"[UI] LO 信号源连接状态变化: {(isConnected ? "已连接" : "已断开")}");
        }

        private void DisconnectLo()
        {
            if (_loGenerator == null) return;
            try
            {
                try { _loGenerator.ConnectionChanged -= OnLoConnectionChanged; } catch { }
                _loGenerator.Disconnect();
                Logger.Log("[UI] LO signal generator disconnected.");
            }
            catch (Exception ex)
            {
                Logger.Log("[Error] LO 断开失败: " + ex.Message);
            }
            finally
            {
                try { _loGenerator?.Dispose(); } catch { }
                _loGenerator = null;
                if (pnlLoStatus != null && !pnlLoStatus.IsDisposed)
                {
                    pnlLoStatus.BackColor = StatusColorDisconnected;
                }
            }
        }

        private bool TryConnectSa()
        {
            // 已"声称连接"成功 → 先 *IDN? 探测,失败按失联处理走重连（参见 TryConnectRf 注释）
            if (_spectrumAnalyzer != null && _spectrumAnalyzer.IsConnected)
            {
                if (PingInstrument(_spectrumAnalyzer, "频谱仪"))
                {
                    return true;
                }
                Logger.Log("[UI] 频谱仪 *IDN? 探测失败,按失联处理走重连。");
                DisconnectSa();
            }
            if (string.IsNullOrWhiteSpace(txtSaAddress.Text))
            {
                pnlSaStatus.BackColor = StatusColorDisconnected;
                Logger.Log("[Error] 频谱仪连接失败: 地址为空。");
                return false;
            }
            try
            {
                _spectrumAnalyzer = new SpectrumAnalyzer(txtSaAddress.Text.Trim());
                _spectrumAnalyzer.ConnectionChanged += OnSaConnectionChanged;
                _spectrumAnalyzer.Connect();
                pnlSaStatus.BackColor = StatusColorConnected;
                Logger.Log("[UI] Spectrum analyzer connected.");
                return true;
            }
            catch (Exception ex)
            {
                pnlSaStatus.BackColor = StatusColorDisconnected;
                if (_spectrumAnalyzer != null)
                {
                    try { _spectrumAnalyzer.ConnectionChanged -= OnSaConnectionChanged; } catch { }
                    try { _spectrumAnalyzer.Dispose(); } catch { }
                }
                _spectrumAnalyzer = null;
                Logger.Log("[Error] 频谱仪连接失败: " + ex.Message);
                return false;
            }
        }

        /// <summary>频谱仪 ConnectionChanged 回调（参见 OnRfConnectionChanged 注释）。</summary>
        private void OnSaConnectionChanged(bool isConnected)
        {
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action<bool>(OnSaConnectionChanged), isConnected); }
                catch { /* 控件已销毁 */ }
                return;
            }
            if (pnlSaStatus != null && !pnlSaStatus.IsDisposed)
            {
                pnlSaStatus.BackColor = isConnected ? StatusColorConnected : StatusColorDisconnected;
            }
            Logger.Log($"[UI] 频谱仪连接状态变化: {(isConnected ? "已连接" : "已断开")}");
        }

        private void DisconnectSa()
        {
            if (_spectrumAnalyzer == null) return;
            try
            {
                try { _spectrumAnalyzer.ConnectionChanged -= OnSaConnectionChanged; } catch { }
                _spectrumAnalyzer.Disconnect();
                Logger.Log("[UI] Spectrum analyzer disconnected.");
            }
            catch (Exception ex)
            {
                Logger.Log("[Error] 频谱仪断开失败: " + ex.Message);
            }
            finally
            {
                try { _spectrumAnalyzer?.Dispose(); } catch { }
                _spectrumAnalyzer = null;
                if (pnlSaStatus != null && !pnlSaStatus.IsDisposed)
                {
                    pnlSaStatus.BackColor = StatusColorDisconnected;
                }
                // 断连时复位频谱仪 Toggle 按钮本地状态与按钮文字
                ResetSaToggleStates();
            }
        }

        /// <summary>
        /// 复位 2 个频谱仪 Toggle 按钮（最大保持 / 峰值自动搜索）的本地状态与按钮文字。
        /// 仅修改 UI 状态，不发 SCPI 命令——仪表已断连。
        /// </summary>
        private void ResetSaToggleStates()
        {
            _saMaxHoldOn = false;
            _saPeakAutoOn = false;
            _saTraceAvgOn = false;
            _saMinHoldOn = false;
            if (btnSaMaxHold != null) btnSaMaxHold.Text = "最大保持";
            if (btnSaPeakAuto != null) btnSaPeakAuto.Text = "峰值自动搜索";
            if (btnSaTraceAvg != null) btnSaTraceAvg.Text = "轨迹平均";
            if (btnSaMinHold != null) btnSaMinHold.Text = "轨迹最小保持";
        }

        private bool TryConnectPower()
        {
            // 已"声称连接"成功 → 先 *IDN? 探测,失败按失联处理走重连（参见 TryConnectRf 注释）
            if (_powerSupply != null && _powerSupply.IsConnected)
            {
                if (PingInstrument(_powerSupply, "电源"))
                {
                    return true;
                }
                Logger.Log("[UI] 电源 *IDN? 探测失败,按失联处理走重连。");
                DisconnectPower();
            }
            if (string.IsNullOrWhiteSpace(txtPowerAddress.Text))
            {
                pnlPowerStatus.BackColor = StatusColorDisconnected;
                Logger.Log("[Error] 电源连接失败: 地址为空。");
                return false;
            }
            try
            {
                _powerSupply = new ODP3063(txtPowerAddress.Text.Trim());
                _powerSupply.ConnectionChanged += OnPowerConnectionChanged;
                _powerSupply.Connect();
                pnlPowerStatus.BackColor = StatusColorConnected;
                // 连接后查询 CH2 实际输出状态，按真实状态同步按钮文字与本地状态。
                // 读取失败则按默认 OFF 处理 + 日志告警，绝不让读取异常破坏"已连接"的成果。
                SyncPowerButtonFromInstrument();
                Logger.Log("[UI] ODP3063 power supply connected.");
                return true;
            }
            catch (Exception ex)
            {
                pnlPowerStatus.BackColor = StatusColorDisconnected;
                if (_powerSupply != null)
                {
                    try { _powerSupply.ConnectionChanged -= OnPowerConnectionChanged; } catch { }
                    try { _powerSupply.Dispose(); } catch { }
                }
                _powerSupply = null;
                Logger.Log("[Error] 电源连接失败: " + ex.Message);
                return false;
            }
        }

        /// <summary>电源 ConnectionChanged 回调（参见 OnRfConnectionChanged 注释）。</summary>
        private void OnPowerConnectionChanged(bool isConnected)
        {
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action<bool>(OnPowerConnectionChanged), isConnected); }
                catch { /* 控件已销毁 */ }
                return;
            }
            if (pnlPowerStatus != null && !pnlPowerStatus.IsDisposed)
            {
                pnlPowerStatus.BackColor = isConnected ? StatusColorConnected : StatusColorDisconnected;
            }
            Logger.Log($"[UI] 电源连接状态变化: {(isConnected ? "已连接" : "已断开")}");
        }

        /// <summary>
        /// 连接电源后调用：读取 CH2 实际输出状态，同步 <see cref="btnOpenPower"/> 文字
        /// 与 <see cref="_powerOutputOn"/> 本地状态。
        /// 接受 "1"/"0"、"ON"/"OFF"（大小写不敏感、含空白也容忍）。
        /// 读取失败 → 按默认 OFF 处理 + 日志告警，不抛异常、不弹窗。
        /// </summary>
        private void SyncPowerButtonFromInstrument()
        {
            bool on = false;
            try
            {
                string raw = _powerSupply.QueryChannel2OutputStatus();
                Logger.Log($"[UI] 电源 CH2 输出状态原始返回: \"{raw}\"");
                string trimmed = raw == null ? string.Empty : raw.Trim();
                on = trimmed == "1"
                     || string.Equals(trimmed, "ON", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                Logger.Log("[Warn] 读取电源输出状态失败，按默认 OFF 处理: " + ex.Message);
                on = false;
            }

            _powerOutputOn = on;
            btnOpenPower.Text = on ? "关闭供电" : "开启供电";
        }

        private void DisconnectPower()
        {
            if (_powerSupply == null) return;
            try
            {
                try { _powerSupply.ConnectionChanged -= OnPowerConnectionChanged; } catch { }
                _powerSupply.Disconnect();
                Logger.Log("[UI] ODP3063 power supply disconnected.");
            }
            catch (Exception ex)
            {
                Logger.Log("[Error] 电源断开失败: " + ex.Message);
            }
            finally
            {
                try { _powerSupply?.Dispose(); } catch { }
                _powerSupply = null;
                if (pnlPowerStatus != null && !pnlPowerStatus.IsDisposed)
                {
                    pnlPowerStatus.BackColor = StatusColorDisconnected;
                }
                // 下次连接仍按默认 OFF 处理，按钮恢复对应文字
                _powerOutputOn = false;
                if (btnOpenPower != null && !btnOpenPower.IsDisposed)
                {
                    btnOpenPower.Text = "开启供电";
                }
            }
        }

        /// <summary>
        /// 一键连接：RF → LO → 频谱仪 → 电源。单台失败只记日志并继续，全程不弹窗。
        /// </summary>
        private void OnConnectAllClicked(object sender, EventArgs e)
        {
            Logger.Log("[UI] ===== 一键连接开始（RF → LO → 频谱仪 → 电源）=====");
            bool r1 = TryConnectRf();
            bool r2 = TryConnectLo();
            bool r3 = TryConnectSa();
            bool r4 = TryConnectPower();
            int ok = (r1 ? 1 : 0) + (r2 ? 1 : 0) + (r3 ? 1 : 0) + (r4 ? 1 : 0);
            Logger.Log($"[UI] ===== 一键连接完成：成功/已连接 {ok} 台，失败 {4 - ok} 台（详见上方日志）=====");
        }

        /// <summary>
        /// 一键断开：电源正在供电时弹窗阻止；否则依次断开 4 台（电源最后）。
        /// 不涉及接收机/矩阵串口（属于"串口连接"组）。
        /// </summary>
        private void OnDisconnectAllClicked(object sender, EventArgs e)
        {
            if (_powerOutputOn)
            {
                MessageBox.Show(this,
                    "电源当前正在供电输出，请先点击“关闭供电”完成关机后再执行一键断开。",
                    "操作被阻止", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Logger.Log("[UI] ===== 一键断开开始 =====");
            DisconnectRf();
            DisconnectLo();
            DisconnectSa();
            DisconnectPower();
            Logger.Log("[UI] ===== 一键断开完成 =====");
        }

        // ============================================================
        // 仪表活性探测（用于线缆检测场景）
        // ============================================================

        /// <summary>
        /// 对已"声称连接"的仪表发一条只读 SCPI（<c>*IDN?</c>）确认 session 还活着。
        /// <para>线缆掉了的时候 <see cref="VisaBaseInstrument.IsConnected"/> 字段不会自己变 false，
        /// 但发命令会立刻抛 <see cref="Ivi.Visa.VisaException"/>，借此判断 session 是否已失联。</para>
        /// <para>返回 true = session 健康，可复用；返回 false = session 失联，
        /// 调用方应当走 <c>Disconnect*</c> 释放旧对象、走完整重连流程。</para>
        /// <para>任何异常都被吞掉并返回 false，绝不向上抛——这是 UI 层"探测 + 决定是否复用"的判定,
        /// 真实异常会由后续重连流程统一报给用户。</para>
        /// </summary>
        private static bool PingInstrument(VisaBaseInstrument instrument, string tag)
        {
            if (instrument == null || !instrument.IsConnected)
            {
                return false;
            }
            try
            {
                string resp = instrument.QueryString("*IDN?");
                if (string.IsNullOrWhiteSpace(resp))
                {
                    Logger.Log($"[UI] {tag} *IDN? 返回空响应,视为失联。");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log($"[UI] {tag} *IDN? 探测失败(可能已掉线): {ex.Message}");
                return false;
            }
        }

        // ============================================================
        // 供电联动辅助方法
        // ============================================================

        /// <summary>
        /// 对单台信号源关闭通用调制（:MOD:STAT 0）与脉冲调制（:PULM:STAT 0）。
        /// 仪表连接成功后的附加保险动作：失败只记日志，不弹窗、不影响连接状态。
        /// </summary>
        private static void DisableModulationQuiet(string tag, Action modOff, Action pulseOff)
        {
            try
            {
                modOff();
                Logger.Log($"[UI] {tag} 信号源调制输出已关闭（:MOD:STAT 0）。");
            }
            catch (Exception ex)
            {
                Logger.Log($"[UI] {tag} 信号源关闭通用调制失败: {ex.Message}");
            }

            try
            {
                pulseOff();
                Logger.Log($"[UI] {tag} 信号源脉冲调制已关闭（:PULM:STAT 0）。");
            }
            catch (Exception ex)
            {
                Logger.Log($"[UI] {tag} 信号源关闭脉冲调制失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 批量开/关 RF、LO 射频输出（:OUTP 1/0）。逐台独立容错：
        /// 未连接则跳过并记日志，单台失败不影响另一台。
        /// </summary>
        private void SetSignalOutputs(bool on)
        {
            string action = on ? "开启" : "关闭";

            if (_rfGenerator == null || !_rfGenerator.IsConnected)
            {
                Logger.Log($"[UI] RF 信号源未连接，跳过{action}射频输出。");
            }
            else
            {
                try
                {
                    _rfGenerator.SetOutputState(on);
                    Logger.Log($"[UI] RF 信号源射频输出已{action}（:OUTP {(on ? 1 : 0)}）。");
                }
                catch (Exception ex)
                {
                    Logger.Log($"[UI] RF 信号源{action}射频输出失败: {ex.Message}");
                }
            }

            if (_loGenerator == null || !_loGenerator.IsConnected)
            {
                Logger.Log($"[UI] LO 信号源未连接，跳过{action}射频输出。");
            }
            else
            {
                try
                {
                    _loGenerator.SetOutputState(on);
                    Logger.Log($"[UI] LO 信号源射频输出已{action}（:OUTP {(on ? 1 : 0)}）。");
                }
                catch (Exception ex)
                {
                    Logger.Log($"[UI] LO 信号源{action}射频输出失败: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 开机序列使用：按面板下拉（cmbReceiverPort/cmbReceiverBaud）自动连接接收机串口。
        /// 已连接则跳过；端口无效或打开失败则弹窗提示并返回 false（调用方应中止开机序列）。
        /// </summary>
        private bool TryAutoConnectReceiver()
        {
            if (_receiver != null && _receiver.IsOpen)
            {
                Logger.Log("[UI] 接收机串口已连接，开机序列跳过自动连接。");
                return true;
            }

            string portName = cmbReceiverPort.SelectedItem == null ? null : cmbReceiverPort.SelectedItem.ToString();
            int baudRate;
            try
            {
                baudRate = int.Parse(cmbReceiverBaud.Text);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "接收机波特率无效: " + ex.Message, "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            if (string.IsNullOrEmpty(portName) || portName == "(无)")
            {
                MessageBox.Show(this, "请先选择有效的接收机串口。（电源已上电，信号输出未开启）", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            try
            {
                _receiver = new ReceiverSerialPort();
                _receiver.Open(portName, baudRate);
                Logger.Log($"[UI] 开机序列自动连接接收机串口 {portName} @ {baudRate}。");
                return true;
            }
            catch (Exception ex)
            {
                _receiver = null;
                Logger.Log("[Error] 开机序列自动连接接收机串口失败: " + ex.Message);
                MessageBox.Show(this,
                    "自动连接接收机串口失败: " + ex.Message + "\n（电源已上电，信号输出未开启，可手动补连或关闭供电后重试）",
                    "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        /// <summary>
        /// 关机序列使用：尽力断开接收机串口，异常只记日志不弹窗，确保关机流程继续。
        /// </summary>
        private void BestEffortDisconnectReceiver()
        {
            if (_receiver == null)
            {
                return;
            }

            try
            {
                _receiver.Close();
                Logger.Log("[UI] 关机序列已断开接收机串口。");
            }
            catch (Exception ex)
            {
                Logger.Log("[Error] 关机序列断开接收机串口失败: " + ex.Message);
            }
            finally
            {
                _receiver = null;
            }
        }

        // -------- 电源输出开关（联动接收机串口与 RF/LO 射频输出）
        // 开机：电源 ON → 自动连接收机串口（失败中止，电源保持上电）→ RF/LO :OUTP 1
        // 关机：RF/LO :OUTP 0 → 断开接收机串口 → 电源 OFF（各步尽力执行）
        private void OnOpenPowerClicked(object sender, EventArgs e)
        {
            if (_powerSupply == null || !_powerSupply.IsConnected)
            {
                MessageBox.Show(this, "请先连接电源。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!_powerOutputOn)
            {
                // -------- 开机序列 --------
                // 1) 电源上电；失败则中止，本地状态保持 OFF
                try
                {
                    _powerSupply.EnableChannel2Output(true);
                }
                catch (Exception ex)
                {
                    Logger.Log("[Error] 电源开启失败: " + ex.Message);
                    MessageBox.Show(this, "电源开启失败: " + ex.Message, "错误",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                _powerOutputOn = true;
                btnOpenPower.Text = "关闭供电";
                Logger.Log("[UI] Power output enabled.");

                // 2) 自动连接收机串口；失败中止，电源保持上电、信号输出不开启
                if (!TryAutoConnectReceiver())
                {
                    return;
                }

                // 3) 开启 RF/LO 射频输出（未连接逐台跳过）
                SetSignalOutputs(true);
            }
            else
            {
                // -------- 关机序列（各步尽力执行） --------
                // 1) 关闭 RF/LO 射频输出
                SetSignalOutputs(false);

                // 2) 断开接收机串口
                BestEffortDisconnectReceiver();

                // 3) 电源断电；VISA 命令异常仅记日志 + 弹窗，本地状态走 finally 强制复位，
                //    避免仪表掉线后 _powerOutputOn 卡在 true 导致"一键断开"被永久阻塞。
                try
                {
                    _powerSupply.EnableChannel2Output(false);
                    Logger.Log("[UI] Power output disabled.");
                }
                catch (Exception ex)
                {
                    Logger.Log("[Error] 电源关闭失败（检查线缆连接状态）: " + ex.Message);
                    MessageBox.Show(this,
                        "电源关闭失败(检查线缆连接状态),本地状态已强制复位,请检查连接后按一键断开",
                        "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    // 无论 VISA 命令成功还是失败，本地 UI 状态都强制复位到 OFF：
                    //   - 仪表确实 OFF:状态正确;
                    //   - 仪表掉线/通信失败:状态也复位,允许后续"一键断开"重新走一遍清理路径,
                    //     不会再卡死在"必须先关闭供电"。
                    _powerOutputOn = false;
                    if (btnOpenPower != null && !btnOpenPower.IsDisposed)
                    {
                        btnOpenPower.Text = "开启供电";
                    }
                }
            }
        }

        // -------- 日志清除按钮
        // -------- 频谱仪快捷按钮（仪表控制区 / grpInstrumentControl）
        //
        // 约定：
        // - 所有方法先检查 _spectrumAnalyzer 是否已 Connect，未连接弹窗提示，不抛异常、不发命令。
        // - Toggle 类按钮（最大保持 / 峰值自动搜索）维护本地状态变量 + 按钮文字，
        //   仪表断连时由 DisconnectSa 统一复位。

        /// <summary>未连接频谱仪时弹窗提示，返回 false；调用方应中止操作。</summary>
        private bool EnsureSpectrumAnalyzerConnected()
        {
            if (_spectrumAnalyzer != null && _spectrumAnalyzer.IsConnected)
            {
                return true;
            }
            MessageBox.Show(this, "请先在『仪表连接状态』区连接频谱仪。", "提示",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        /// <summary>峰值搜索：marker1 启 → max-peak → 读 Y，打到日志。</summary>
        private void OnSaPeakSearchClicked(object sender, EventArgs e)
        {
            if (!EnsureSpectrumAnalyzerConnected()) return;
            try
            {
                double peakDbm = _spectrumAnalyzer.MeasureMarkerPeak();
                Logger.Log($"[UI] 峰值搜索结果：{peakDbm:F3} dBm");
            }
            catch (Exception ex)
            {
                Logger.Log("[UI] 峰值搜索失败: " + ex.Message);
                MessageBox.Show(this, "峰值搜索失败: " + ex.Message, "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>关闭所有标记（:CALC:MARK1:AOFF）。</summary>
        private void OnSaMarkerAllOffClicked(object sender, EventArgs e)
        {
            if (!EnsureSpectrumAnalyzerConnected()) return;
            try
            {
                _spectrumAnalyzer.SetAllMarkOFF();
                Logger.Log("[UI] 频谱仪已关闭所有标记");
            }
            catch (Exception ex)
            {
                Logger.Log("[UI] 关闭标记失败: " + ex.Message);
                MessageBox.Show(this, "关闭标记失败: " + ex.Message, "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>频谱仪复位（*RST + *CLS）。</summary>
        private void OnSaResetClicked(object sender, EventArgs e)
        {
            if (!EnsureSpectrumAnalyzerConnected()) return;
            try
            {
                _spectrumAnalyzer.Reset();
                // 复位后仪表回到默认（关闭最大保持 / 关闭峰值自动搜索），
                // 本地 Toggle 状态同步复位
                _saMaxHoldOn = false;
                btnSaMaxHold.Text = "最大保持";
                _saPeakAutoOn = false;
                btnSaPeakAuto.Text = "峰值自动搜索";
                _saTraceAvgOn = false;
                btnSaTraceAvg.Text = "轨迹平均";
                _saMinHoldOn = false;
                btnSaMinHold.Text = "轨迹最小保持";

                // 复位后立即开启连续扫描（*RST 默认是单次，手动恢复为连续模式）。
                // 注：UI 上当前未保留“连续扫描”按钮与本地 Toggle 状态，
                // 因此此处只发 SCPI 命令、不修改任何字段。
                _spectrumAnalyzer.SetContinuousScan(true);
                Logger.Log("[UI] 频谱仪复位后已开启连续扫描");
            }
            catch (Exception ex)
            {
                Logger.Log("[UI] 频谱仪复位失败: " + ex.Message);
                MessageBox.Show(this, "频谱仪复位失败: " + ex.Message, "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>最大保持 Toggle。点击切换 MAXH / WRIT，按钮文字同步。</summary>
        private void OnSaMaxHoldClicked(object sender, EventArgs e)
        {
            if (!EnsureSpectrumAnalyzerConnected()) return;
            bool next = !_saMaxHoldOn;
            try
            {
                _spectrumAnalyzer.SetMaxHold(next);
                _saMaxHoldOn = next;
                btnSaMaxHold.Text = next ? "最大保持:开" : "最大保持";
                Logger.Log($"[UI] 频谱仪最大保持：{(next ? "开" : "关")}");
            }
            catch (Exception ex)
            {
                Logger.Log("[UI] 设置最大保持失败: " + ex.Message);
                MessageBox.Show(this, "设置最大保持失败: " + ex.Message, "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>峰值自动搜索 Toggle。点击切换 CALC:MARK:CPS，按钮文字同步。</summary>
        private void OnSaPeakAutoClicked(object sender, EventArgs e)
        {
            if (!EnsureSpectrumAnalyzerConnected()) return;
            bool next = !_saPeakAutoOn;
            try
            {
                _spectrumAnalyzer.SetPeakAutoSearch(next);
                _saPeakAutoOn = next;
                btnSaPeakAuto.Text = next ? "峰值自动:开" : "峰值自动搜索";
                Logger.Log($"[UI] 频谱仪峰值自动搜索：{(next ? "开" : "关")}");
            }
            catch (Exception ex)
            {
                Logger.Log("[UI] 设置峰值自动搜索失败: " + ex.Message);
                MessageBox.Show(this, "设置峰值自动搜索失败: " + ex.Message, "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>RBW/VBW 一键设自动（:BWID:RES:AUTO ON + :BWID:VID:AUTO ON）。</summary>
        private void OnSaBwAutoClicked(object sender, EventArgs e)
        {
            if (!EnsureSpectrumAnalyzerConnected()) return;
            try
            {
                _spectrumAnalyzer.SetResolutionBandwidthAuto();
                _spectrumAnalyzer.SetVideoBandwidthAuto();
                Logger.Log("[UI] 频谱仪 RBW/VBW 已设为自动");
            }
            catch (Exception ex)
            {
                Logger.Log("[UI] 设置 RBW/VBW 自动失败: " + ex.Message);
                MessageBox.Show(this, "设置 RBW/VBW 自动失败: " + ex.Message, "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>轨迹平均模式切换（:TRAC:TYPE AVER / WRIT）。Toggle，按钮文字在"轨迹平均"与"轨迹平均:开"之间切换。</summary>
        private void OnSaTraceAvgClicked(object sender, EventArgs e)
        {
            if (!EnsureSpectrumAnalyzerConnected()) return;
            bool next = !_saTraceAvgOn;
            try
            {
                _spectrumAnalyzer.SetAverageMode(next);
                _saTraceAvgOn = next;
                btnSaTraceAvg.Text = next ? "轨迹平均:开" : "轨迹平均";
                Logger.Log($"[UI] 频谱仪轨迹平均模式：{(next ? "开" : "关")}");
            }
            catch (Exception ex)
            {
                Logger.Log("[UI] 设置轨迹平均失败: " + ex.Message);
                MessageBox.Show(this, "设置轨迹平均失败: " + ex.Message, "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>轨迹最小保持 Toggle（:TRACE1:MODE MINH / WRIT）。与最大保持对称，按钮文字同步。</summary>
        private void OnSaMinHoldClicked(object sender, EventArgs e)
        {
            if (!EnsureSpectrumAnalyzerConnected()) return;
            bool next = !_saMinHoldOn;
            try
            {
                _spectrumAnalyzer.SetMinHold(next);
                _saMinHoldOn = next;
                btnSaMinHold.Text = next ? "轨迹最小保持:开" : "轨迹最小保持";
                Logger.Log($"[UI] 频谱仪轨迹最小保持：{(next ? "开" : "关")}");
            }
            catch (Exception ex)
            {
                Logger.Log("[UI] 设置轨迹最小保持失败: " + ex.Message);
                MessageBox.Show(this, "设置轨迹最小保持失败: " + ex.Message, "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// 开启差值标记（:CALC:MARK1:MODE DELTA），不读 Y?、不设 X 频率。
        /// <para>调试便捷按钮：切到 Delta 模式后直接看频谱仪屏幕。
        /// 关闭标记请用"关闭标记"按钮。</para>
        /// </summary>
        private void OnSaDeltaMarkerClicked(object sender, EventArgs e)
        {
            if (!EnsureSpectrumAnalyzerConnected()) return;
            try
            {
                _spectrumAnalyzer.EnableDeltaMarker();
                Logger.Log("[UI] 频谱仪已开启差值标记");
            }
            catch (Exception ex)
            {
                Logger.Log("[UI] 开启差值标记失败: " + ex.Message);
                MessageBox.Show(this, "开启差值标记失败: " + ex.Message, "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// 进入谐波测试模式（:INITiate:HARMonics），不读幅度结果。
        /// <para>调试便捷按钮：进入谐波模式后直接看频谱仪屏幕。
        /// 测量结果记录请在自动测试界面写脚本。</para>
        /// </summary>
        private void OnSaHarmonicsClicked(object sender, EventArgs e)
        {
            if (!EnsureSpectrumAnalyzerConnected()) return;
            try
            {
                _spectrumAnalyzer.InitializeHarmonicsTest();
                Logger.Log("[UI] 频谱仪已进入谐波测试模式");
            }
            catch (Exception ex)
            {
                Logger.Log("[UI] 进入谐波测试模式失败: " + ex.Message);
                MessageBox.Show(this, "进入谐波测试模式失败: " + ex.Message, "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// 参考电平 +5 dBm。
        /// <para>读到当前参考电平（:DISP:WIND:TRAC:Y:RLEV?）+ 5 dBm 后再下发设置。
        /// 读失败或设置失败仅记日志、不弹窗，避免打断调试。</para>
        /// </summary>
        private void OnSaRefLevelUpClicked(object sender, EventArgs e)
        {
            AdjustReferenceLevel(+5.0);
        }

        /// <summary>
        /// 参考电平 -5 dBm。
        /// <para>读到当前参考电平（:DISP:WIND:TRAC:Y:RLEV?）- 5 dBm 后再下发设置。
        /// 读失败或设置失败仅记日志、不弹窗，避免打断调试。</para>
        /// </summary>
        private void OnSaRefLevelDownClicked(object sender, EventArgs e)
        {
            AdjustReferenceLevel(-5.0);
        }

        /// <summary>
        /// 通用"参考电平按步进调整"辅助：读当前 → 叠加步进 → 设置。
        /// 所有异常仅记日志，不弹窗、不改变本地状态。
        /// </summary>
        private void AdjustReferenceLevel(double stepDbm)
        {
            if (!EnsureSpectrumAnalyzerConnected()) return;
            try
            {
                double current = _spectrumAnalyzer.QueryReferenceLevel();
                double next = current + stepDbm;
                _spectrumAnalyzer.SetReferenceLevel(next);
                Logger.Log(
                    $"[UI] 参考电平已调整：{current:F2} dBm → {next:F2} dBm（步进 {stepDbm:+0.0;-0.0;0} dBm）");
            }
            catch (Exception ex)
            {
                Logger.Log($"[UI] 参考电平调整失败（步进 {stepDbm:+0.0;-0.0;0} dBm）：{ex.Message}");
            }
        }

        // -------- 日志清除按钮
        private void OnBtnLogClearClicked(object sender, EventArgs e)
        {
            if (rtbLog != null && !rtbLog.IsDisposed)
            {
                rtbLog.Clear();
            }
        }

        // -------- 频段下拉框选中变化：隐式切换 RF/LO 信号源频率
        /// <summary>
        /// 频段下拉框选中变化时，自动切换 RF/LO 信号源频率（隐式）。
        /// <para>仅切换频率，不动功率；十六进制命令仍由"设置"按钮（btnBandSet）触发。</para>
        /// </summary>
        private void OnBandSelectionChanged(object sender, EventArgs e)
        {
            // 初始化阶段不触发（InitializeDefaults 内 RefreshBandCombo 自动选中第一项时不切）
            if (!_initialized) return;

            // 信号源未连接 → 静默跳过 + 日志
            if (_rfGenerator == null || !_rfGenerator.IsConnected)
            {
                Logger.Log("[UI] RF 信号源未连接，跳过隐式频率切换");
                return;
            }
            if (_loGenerator == null || !_loGenerator.IsConnected)
            {
                Logger.Log("[UI] LO 信号源未连接，跳过隐式频率切换");
                return;
            }

            // 取当前选中的频段
            if (cmbBand.SelectedIndex < 0 || _currentBands == null
                || cmbBand.SelectedIndex >= _currentBands.Count)
            {
                Logger.Log("[UI] 频段选择无效，跳过隐式频率切换");
                return;
            }

            var band = _currentBands[cmbBand.SelectedIndex];
            string displayName = band.DisplayName;
            if (!TryExtractMHzFromDisplayName(displayName, out double rfFreqMHz))
            {
                Logger.Log($"[UI] 无法从频段名称 '{displayName}' 提取频率，跳过隐式切换");
                return;
            }

            double rfFreqHz = rfFreqMHz * 1_000_000.0;

            // 根据当前模式计算 LO 频率
            ReceiverMode currentMode;
            if (rbMode1.Checked) currentMode = ReceiverMode.Mode1;
            else if (rbMode2.Checked) currentMode = ReceiverMode.Mode2;
            else currentMode = ReceiverMode.Mode3;

            double loFreqHz = LoFrequencyRule.Calculate(rfFreqHz, (int)currentMode);

            // 切换信号源频率（只切频率，不动功率；各自独立异常处理）
            try
            {
                _rfGenerator.SetFrequency(rfFreqHz);
                Logger.Log($"[UI] 射频信号源已切换到 {rfFreqMHz:F3} MHz");
            }
            catch (Exception ex)
            {
                Logger.Log($"[UI] RF 频率切换失败: {ex.Message}");
            }

            try
            {
                _loGenerator.SetFrequency(loFreqHz);
                double loFreqMHz = loFreqHz / 1_000_000.0;
                Logger.Log($"[UI] 本振信号源已切换到 {loFreqMHz:F3} MHz");
            }
            catch (Exception ex)
            {
                Logger.Log($"[UI] LO 频率切换失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 从频段显示名称中提取 MHz 数值（如 "频段108" → 108）。
        /// </summary>
        private static bool TryExtractMHzFromDisplayName(string displayName, out double mhz)
        {
            mhz = 0;
            if (string.IsNullOrEmpty(displayName)) return false;
            string numberPart = displayName.Replace("频段", "").Trim();
            return double.TryParse(numberPart, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out mhz);
        }

        // -------- 日志回调
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
    }
}
