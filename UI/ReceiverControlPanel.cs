using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    /// <para>完全代码生成，无 .Designer.cs / .resx。</para>
    /// </summary>
    public class ReceiverControlPanel : UserControl
    {
        // ============================================================
        // 控件字段（按区域分组）
        // ============================================================

        // ---- 串口连接区
        private ComboBox _cmbReceiverPort;
        private ComboBox _cmbReceiverBaud;
        private Button _btnReceiverConnect;
        private Button _btnReceiverDisconnect;
        private ComboBox _cmbMatrixPort;
        private ComboBox _cmbMatrixBaud;
        private Button _btnMatrixConnect;
        private Button _btnMatrixDisconnect;

        // ---- 参数配置区
        private RadioButton _rbTempNormal;
        private RadioButton _rbTempHigh;
        private RadioButton _rbTempLow;
        private Button _btnTempSet;
        private ComboBox _cmbChannel;
        private RadioButton _rbMode1;
        private RadioButton _rbMode2;
        private RadioButton _rbMode3;
        private Button _btnModeSet;
        private ComboBox _cmbBand;
        private Button _btnBandSet;
        private IReadOnlyList<ModeBandTable.BandInfo> _currentBands;
        private NumericUpDown _numAgcAtten;
        private Button _btnAgcSet;

        // ---- 仪表状态区
        private Panel _pnlRfStatus;
        private TextBox _txtRfAddress;
        private Button _btnRfConnect;
        private Button _btnRfDisconnect;
        private Panel _pnlLoStatus;
        private TextBox _txtLoAddress;
        private Button _btnLoConnect;
        private Button _btnLoDisconnect;
        private Panel _pnlSaStatus;
        private TextBox _txtSaAddress;
        private Button _btnSaConnect;
        private Button _btnSaDisconnect;

        // ---- 通信日志区
        private RichTextBox _rtbLog;
        private Button _btnLogClear;

        // ============================================================
        // 业务对象（M5 阶段内部创建；M6/真机阶段可改为 DI 注入）
        // ============================================================
        private ReceiverSerialPort _receiver;
        private ChannelLink _channelLink;
        private readonly MatrixSerialPort _matrix = new MatrixSerialPort();

        private SignalGenerator _rfGenerator;
        private SignalGeneratorLO _loGenerator;
        private SpectrumAnalyzer _spectrumAnalyzer;

        // 抑制初始化阶段事件回调（避免在构建控件时立刻触发副作用）
        private bool _initialized;

        // 仪表状态颜色常量
        private static readonly Color StatusColorDisconnected = Color.Red;
        private static readonly Color StatusColorConnected = Color.LimeGreen;

        // 波特率下拉默认值
        private static readonly int[] BaudRateOptions = { 9600, 19200, 38400, 57600, 115200 };

        // ============================================================
        // 构造与生命周期
        // ============================================================

        public ReceiverControlPanel()
        {
            Dock = DockStyle.Fill;
            AutoScaleMode = AutoScaleMode.Font;
            BuildUi();
            InitializeDefaults();
            WireEvents();
            _initialized = true;

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

                // 关闭业务资源
                try { _receiver?.Close(); } catch { }
                try { _receiver?.Dispose(); } catch { }
                try { _matrix?.Disconnect(); } catch { }
                try { _rfGenerator?.Dispose(); } catch { }
                try { _loGenerator?.Dispose(); } catch { }
                try { _spectrumAnalyzer?.Dispose(); } catch { }
            }
            base.Dispose(disposing);
        }

        // ============================================================
        // UI 构建
        // ============================================================

        private void BuildUi()
        {
            // 顶层布局：4 行 1 列，前三行 AutoSize，最后一行 Fill
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                AutoSize = false,
                Padding = new Padding(8)
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            root.Controls.Add(BuildSerialGroup(),       0, 0);
            root.Controls.Add(BuildParameterGroup(),    0, 1);
            root.Controls.Add(BuildInstrumentGroup(),   0, 2);
            root.Controls.Add(BuildLogGroup(),          0, 3);

            Controls.Add(root);
        }

        // ------------------------------------------------------------
        // 串口连接区
        // ------------------------------------------------------------
        private GroupBox BuildSerialGroup()
        {
            var group = new GroupBox
            {
                Text = "串口连接",
                Dock = DockStyle.Fill,
                Padding = new Padding(10, 5, 10, 10)
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 6,
                RowCount = 2,
                AutoSize = true
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 75F));

            // 接收机行
            var lblReceiver = new Label { Text = "接收机串口：", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill, AutoSize = false };
            _cmbReceiverPort = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            _cmbReceiverBaud = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            foreach (int b in BaudRateOptions) _cmbReceiverBaud.Items.Add(b.ToString());
            _btnReceiverConnect = new Button { Text = "连接", Dock = DockStyle.Fill };
            _btnReceiverDisconnect = new Button { Text = "断开", Dock = DockStyle.Fill };
            layout.Controls.Add(lblReceiver,            0, 0);
            layout.Controls.Add(_cmbReceiverPort,       1, 0);
            layout.Controls.Add(_cmbReceiverBaud,       2, 0);
            layout.Controls.Add(_btnReceiverConnect,    3, 0);
            layout.Controls.Add(_btnReceiverDisconnect, 4, 0);
            // 第 6 列：留空 stretch
            layout.Controls.Add(new Label(), 5, 0);

            // 矩阵行
            var lblMatrix = new Label { Text = "矩阵串口：", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill, AutoSize = false };
            _cmbMatrixPort = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            _cmbMatrixBaud = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            foreach (int b in BaudRateOptions) _cmbMatrixBaud.Items.Add(b.ToString());
            _btnMatrixConnect = new Button { Text = "连接", Dock = DockStyle.Fill };
            _btnMatrixDisconnect = new Button { Text = "断开", Dock = DockStyle.Fill };
            layout.Controls.Add(lblMatrix,           0, 1);
            layout.Controls.Add(_cmbMatrixPort,      1, 1);
            layout.Controls.Add(_cmbMatrixBaud,      2, 1);
            layout.Controls.Add(_btnMatrixConnect,   3, 1);
            layout.Controls.Add(_btnMatrixDisconnect,4, 1);
            layout.Controls.Add(new Label(), 5, 1);

            group.Controls.Add(layout);
            return group;
        }

        // ------------------------------------------------------------
        // 参数配置区
        // ------------------------------------------------------------
        private GroupBox BuildParameterGroup()
        {
            var group = new GroupBox
            {
                Text = "接收机参数配置",
                AutoSize = true,
                Dock = DockStyle.Top,
                Padding = new Padding(10, 5, 10, 10)
            };

            // 列：标签(90) | 主控件区(弹性) | 设置按钮(80) | 备注文本(弹性)
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 4,
                RowCount = 5,
                AutoSize = true
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220F));

            // ---- 第 1 行：温度
            _rbTempNormal = new RadioButton { Text = "常温", AutoSize = true };
            _rbTempHigh   = new RadioButton { Text = "高温", AutoSize = true };
            _rbTempLow    = new RadioButton { Text = "低温", AutoSize = true };
            _btnTempSet   = new Button { Text = "设置", Dock = DockStyle.Fill };
            var tempPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                WrapContents = false
            };
            tempPanel.Controls.AddRange(new Control[] { _rbTempNormal, _rbTempHigh, _rbTempLow });

            layout.Controls.Add(new Label { Text = "温度区间：", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            layout.Controls.Add(tempPanel, 1, 0);
            layout.Controls.Add(_btnTempSet, 2, 0);
            layout.Controls.Add(new Label(), 3, 0);

            // ---- 第 2 行：通道
            _cmbChannel = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            for (int ch = 1; ch <= 8; ch++) _cmbChannel.Items.Add("通道" + ch);

            layout.Controls.Add(new Label { Text = "通道选择：", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
            layout.Controls.Add(_cmbChannel, 1, 1);
            layout.Controls.Add(new Label(), 2, 1);
            layout.Controls.Add(new Label { Text = "（选择后自动联动矩阵切换）", AutoSize = true, ForeColor = Color.Gray }, 3, 1);

            // ---- 第 3 行：模式
            _rbMode1 = new RadioButton { Text = "Mode1", AutoSize = true };
            _rbMode2 = new RadioButton { Text = "Mode2", AutoSize = true };
            _rbMode3 = new RadioButton { Text = "Mode3", AutoSize = true };
            _btnModeSet = new Button { Text = "设置", Dock = DockStyle.Fill };
            var modePanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                WrapContents = false
            };
            modePanel.Controls.AddRange(new Control[] { _rbMode1, _rbMode2, _rbMode3 });

            layout.Controls.Add(new Label { Text = "模式选择：", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 2);
            layout.Controls.Add(modePanel, 1, 2);
            layout.Controls.Add(_btnModeSet, 2, 2);
            layout.Controls.Add(new Label(), 3, 2);

            // ---- 第 4 行：频段
            _cmbBand = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            _btnBandSet = new Button { Text = "设置", Dock = DockStyle.Fill };

            layout.Controls.Add(new Label { Text = "频段选择：", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 3);
            layout.Controls.Add(_cmbBand, 1, 3);
            layout.Controls.Add(_btnBandSet, 2, 3);
            layout.Controls.Add(new Label { Text = "（依当前模式动态填充）", AutoSize = true, ForeColor = Color.Gray }, 3, 3);

            // ---- 第 5 行：AGC 衰减
            _numAgcAtten = new NumericUpDown
            {
                Minimum = 0,
                Maximum = 60,
                Increment = 4,
                Value = 0,
                Dock = DockStyle.Left,
                Width = 80
            };
            _btnAgcSet = new Button { Text = "设置", Dock = DockStyle.Left };

            layout.Controls.Add(new Label { Text = "AGC 衰减：", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 4);
            var agcHost = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                WrapContents = false
            };
            agcHost.Controls.Add(_numAgcAtten);
            agcHost.Controls.Add(_btnAgcSet);
            agcHost.Controls.Add(new Label { Text = " dB（步进 4）", AutoSize = true, ForeColor = Color.Gray });
            layout.Controls.Add(agcHost, 1, 4);
            layout.Controls.Add(new Label(), 2, 4);
            layout.Controls.Add(new Label(), 3, 4);

            group.Controls.Add(layout);
            return group;
        }

        // ------------------------------------------------------------
        // 仪表状态区
        // ------------------------------------------------------------
        private GroupBox BuildInstrumentGroup()
        {
            var group = new GroupBox
            {
                Text = "仪表连接状态",
                AutoSize = true,
                Dock = DockStyle.Top,
                Padding = new Padding(10, 5, 10, 10)
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 5,
                RowCount = 3,
                AutoSize = true
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60F));   // 圆点
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80F));   // 名称
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90F));   // VISA 地址 label
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));   // 地址框
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150F));  // 连接+断开

            // Row 0: RF
            _pnlRfStatus    = CreateStatusDot();
            _txtRfAddress   = new TextBox { Text = "TCPIP0::192.168.1.10::INSTR", Dock = DockStyle.Fill };
            _btnRfConnect   = new Button { Text = "连接", Dock = DockStyle.Fill };
            _btnRfDisconnect = new Button { Text = "断开", Dock = DockStyle.Fill };
            var rfConnHost = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
            rfConnHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            rfConnHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            rfConnHost.Controls.Add(_btnRfConnect, 0, 0);
            rfConnHost.Controls.Add(_btnRfDisconnect, 1, 0);

            layout.Controls.Add(_pnlRfStatus, 0, 0);
            layout.Controls.Add(new Label { Text = "RF 信号源：", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 1, 0);
            layout.Controls.Add(new Label { Text = "VISA 地址：", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 2, 0);
            layout.Controls.Add(_txtRfAddress, 3, 0);
            layout.Controls.Add(rfConnHost, 4, 0);

            // Row 1: LO
            _pnlLoStatus    = CreateStatusDot();
            _txtLoAddress   = new TextBox { Text = "TCPIP0::192.168.1.11::INSTR", Dock = DockStyle.Fill };
            _btnLoConnect   = new Button { Text = "连接", Dock = DockStyle.Fill };
            _btnLoDisconnect = new Button { Text = "断开", Dock = DockStyle.Fill };
            var loConnHost = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
            loConnHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            loConnHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            loConnHost.Controls.Add(_btnLoConnect, 0, 0);
            loConnHost.Controls.Add(_btnLoDisconnect, 1, 0);

            layout.Controls.Add(_pnlLoStatus, 0, 1);
            layout.Controls.Add(new Label { Text = "LO 信号源：", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 1, 1);
            layout.Controls.Add(new Label { Text = "VISA 地址：", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 2, 1);
            layout.Controls.Add(_txtLoAddress, 3, 1);
            layout.Controls.Add(loConnHost, 4, 1);

            // Row 2: Spectrum
            _pnlSaStatus    = CreateStatusDot();
            _txtSaAddress   = new TextBox { Text = "TCPIP0::192.168.1.12::INSTR", Dock = DockStyle.Fill };
            _btnSaConnect   = new Button { Text = "连接", Dock = DockStyle.Fill };
            _btnSaDisconnect = new Button { Text = "断开", Dock = DockStyle.Fill };
            var saConnHost = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
            saConnHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            saConnHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            saConnHost.Controls.Add(_btnSaConnect, 0, 0);
            saConnHost.Controls.Add(_btnSaDisconnect, 1, 0);

            layout.Controls.Add(_pnlSaStatus, 0, 2);
            layout.Controls.Add(new Label { Text = "频谱仪：", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 1, 2);
            layout.Controls.Add(new Label { Text = "VISA 地址：", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 2, 2);
            layout.Controls.Add(_txtSaAddress, 3, 2);
            layout.Controls.Add(saConnHost, 4, 2);

            group.Controls.Add(layout);
            return group;
        }

        private static Panel CreateStatusDot()
        {
            return new Panel
            {
                BackColor = StatusColorDisconnected,
                Width = 16,
                Height = 16,
                Margin = new Padding(6),
                BorderStyle = BorderStyle.FixedSingle
            };
        }

        // ------------------------------------------------------------
        // 通信日志区
        // ------------------------------------------------------------
        private GroupBox BuildLogGroup()
        {
            var group = new GroupBox
            {
                Text = "通信日志",
                Dock = DockStyle.Fill,
                Padding = new Padding(10, 5, 10, 10)
            };

            var inner = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1
            };
            inner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            inner.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90F));

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

            // 清除按钮 + 顶部留白，借助 TableLayoutPanel 实现垂直居中（顶部 stretch + 底部固定高度按钮）
            var clearHost = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2
            };
            clearHost.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            clearHost.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            _btnLogClear = new Button { Text = "清除", Dock = DockStyle.Fill };
            clearHost.Controls.Add(new Label(), 0, 0);
            clearHost.Controls.Add(_btnLogClear, 0, 1);

            inner.Controls.Add(_rtbLog, 0, 0);
            inner.Controls.Add(clearHost, 1, 0);

            group.Controls.Add(inner);
            return group;
        }

        // ============================================================
        // 默认值 & 事件绑定
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
                    _cmbReceiverPort.Items.AddRange(ports);
                    _cmbMatrixPort.Items.AddRange(ports);
                    _cmbReceiverPort.SelectedIndex = 0;
                    _cmbMatrixPort.SelectedIndex = 0;
                }
                else
                {
                    _cmbReceiverPort.Items.Add("(无)");
                    _cmbMatrixPort.Items.Add("(无)");
                    _cmbReceiverPort.SelectedIndex = 0;
                    _cmbMatrixPort.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[UI] 获取串口列表失败：{ex.Message}");
            }

            // 波特率默认 9600
            int idx9600 = _cmbReceiverBaud.Items.IndexOf("9600");
            if (idx9600 >= 0) _cmbReceiverBaud.SelectedIndex = idx9600;
            idx9600 = _cmbMatrixBaud.Items.IndexOf("9600");
            if (idx9600 >= 0) _cmbMatrixBaud.SelectedIndex = idx9600;

            // 参数默认
            _rbTempNormal.Checked = true;
            _cmbChannel.SelectedIndex = 0;
            _rbMode1.Checked = true;
            // 触发频段下拉刷新
            RefreshBandCombo(ReceiverMode.Mode1);
            _numAgcAtten.Value = 0;

            // 仪表圆点红色
            _pnlRfStatus.BackColor = StatusColorDisconnected;
            _pnlLoStatus.BackColor = StatusColorDisconnected;
            _pnlSaStatus.BackColor = StatusColorDisconnected;
        }

        private void WireEvents()
        {
            // 串口
            _btnReceiverConnect.Click += OnReceiverConnectClicked;
            _btnReceiverDisconnect.Click += OnReceiverDisconnectClicked;
            _btnMatrixConnect.Click += OnMatrixConnectClicked;
            _btnMatrixDisconnect.Click += OnMatrixDisconnectClicked;

            // 参数 - 通道选择变更 → 自动联动矩阵切换
            _cmbChannel.SelectedIndexChanged += OnChannelSelected;

            // 参数 - 模式 Radio Checked 变更 → 刷新频段下拉
            _rbMode1.CheckedChanged += OnModeChanged;
            _rbMode2.CheckedChanged += OnModeChanged;
            _rbMode3.CheckedChanged += OnModeChanged;

            // 参数 - 三个"设置"按钮
            _btnTempSet.Click += OnTempSetClicked;
            _btnModeSet.Click += OnModeSetClicked;
            _btnBandSet.Click += OnBandSetClicked;
            _btnAgcSet.Click += OnAgcSetClicked;

            // 仪表
            _btnRfConnect.Click += OnRfConnectClicked;
            _btnRfDisconnect.Click += OnRfDisconnectClicked;
            _btnLoConnect.Click += OnLoConnectClicked;
            _btnLoDisconnect.Click += OnLoDisconnectClicked;
            _btnSaConnect.Click += OnSaConnectClicked;
            _btnSaDisconnect.Click += OnSaDisconnectClicked;

            // 日志
            _btnLogClear.Click += (s, e) => _rtbLog.Clear();
        }

        // ============================================================
        // 事件处理
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
                string portName = _cmbReceiverPort.SelectedItem == null ? null : _cmbReceiverPort.SelectedItem.ToString();
                int baudRate = int.Parse(_cmbReceiverBaud.Text);
                if (string.IsNullOrEmpty(portName) || portName == "(无)")
                {
                    MessageBox.Show(this, "请先选择有效的串口。", "提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                _receiver = new ReceiverSerialPort();
                _receiver.Open(portName, baudRate);

                // 同步实例化 ChannelLink；桩矩阵先放进去，协议到位后由 M3 替换
                _channelLink = new ChannelLink(_receiver, _matrix);
                Logger.Log($"[UI] Receiver connected on {portName} @ {baudRate}, ChannelLink ready.");
            }
            catch (Exception ex)
            {
                _receiver = null;
                _channelLink = null;
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
                _channelLink = null;
            }
        }

        private void OnMatrixConnectClicked(object sender, EventArgs e)
        {
            try
            {
                string portName = _cmbMatrixPort.SelectedItem == null ? null : _cmbMatrixPort.SelectedItem.ToString();
                int baudRate = int.Parse(_cmbMatrixBaud.Text);
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

        // -------- 通道联动
        private void OnChannelSelected(object sender, EventArgs e)
        {
            if (!_initialized) return; // 初始化阶段不触发切换
            if (_channelLink == null)
            {
                Logger.Log("[UI] 通道变更被忽略：_receiver 未连接。");
                return;
            }

            int ch = _cmbChannel.SelectedIndex + 1; // 0..7 → 1..8
            try
            {
                var sw = Stopwatch.StartNew();
                _channelLink.SwitchToAsync(ch).GetAwaiter().GetResult();
                Logger.Log($"[UI] SwitchToAsync ch={ch} 耗时 {sw.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "通道切换失败: " + ex.Message, "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                Logger.Log("[Error] 通道切换失败: " + ex.Message);
            }
        }

        // -------- 模式切换（联动频段下拉）
        private void OnModeChanged(object sender, EventArgs e)
        {
            if (!_initialized) return;
            if (!_rbMode1.Checked && !_rbMode2.Checked && !_rbMode3.Checked) return;

            ReceiverMode mode;
            if (_rbMode1.Checked) mode = ReceiverMode.Mode1;
            else if (_rbMode2.Checked) mode = ReceiverMode.Mode2;
            else mode = ReceiverMode.Mode3;

            RefreshBandCombo(mode);
        }

        private void RefreshBandCombo(ReceiverMode mode)
        {
            _cmbBand.BeginUpdate();
            try
            {
                _cmbBand.Items.Clear();
                var bands = ModeBandTable.GetBands(mode);
                _currentBands = bands;
                foreach (var b in bands)
                {
                    _cmbBand.Items.Add(b.DisplayName);
                }
                if (_cmbBand.Items.Count > 0)
                {
                    _cmbBand.SelectedIndex = 0;
                }
            }
            finally
            {
                _cmbBand.EndUpdate();
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
            if (_rbTempNormal.Checked) range = TemperatureRange.Normal;
            else if (_rbTempHigh.Checked) range = TemperatureRange.High;
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
            if (_rbMode1.Checked) mode = ReceiverMode.Mode1;
            else if (_rbMode2.Checked) mode = ReceiverMode.Mode2;
            else mode = ReceiverMode.Mode3;
            try
            {
                _receiver.SendModeCommand(mode);
            }
            catch (Exception ex)
            {
                Logger.Log("[Error] 模式命令发送失败: " + ex.Message);
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
            if (_currentBands == null || _cmbBand.SelectedIndex < 0
                || _cmbBand.SelectedIndex >= _currentBands.Count)
            {
                MessageBox.Show(this, "无可用频段。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            ReceiverMode mode;
            if (_rbMode1.Checked) mode = ReceiverMode.Mode1;
            else if (_rbMode2.Checked) mode = ReceiverMode.Mode2;
            else mode = ReceiverMode.Mode3;

            int bandIndex = _currentBands[_cmbBand.SelectedIndex].BandIndex;

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
            int ch = _cmbChannel.SelectedIndex + 1;
            int atten = (int)_numAgcAtten.Value;
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

        // -------- 仪表连接
        private void OnRfConnectClicked(object sender, EventArgs e)
        {
            try
            {
                if (_rfGenerator != null && _rfGenerator.IsConnected)
                {
                    MessageBox.Show(this, "RF 信号源已连接。", "提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                _rfGenerator = new SignalGenerator(_txtRfAddress.Text.Trim());
                _rfGenerator.Connect();
                _pnlRfStatus.BackColor = StatusColorConnected;
                Logger.Log("[UI] RF signal generator connected.");
            }
            catch (Exception ex)
            {
                _pnlRfStatus.BackColor = StatusColorDisconnected;
                try { _rfGenerator?.Dispose(); } catch { }
                _rfGenerator = null;
                Logger.Log("[Error] RF 连接失败: " + ex.Message);
                MessageBox.Show(this, "RF 信号源连接失败: " + ex.Message, "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnRfDisconnectClicked(object sender, EventArgs e)
        {
            try
            {
                _rfGenerator?.Disconnect();
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
                _pnlRfStatus.BackColor = StatusColorDisconnected;
            }
        }

        private void OnLoConnectClicked(object sender, EventArgs e)
        {
            try
            {
                if (_loGenerator != null && _loGenerator.IsConnected)
                {
                    MessageBox.Show(this, "LO 信号源已连接。", "提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                _loGenerator = new SignalGeneratorLO(_txtLoAddress.Text.Trim());
                _loGenerator.Connect();
                _pnlLoStatus.BackColor = StatusColorConnected;
                Logger.Log("[UI] LO signal generator connected.");
            }
            catch (Exception ex)
            {
                _pnlLoStatus.BackColor = StatusColorDisconnected;
                try { _loGenerator?.Dispose(); } catch { }
                _loGenerator = null;
                Logger.Log("[Error] LO 连接失败: " + ex.Message);
                MessageBox.Show(this, "LO 信号源连接失败: " + ex.Message, "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnLoDisconnectClicked(object sender, EventArgs e)
        {
            try
            {
                _loGenerator?.Disconnect();
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
                _pnlLoStatus.BackColor = StatusColorDisconnected;
            }
        }

        private void OnSaConnectClicked(object sender, EventArgs e)
        {
            try
            {
                if (_spectrumAnalyzer != null && _spectrumAnalyzer.IsConnected)
                {
                    MessageBox.Show(this, "频谱仪已连接。", "提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                _spectrumAnalyzer = new SpectrumAnalyzer(_txtSaAddress.Text.Trim());
                _spectrumAnalyzer.Connect();
                _pnlSaStatus.BackColor = StatusColorConnected;
                Logger.Log("[UI] Spectrum analyzer connected.");
            }
            catch (Exception ex)
            {
                _pnlSaStatus.BackColor = StatusColorDisconnected;
                try { _spectrumAnalyzer?.Dispose(); } catch { }
                _spectrumAnalyzer = null;
                Logger.Log("[Error] 频谱仪连接失败: " + ex.Message);
                MessageBox.Show(this, "频谱仪连接失败: " + ex.Message, "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnSaDisconnectClicked(object sender, EventArgs e)
        {
            try
            {
                _spectrumAnalyzer?.Disconnect();
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
                _pnlSaStatus.BackColor = StatusColorDisconnected;
            }
        }

        // -------- 日志回调
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
