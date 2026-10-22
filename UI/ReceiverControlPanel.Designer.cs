namespace A205AutoTestSystem.UI
{
    /// <summary>
    /// ReceiverControlPanel 的设计器文件（绝对定位布局）。
    /// 所有控件使用显式 Location/Size，可在 VS 设计器中自由拖拽、缩放调整。
    /// </summary>
    partial class ReceiverControlPanel
    {
        private System.ComponentModel.IContainer components = null;

        // ============================================================
        // 控件字段
        // ============================================================

        // 串口连接区
        private System.Windows.Forms.GroupBox grpSerialConnection;
        private System.Windows.Forms.Label lblReceiverSerial;
        private System.Windows.Forms.ComboBox cmbReceiverPort;
        private System.Windows.Forms.ComboBox cmbReceiverBaud;
        private System.Windows.Forms.Button btnReceiverConnect;
        private System.Windows.Forms.Button btnReceiverDisconnect;
        private System.Windows.Forms.Label lblMatrixSerial;
        private System.Windows.Forms.ComboBox cmbMatrixPort;
        private System.Windows.Forms.ComboBox cmbMatrixBaud;
        private System.Windows.Forms.Button btnMatrixConnect;
        private System.Windows.Forms.Button btnMatrixDisconnect;

        // 参数配置区
        private System.Windows.Forms.GroupBox grpParameterConfig;
        // 温度/模式各用一个无边框 Panel 作为 RadioButton 互斥分组容器（WinForms 按直接父容器分组）
        private System.Windows.Forms.Panel pnlTempGroup;
        private System.Windows.Forms.Panel pnlModeGroup;
        private System.Windows.Forms.RadioButton rbTempNormal;
        private System.Windows.Forms.RadioButton rbTempHigh;
        private System.Windows.Forms.RadioButton rbTempLow;
        private System.Windows.Forms.Button btnTempSet;
        private System.Windows.Forms.Label lblTempRange;
        // M6.x：暴露给同程序集 AutoTestPanel 访问（单向同步通道号）。
        internal System.Windows.Forms.ComboBox cmbChannel;
        private System.Windows.Forms.Label lblChannelSelect;
        private System.Windows.Forms.RadioButton rbMode1;
        private System.Windows.Forms.RadioButton rbMode2;
        private System.Windows.Forms.RadioButton rbMode3;
        private System.Windows.Forms.Button btnModeSet;
        private System.Windows.Forms.Label lblModeSelect;
        private System.Windows.Forms.ComboBox cmbBand;
        private System.Windows.Forms.Button btnBandSet;
        private System.Windows.Forms.Label lblBandSelect;
        private System.Windows.Forms.NumericUpDown numAgcAtten;
        private System.Windows.Forms.Button btnAgcSet;
        private System.Windows.Forms.Label lblAgcAtten;

        // 仪表状态区（地址与连接状态已迁移到配置文件 + 信号链路拓扑图）
        private System.Windows.Forms.GroupBox grpInstrumentStatus;
        private System.Windows.Forms.Label lblStatusHint;
        private System.Windows.Forms.Button btnConnectAll;
        private System.Windows.Forms.Button btnDisconnectAll;

        // 通信日志区
        private System.Windows.Forms.GroupBox grpCommunicationLog;
        private System.Windows.Forms.RichTextBox rtbLog;
        private System.Windows.Forms.Button btnLogClear;

        // 注：Dispose(bool) override 保留在 ReceiverControlPanel.cs 中，
        //     业务资源（Logger / 串口 / 仪表）的清理与 components.Dispose 都在那里。

        #region Windows 窗体设计器生成的代码

        private void InitializeComponent()
        {
            this.grpSerialConnection = new System.Windows.Forms.GroupBox();
            this.lblReceiverSerial = new System.Windows.Forms.Label();
            this.cmbReceiverPort = new System.Windows.Forms.ComboBox();
            this.cmbReceiverBaud = new System.Windows.Forms.ComboBox();
            this.btnReceiverConnect = new System.Windows.Forms.Button();
            this.btnReceiverDisconnect = new System.Windows.Forms.Button();
            this.lblMatrixSerial = new System.Windows.Forms.Label();
            this.cmbMatrixPort = new System.Windows.Forms.ComboBox();
            this.cmbMatrixBaud = new System.Windows.Forms.ComboBox();
            this.btnMatrixConnect = new System.Windows.Forms.Button();
            this.btnMatrixDisconnect = new System.Windows.Forms.Button();
            this.grpParameterConfig = new System.Windows.Forms.GroupBox();
            this.lblTempRange = new System.Windows.Forms.Label();
            this.pnlTempGroup = new System.Windows.Forms.Panel();
            this.rbTempNormal = new System.Windows.Forms.RadioButton();
            this.rbTempHigh = new System.Windows.Forms.RadioButton();
            this.rbTempLow = new System.Windows.Forms.RadioButton();
            this.btnTempSet = new System.Windows.Forms.Button();
            this.lblChannelSelect = new System.Windows.Forms.Label();
            this.cmbChannel = new System.Windows.Forms.ComboBox();
            this.lblModeSelect = new System.Windows.Forms.Label();
            this.pnlModeGroup = new System.Windows.Forms.Panel();
            this.rbMode1 = new System.Windows.Forms.RadioButton();
            this.rbMode2 = new System.Windows.Forms.RadioButton();
            this.rbMode3 = new System.Windows.Forms.RadioButton();
            this.btnModeSet = new System.Windows.Forms.Button();
            this.lblBandSelect = new System.Windows.Forms.Label();
            this.cmbBand = new System.Windows.Forms.ComboBox();
            this.btnBandSet = new System.Windows.Forms.Button();
            this.lblAgcAtten = new System.Windows.Forms.Label();
            this.numAgcAtten = new System.Windows.Forms.NumericUpDown();
            this.btnAgcSet = new System.Windows.Forms.Button();
            this.grpInstrumentStatus = new System.Windows.Forms.GroupBox();
            this.lblStatusHint = new System.Windows.Forms.Label();
            this.btnConnectAll = new System.Windows.Forms.Button();
            this.btnDisconnectAll = new System.Windows.Forms.Button();
            this.grpCommunicationLog = new System.Windows.Forms.GroupBox();
            this.rtbLog = new System.Windows.Forms.RichTextBox();
            this.btnLogClear = new System.Windows.Forms.Button();
            this.btnOpenPower = new System.Windows.Forms.Button();
            this.grpInstrumentControl = new System.Windows.Forms.GroupBox();
            this.btnSaPeakSearch = new System.Windows.Forms.Button();
            this.btnSaMarkerAllOff = new System.Windows.Forms.Button();
            this.btnSaReset = new System.Windows.Forms.Button();
            this.btnSaMaxHold = new System.Windows.Forms.Button();
            this.btnSaPeakAuto = new System.Windows.Forms.Button();
            this.btnSaBwAuto = new System.Windows.Forms.Button();
            this.btnSaTraceAvg = new System.Windows.Forms.Button();
            this.btnSaMinHold = new System.Windows.Forms.Button();
            this.btnSaDeltaMarker = new System.Windows.Forms.Button();
            this.btnSaHarmonics = new System.Windows.Forms.Button();
            this.btnSaRefLevelUp = new System.Windows.Forms.Button();
            this.btnSaRefLevelDown = new System.Windows.Forms.Button();
            this.pnlSignalFlow = new A205AutoTestSystem.UI.SignalFlowPanel();
            this.grpSerialConnection.SuspendLayout();
            this.grpParameterConfig.SuspendLayout();
            this.pnlTempGroup.SuspendLayout();
            this.pnlModeGroup.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numAgcAtten)).BeginInit();
            this.grpInstrumentStatus.SuspendLayout();
            this.grpCommunicationLog.SuspendLayout();
            this.grpInstrumentControl.SuspendLayout();
            this.SuspendLayout();
            // 
            // grpSerialConnection
            // 
            this.grpSerialConnection.Controls.Add(this.lblReceiverSerial);
            this.grpSerialConnection.Controls.Add(this.cmbReceiverPort);
            this.grpSerialConnection.Controls.Add(this.cmbReceiverBaud);
            this.grpSerialConnection.Controls.Add(this.btnReceiverConnect);
            this.grpSerialConnection.Controls.Add(this.btnReceiverDisconnect);
            this.grpSerialConnection.Controls.Add(this.lblMatrixSerial);
            this.grpSerialConnection.Controls.Add(this.cmbMatrixPort);
            this.grpSerialConnection.Controls.Add(this.cmbMatrixBaud);
            this.grpSerialConnection.Controls.Add(this.btnMatrixConnect);
            this.grpSerialConnection.Controls.Add(this.btnMatrixDisconnect);
            this.grpSerialConnection.Location = new System.Drawing.Point(0, 8);
            this.grpSerialConnection.Name = "grpSerialConnection";
            this.grpSerialConnection.Padding = new System.Windows.Forms.Padding(10, 5, 10, 10);
            this.grpSerialConnection.Size = new System.Drawing.Size(449, 86);
            this.grpSerialConnection.TabIndex = 0;
            this.grpSerialConnection.TabStop = false;
            this.grpSerialConnection.Text = "串口连接";
            // 
            // lblReceiverSerial
            // 
            this.lblReceiverSerial.Location = new System.Drawing.Point(12, 20);
            this.lblReceiverSerial.Name = "lblReceiverSerial";
            this.lblReceiverSerial.Size = new System.Drawing.Size(84, 24);
            this.lblReceiverSerial.TabIndex = 0;
            this.lblReceiverSerial.Text = "接收机串口";
            this.lblReceiverSerial.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // cmbReceiverPort
            // 
            this.cmbReceiverPort.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbReceiverPort.Location = new System.Drawing.Point(102, 21);
            this.cmbReceiverPort.Name = "cmbReceiverPort";
            this.cmbReceiverPort.Size = new System.Drawing.Size(84, 23);
            this.cmbReceiverPort.TabIndex = 1;
            // 
            // cmbReceiverBaud
            // 
            this.cmbReceiverBaud.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbReceiverBaud.Items.AddRange(new object[] {
            "9600",
            "19200",
            "38400",
            "57600",
            "115200"});
            this.cmbReceiverBaud.Location = new System.Drawing.Point(192, 22);
            this.cmbReceiverBaud.Name = "cmbReceiverBaud";
            this.cmbReceiverBaud.Size = new System.Drawing.Size(84, 23);
            this.cmbReceiverBaud.TabIndex = 2;
            // 
            // btnReceiverConnect
            // 
            this.btnReceiverConnect.Location = new System.Drawing.Point(288, 21);
            this.btnReceiverConnect.Name = "btnReceiverConnect";
            this.btnReceiverConnect.Size = new System.Drawing.Size(72, 26);
            this.btnReceiverConnect.TabIndex = 3;
            this.btnReceiverConnect.Text = "连接";
            this.btnReceiverConnect.UseVisualStyleBackColor = true;
            this.btnReceiverConnect.Click += new System.EventHandler(this.OnReceiverConnectClicked);
            // 
            // btnReceiverDisconnect
            // 
            this.btnReceiverDisconnect.Location = new System.Drawing.Point(366, 21);
            this.btnReceiverDisconnect.Name = "btnReceiverDisconnect";
            this.btnReceiverDisconnect.Size = new System.Drawing.Size(72, 26);
            this.btnReceiverDisconnect.TabIndex = 4;
            this.btnReceiverDisconnect.Text = "断开";
            this.btnReceiverDisconnect.UseVisualStyleBackColor = true;
            this.btnReceiverDisconnect.Click += new System.EventHandler(this.OnReceiverDisconnectClicked);
            // 
            // lblMatrixSerial
            // 
            this.lblMatrixSerial.Location = new System.Drawing.Point(18, 52);
            this.lblMatrixSerial.Name = "lblMatrixSerial";
            this.lblMatrixSerial.Size = new System.Drawing.Size(68, 24);
            this.lblMatrixSerial.TabIndex = 5;
            this.lblMatrixSerial.Text = "矩阵串口";
            this.lblMatrixSerial.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // cmbMatrixPort
            // 
            this.cmbMatrixPort.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbMatrixPort.Location = new System.Drawing.Point(102, 53);
            this.cmbMatrixPort.Name = "cmbMatrixPort";
            this.cmbMatrixPort.Size = new System.Drawing.Size(84, 23);
            this.cmbMatrixPort.TabIndex = 6;
            // 
            // cmbMatrixBaud
            // 
            this.cmbMatrixBaud.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbMatrixBaud.Items.AddRange(new object[] {
            "9600",
            "19200",
            "38400",
            "57600",
            "115200"});
            this.cmbMatrixBaud.Location = new System.Drawing.Point(192, 54);
            this.cmbMatrixBaud.Name = "cmbMatrixBaud";
            this.cmbMatrixBaud.Size = new System.Drawing.Size(84, 23);
            this.cmbMatrixBaud.TabIndex = 7;
            // 
            // btnMatrixConnect
            // 
            this.btnMatrixConnect.Location = new System.Drawing.Point(288, 53);
            this.btnMatrixConnect.Name = "btnMatrixConnect";
            this.btnMatrixConnect.Size = new System.Drawing.Size(72, 26);
            this.btnMatrixConnect.TabIndex = 8;
            this.btnMatrixConnect.Text = "连接";
            this.btnMatrixConnect.UseVisualStyleBackColor = true;
            this.btnMatrixConnect.Click += new System.EventHandler(this.OnMatrixConnectClicked);
            // 
            // btnMatrixDisconnect
            // 
            this.btnMatrixDisconnect.Location = new System.Drawing.Point(366, 53);
            this.btnMatrixDisconnect.Name = "btnMatrixDisconnect";
            this.btnMatrixDisconnect.Size = new System.Drawing.Size(72, 26);
            this.btnMatrixDisconnect.TabIndex = 9;
            this.btnMatrixDisconnect.Text = "断开";
            this.btnMatrixDisconnect.UseVisualStyleBackColor = true;
            this.btnMatrixDisconnect.Click += new System.EventHandler(this.OnMatrixDisconnectClicked);
            // 
            // grpParameterConfig
            // 
            this.grpParameterConfig.Controls.Add(this.lblTempRange);
            this.grpParameterConfig.Controls.Add(this.pnlTempGroup);
            this.grpParameterConfig.Controls.Add(this.btnTempSet);
            this.grpParameterConfig.Controls.Add(this.lblChannelSelect);
            this.grpParameterConfig.Controls.Add(this.cmbChannel);
            this.grpParameterConfig.Controls.Add(this.lblModeSelect);
            this.grpParameterConfig.Controls.Add(this.pnlModeGroup);
            this.grpParameterConfig.Controls.Add(this.btnModeSet);
            this.grpParameterConfig.Controls.Add(this.lblBandSelect);
            this.grpParameterConfig.Controls.Add(this.cmbBand);
            this.grpParameterConfig.Controls.Add(this.btnBandSet);
            this.grpParameterConfig.Controls.Add(this.lblAgcAtten);
            this.grpParameterConfig.Controls.Add(this.numAgcAtten);
            this.grpParameterConfig.Controls.Add(this.btnAgcSet);
            this.grpParameterConfig.Location = new System.Drawing.Point(0, 100);
            this.grpParameterConfig.Name = "grpParameterConfig";
            this.grpParameterConfig.Padding = new System.Windows.Forms.Padding(10, 5, 10, 10);
            this.grpParameterConfig.Size = new System.Drawing.Size(449, 188);
            this.grpParameterConfig.TabIndex = 1;
            this.grpParameterConfig.TabStop = false;
            this.grpParameterConfig.Text = "接收机参数配置";
            // 
            // lblTempRange
            // 
            this.lblTempRange.Location = new System.Drawing.Point(18, 28);
            this.lblTempRange.Name = "lblTempRange";
            this.lblTempRange.Size = new System.Drawing.Size(82, 24);
            this.lblTempRange.TabIndex = 0;
            this.lblTempRange.Text = "温度区间";
            this.lblTempRange.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // pnlTempGroup
            // 
            this.pnlTempGroup.Controls.Add(this.rbTempNormal);
            this.pnlTempGroup.Controls.Add(this.rbTempHigh);
            this.pnlTempGroup.Controls.Add(this.rbTempLow);
            this.pnlTempGroup.Location = new System.Drawing.Point(104, 28);
            this.pnlTempGroup.Name = "pnlTempGroup";
            this.pnlTempGroup.Size = new System.Drawing.Size(250, 24);
            this.pnlTempGroup.TabIndex = 20;
            // 
            // rbTempNormal
            // 
            this.rbTempNormal.AutoSize = true;
            this.rbTempNormal.Location = new System.Drawing.Point(5, 2);
            this.rbTempNormal.Name = "rbTempNormal";
            this.rbTempNormal.Size = new System.Drawing.Size(58, 19);
            this.rbTempNormal.TabIndex = 1;
            this.rbTempNormal.Text = "常温";
            this.rbTempNormal.UseVisualStyleBackColor = true;
            // 
            // rbTempHigh
            // 
            this.rbTempHigh.AutoSize = true;
            this.rbTempHigh.Location = new System.Drawing.Point(88, 2);
            this.rbTempHigh.Name = "rbTempHigh";
            this.rbTempHigh.Size = new System.Drawing.Size(58, 19);
            this.rbTempHigh.TabIndex = 2;
            this.rbTempHigh.Text = "高温";
            this.rbTempHigh.UseVisualStyleBackColor = true;
            // 
            // rbTempLow
            // 
            this.rbTempLow.AutoSize = true;
            this.rbTempLow.Location = new System.Drawing.Point(178, 3);
            this.rbTempLow.Name = "rbTempLow";
            this.rbTempLow.Size = new System.Drawing.Size(58, 19);
            this.rbTempLow.TabIndex = 3;
            this.rbTempLow.Text = "低温";
            this.rbTempLow.UseVisualStyleBackColor = true;
            // 
            // btnTempSet
            // 
            this.btnTempSet.Location = new System.Drawing.Point(366, 26);
            this.btnTempSet.Name = "btnTempSet";
            this.btnTempSet.Size = new System.Drawing.Size(72, 26);
            this.btnTempSet.TabIndex = 4;
            this.btnTempSet.Text = "设置";
            this.btnTempSet.UseVisualStyleBackColor = true;
            this.btnTempSet.Click += new System.EventHandler(this.OnTempSetClicked);
            // 
            // lblChannelSelect
            // 
            this.lblChannelSelect.Location = new System.Drawing.Point(18, 59);
            this.lblChannelSelect.Name = "lblChannelSelect";
            this.lblChannelSelect.Size = new System.Drawing.Size(84, 24);
            this.lblChannelSelect.TabIndex = 5;
            this.lblChannelSelect.Text = "通道选择";
            this.lblChannelSelect.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // cmbChannel
            // 
            this.cmbChannel.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbChannel.Location = new System.Drawing.Point(104, 59);
            this.cmbChannel.Name = "cmbChannel";
            this.cmbChannel.Size = new System.Drawing.Size(250, 23);
            this.cmbChannel.TabIndex = 6;
            this.cmbChannel.SelectedIndexChanged += new System.EventHandler(this.OnChannelSelected);
            // 
            // lblModeSelect
            // 
            this.lblModeSelect.Location = new System.Drawing.Point(18, 92);
            this.lblModeSelect.Name = "lblModeSelect";
            this.lblModeSelect.Size = new System.Drawing.Size(84, 24);
            this.lblModeSelect.TabIndex = 8;
            this.lblModeSelect.Text = "模式选择";
            this.lblModeSelect.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // pnlModeGroup
            // 
            this.pnlModeGroup.Controls.Add(this.rbMode1);
            this.pnlModeGroup.Controls.Add(this.rbMode2);
            this.pnlModeGroup.Controls.Add(this.rbMode3);
            this.pnlModeGroup.Location = new System.Drawing.Point(104, 92);
            this.pnlModeGroup.Name = "pnlModeGroup";
            this.pnlModeGroup.Size = new System.Drawing.Size(250, 24);
            this.pnlModeGroup.TabIndex = 21;
            // 
            // rbMode1
            // 
            this.rbMode1.AutoSize = true;
            this.rbMode1.Location = new System.Drawing.Point(4, 2);
            this.rbMode1.Name = "rbMode1";
            this.rbMode1.Size = new System.Drawing.Size(68, 19);
            this.rbMode1.TabIndex = 9;
            this.rbMode1.Text = "Mode1";
            this.rbMode1.UseVisualStyleBackColor = true;
            this.rbMode1.CheckedChanged += new System.EventHandler(this.OnModeChanged);
            // 
            // rbMode2
            // 
            this.rbMode2.AutoSize = true;
            this.rbMode2.Location = new System.Drawing.Point(88, 2);
            this.rbMode2.Name = "rbMode2";
            this.rbMode2.Size = new System.Drawing.Size(68, 19);
            this.rbMode2.TabIndex = 10;
            this.rbMode2.Text = "Mode2";
            this.rbMode2.UseVisualStyleBackColor = true;
            this.rbMode2.CheckedChanged += new System.EventHandler(this.OnModeChanged);
            // 
            // rbMode3
            // 
            this.rbMode3.AutoSize = true;
            this.rbMode3.Location = new System.Drawing.Point(178, 3);
            this.rbMode3.Name = "rbMode3";
            this.rbMode3.Size = new System.Drawing.Size(68, 19);
            this.rbMode3.TabIndex = 11;
            this.rbMode3.Text = "Mode3";
            this.rbMode3.UseVisualStyleBackColor = true;
            this.rbMode3.CheckedChanged += new System.EventHandler(this.OnModeChanged);
            // 
            // btnModeSet
            // 
            this.btnModeSet.Location = new System.Drawing.Point(366, 90);
            this.btnModeSet.Name = "btnModeSet";
            this.btnModeSet.Size = new System.Drawing.Size(72, 26);
            this.btnModeSet.TabIndex = 12;
            this.btnModeSet.Text = "设置";
            this.btnModeSet.UseVisualStyleBackColor = true;
            this.btnModeSet.Click += new System.EventHandler(this.OnModeSetClicked);
            // 
            // lblBandSelect
            // 
            this.lblBandSelect.Location = new System.Drawing.Point(18, 122);
            this.lblBandSelect.Name = "lblBandSelect";
            this.lblBandSelect.Size = new System.Drawing.Size(84, 24);
            this.lblBandSelect.TabIndex = 13;
            this.lblBandSelect.Text = "频段选择";
            this.lblBandSelect.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // cmbBand
            // 
            this.cmbBand.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbBand.Location = new System.Drawing.Point(104, 123);
            this.cmbBand.Name = "cmbBand";
            this.cmbBand.Size = new System.Drawing.Size(250, 23);
            this.cmbBand.TabIndex = 14;
            // 
            // btnBandSet
            // 
            this.btnBandSet.Location = new System.Drawing.Point(366, 123);
            this.btnBandSet.Name = "btnBandSet";
            this.btnBandSet.Size = new System.Drawing.Size(72, 26);
            this.btnBandSet.TabIndex = 15;
            this.btnBandSet.Text = "设置";
            this.btnBandSet.UseVisualStyleBackColor = true;
            this.btnBandSet.Click += new System.EventHandler(this.OnBandSetClicked);
            // 
            // lblAgcAtten
            // 
            this.lblAgcAtten.Location = new System.Drawing.Point(18, 154);
            this.lblAgcAtten.Name = "lblAgcAtten";
            this.lblAgcAtten.Size = new System.Drawing.Size(84, 24);
            this.lblAgcAtten.TabIndex = 17;
            this.lblAgcAtten.Text = "AGC 衰减";
            this.lblAgcAtten.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // numAgcAtten
            // 
            this.numAgcAtten.Increment = new decimal(new int[] {
            4,
            0,
            0,
            0});
            this.numAgcAtten.Location = new System.Drawing.Point(104, 155);
            this.numAgcAtten.Maximum = new decimal(new int[] {
            60,
            0,
            0,
            0});
            this.numAgcAtten.Name = "numAgcAtten";
            this.numAgcAtten.Size = new System.Drawing.Size(250, 25);
            this.numAgcAtten.TabIndex = 18;
            // 
            // btnAgcSet
            // 
            this.btnAgcSet.Location = new System.Drawing.Point(366, 154);
            this.btnAgcSet.Name = "btnAgcSet";
            this.btnAgcSet.Size = new System.Drawing.Size(72, 26);
            this.btnAgcSet.TabIndex = 19;
            this.btnAgcSet.Text = "设置";
            this.btnAgcSet.UseVisualStyleBackColor = true;
            this.btnAgcSet.Click += new System.EventHandler(this.OnAgcSetClicked);
            // 
            // grpInstrumentStatus
            // 
            this.grpInstrumentStatus.Controls.Add(this.lblStatusHint);
            this.grpInstrumentStatus.Location = new System.Drawing.Point(0, 294);
            this.grpInstrumentStatus.Name = "grpInstrumentStatus";
            this.grpInstrumentStatus.Padding = new System.Windows.Forms.Padding(10, 5, 10, 10);
            this.grpInstrumentStatus.Size = new System.Drawing.Size(449, 153);
            this.grpInstrumentStatus.TabIndex = 2;
            this.grpInstrumentStatus.TabStop = false;
            this.grpInstrumentStatus.Text = "仪表连接（地址读自 config\\Instruments.xml）";
            // 
            // lblStatusHint
            // 
            this.lblStatusHint.Location = new System.Drawing.Point(18, 30);
            this.lblStatusHint.Name = "lblStatusHint";
            this.lblStatusHint.Size = new System.Drawing.Size(335, 96);
            this.lblStatusHint.TabIndex = 1;
            this.lblStatusHint.Text = "仪表 VISA 地址已改为配置文件管理，请编辑程序目录下的 config\\Instruments.xml 后点击“连接”。\r\n\r\n各仪表的连接状态（含异常标红）请" +
    "查看右侧『信号链路』拓扑图，失败原因见下方日志。";
            // 
            // btnConnectAll
            // 
            this.btnConnectAll.Location = new System.Drawing.Point(17, 183);
            this.btnConnectAll.Name = "btnConnectAll";
            this.btnConnectAll.Size = new System.Drawing.Size(80, 49);
            this.btnConnectAll.TabIndex = 30;
            this.btnConnectAll.Text = "连接";
            this.btnConnectAll.UseVisualStyleBackColor = true;
            this.btnConnectAll.Click += new System.EventHandler(this.OnConnectAllClicked);
            // 
            // btnDisconnectAll
            // 
            this.btnDisconnectAll.Location = new System.Drawing.Point(105, 183);
            this.btnDisconnectAll.Name = "btnDisconnectAll";
            this.btnDisconnectAll.Size = new System.Drawing.Size(80, 49);
            this.btnDisconnectAll.TabIndex = 31;
            this.btnDisconnectAll.Text = "断开";
            this.btnDisconnectAll.UseVisualStyleBackColor = true;
            this.btnDisconnectAll.Click += new System.EventHandler(this.OnDisconnectAllClicked);
            // 
            // grpCommunicationLog
            // 
            this.grpCommunicationLog.Controls.Add(this.rtbLog);
            this.grpCommunicationLog.Controls.Add(this.btnLogClear);
            this.grpCommunicationLog.Location = new System.Drawing.Point(0, 453);
            this.grpCommunicationLog.Name = "grpCommunicationLog";
            this.grpCommunicationLog.Padding = new System.Windows.Forms.Padding(10, 5, 10, 10);
            this.grpCommunicationLog.Size = new System.Drawing.Size(841, 264);
            this.grpCommunicationLog.TabIndex = 3;
            this.grpCommunicationLog.TabStop = false;
            this.grpCommunicationLog.Text = "日志";
            // 
            // rtbLog
            // 
            this.rtbLog.BackColor = System.Drawing.Color.Black;
            this.rtbLog.DetectUrls = false;
            this.rtbLog.Font = new System.Drawing.Font("Consolas", 9F);
            this.rtbLog.ForeColor = System.Drawing.Color.LightGreen;
            this.rtbLog.Location = new System.Drawing.Point(4, 19);
            this.rtbLog.Name = "rtbLog";
            this.rtbLog.ReadOnly = true;
            this.rtbLog.Size = new System.Drawing.Size(835, 215);
            this.rtbLog.TabIndex = 0;
            this.rtbLog.Text = "";
            // 
            // btnLogClear
            // 
            this.btnLogClear.Location = new System.Drawing.Point(769, 234);
            this.btnLogClear.Name = "btnLogClear";
            this.btnLogClear.Size = new System.Drawing.Size(70, 26);
            this.btnLogClear.TabIndex = 1;
            this.btnLogClear.Text = "清除";
            this.btnLogClear.UseVisualStyleBackColor = true;
            this.btnLogClear.Click += new System.EventHandler(this.OnBtnLogClearClicked);
            // 
            // btnOpenPower
            // 
            this.btnOpenPower.Location = new System.Drawing.Point(17, 20);
            this.btnOpenPower.Name = "btnOpenPower";
            this.btnOpenPower.Size = new System.Drawing.Size(80, 49);
            this.btnOpenPower.TabIndex = 22;
            this.btnOpenPower.Text = "开启供电";
            this.btnOpenPower.UseVisualStyleBackColor = true;
            this.btnOpenPower.Click += new System.EventHandler(this.OnOpenPowerClicked);
            // 
            // grpInstrumentControl
            // 
            this.grpInstrumentControl.Controls.Add(this.btnOpenPower);
            this.grpInstrumentControl.Controls.Add(this.btnDisconnectAll);
            this.grpInstrumentControl.Controls.Add(this.btnConnectAll);
            this.grpInstrumentControl.Controls.Add(this.btnSaPeakSearch);
            this.grpInstrumentControl.Controls.Add(this.btnSaMarkerAllOff);
            this.grpInstrumentControl.Controls.Add(this.btnSaReset);
            this.grpInstrumentControl.Controls.Add(this.btnSaMaxHold);
            this.grpInstrumentControl.Controls.Add(this.btnSaPeakAuto);
            this.grpInstrumentControl.Controls.Add(this.btnSaBwAuto);
            this.grpInstrumentControl.Controls.Add(this.btnSaTraceAvg);
            this.grpInstrumentControl.Controls.Add(this.btnSaMinHold);
            this.grpInstrumentControl.Controls.Add(this.btnSaDeltaMarker);
            this.grpInstrumentControl.Controls.Add(this.btnSaHarmonics);
            this.grpInstrumentControl.Controls.Add(this.btnSaRefLevelUp);
            this.grpInstrumentControl.Controls.Add(this.btnSaRefLevelDown);
            this.grpInstrumentControl.Location = new System.Drawing.Point(456, 8);
            this.grpInstrumentControl.Name = "grpInstrumentControl";
            this.grpInstrumentControl.Size = new System.Drawing.Size(383, 238);
            this.grpInstrumentControl.TabIndex = 33;
            this.grpInstrumentControl.TabStop = false;
            this.grpInstrumentControl.Text = "仪表控制";
            // 
            // btnSaPeakSearch
            // 
            this.btnSaPeakSearch.Location = new System.Drawing.Point(17, 130);
            this.btnSaPeakSearch.Name = "btnSaPeakSearch";
            this.btnSaPeakSearch.Size = new System.Drawing.Size(80, 49);
            this.btnSaPeakSearch.TabIndex = 40;
            this.btnSaPeakSearch.Text = "峰值搜索";
            this.btnSaPeakSearch.UseVisualStyleBackColor = true;
            this.btnSaPeakSearch.Click += new System.EventHandler(this.OnSaPeakSearchClicked);
            // 
            // btnSaMarkerAllOff
            // 
            this.btnSaMarkerAllOff.Location = new System.Drawing.Point(285, 130);
            this.btnSaMarkerAllOff.Name = "btnSaMarkerAllOff";
            this.btnSaMarkerAllOff.Size = new System.Drawing.Size(80, 49);
            this.btnSaMarkerAllOff.TabIndex = 41;
            this.btnSaMarkerAllOff.Text = "关闭标记";
            this.btnSaMarkerAllOff.UseVisualStyleBackColor = true;
            this.btnSaMarkerAllOff.Click += new System.EventHandler(this.OnSaMarkerAllOffClicked);
            // 
            // btnSaReset
            // 
            this.btnSaReset.Location = new System.Drawing.Point(285, 20);
            this.btnSaReset.Name = "btnSaReset";
            this.btnSaReset.Size = new System.Drawing.Size(80, 49);
            this.btnSaReset.TabIndex = 42;
            this.btnSaReset.Text = "Reset";
            this.btnSaReset.UseVisualStyleBackColor = true;
            this.btnSaReset.Click += new System.EventHandler(this.OnSaResetClicked);
            // 
            // btnSaMaxHold
            // 
            this.btnSaMaxHold.Location = new System.Drawing.Point(105, 75);
            this.btnSaMaxHold.Name = "btnSaMaxHold";
            this.btnSaMaxHold.Size = new System.Drawing.Size(80, 49);
            this.btnSaMaxHold.TabIndex = 43;
            this.btnSaMaxHold.Text = "最大保持";
            this.btnSaMaxHold.UseVisualStyleBackColor = true;
            this.btnSaMaxHold.Click += new System.EventHandler(this.OnSaMaxHoldClicked);
            // 
            // btnSaPeakAuto
            // 
            this.btnSaPeakAuto.Location = new System.Drawing.Point(197, 130);
            this.btnSaPeakAuto.Name = "btnSaPeakAuto";
            this.btnSaPeakAuto.Size = new System.Drawing.Size(80, 49);
            this.btnSaPeakAuto.TabIndex = 46;
            this.btnSaPeakAuto.Text = "峰值自动搜索";
            this.btnSaPeakAuto.UseVisualStyleBackColor = true;
            this.btnSaPeakAuto.Click += new System.EventHandler(this.OnSaPeakAutoClicked);
            // 
            // btnSaBwAuto
            // 
            this.btnSaBwAuto.Location = new System.Drawing.Point(285, 75);
            this.btnSaBwAuto.Name = "btnSaBwAuto";
            this.btnSaBwAuto.Size = new System.Drawing.Size(80, 49);
            this.btnSaBwAuto.TabIndex = 47;
            this.btnSaBwAuto.Text = "RBW/VBW自动";
            this.btnSaBwAuto.UseVisualStyleBackColor = true;
            this.btnSaBwAuto.Click += new System.EventHandler(this.OnSaBwAutoClicked);
            // 
            // btnSaTraceAvg
            // 
            this.btnSaTraceAvg.Location = new System.Drawing.Point(17, 75);
            this.btnSaTraceAvg.Name = "btnSaTraceAvg";
            this.btnSaTraceAvg.Size = new System.Drawing.Size(80, 49);
            this.btnSaTraceAvg.TabIndex = 48;
            this.btnSaTraceAvg.Text = "轨迹平均";
            this.btnSaTraceAvg.UseVisualStyleBackColor = true;
            this.btnSaTraceAvg.Click += new System.EventHandler(this.OnSaTraceAvgClicked);
            // 
            // btnSaMinHold
            // 
            this.btnSaMinHold.Location = new System.Drawing.Point(197, 75);
            this.btnSaMinHold.Name = "btnSaMinHold";
            this.btnSaMinHold.Size = new System.Drawing.Size(80, 49);
            this.btnSaMinHold.TabIndex = 49;
            this.btnSaMinHold.Text = "最小保持";
            this.btnSaMinHold.UseVisualStyleBackColor = true;
            this.btnSaMinHold.Click += new System.EventHandler(this.OnSaMinHoldClicked);
            // 
            // btnSaDeltaMarker
            // 
            this.btnSaDeltaMarker.Location = new System.Drawing.Point(105, 130);
            this.btnSaDeltaMarker.Name = "btnSaDeltaMarker";
            this.btnSaDeltaMarker.Size = new System.Drawing.Size(80, 49);
            this.btnSaDeltaMarker.TabIndex = 50;
            this.btnSaDeltaMarker.Text = "差值标记";
            this.btnSaDeltaMarker.UseVisualStyleBackColor = true;
            this.btnSaDeltaMarker.Click += new System.EventHandler(this.OnSaDeltaMarkerClicked);
            // 
            // btnSaHarmonics
            // 
            this.btnSaHarmonics.Location = new System.Drawing.Point(105, 20);
            this.btnSaHarmonics.Name = "btnSaHarmonics";
            this.btnSaHarmonics.Size = new System.Drawing.Size(80, 49);
            this.btnSaHarmonics.TabIndex = 51;
            this.btnSaHarmonics.Text = "谐波测量";
            this.btnSaHarmonics.UseVisualStyleBackColor = true;
            this.btnSaHarmonics.Click += new System.EventHandler(this.OnSaHarmonicsClicked);
            // 
            // btnSaRefLevelUp
            // 
            this.btnSaRefLevelUp.Location = new System.Drawing.Point(197, 183);
            this.btnSaRefLevelUp.Name = "btnSaRefLevelUp";
            this.btnSaRefLevelUp.Size = new System.Drawing.Size(80, 49);
            this.btnSaRefLevelUp.TabIndex = 52;
            this.btnSaRefLevelUp.Text = "参考电平+5";
            this.btnSaRefLevelUp.UseVisualStyleBackColor = true;
            this.btnSaRefLevelUp.Click += new System.EventHandler(this.OnSaRefLevelUpClicked);
            // 
            // btnSaRefLevelDown
            // 
            this.btnSaRefLevelDown.Location = new System.Drawing.Point(285, 183);
            this.btnSaRefLevelDown.Name = "btnSaRefLevelDown";
            this.btnSaRefLevelDown.Size = new System.Drawing.Size(80, 49);
            this.btnSaRefLevelDown.TabIndex = 53;
            this.btnSaRefLevelDown.Text = "参考电平-5";
            this.btnSaRefLevelDown.UseVisualStyleBackColor = true;
            this.btnSaRefLevelDown.Click += new System.EventHandler(this.OnSaRefLevelDownClicked);
            // 
            // pnlSignalFlow
            // 
            this.pnlSignalFlow.BackColor = System.Drawing.Color.White;
            this.pnlSignalFlow.Location = new System.Drawing.Point(456, 248);
            this.pnlSignalFlow.LoOutputOn = false;
            this.pnlSignalFlow.LoState = A205AutoTestSystem.Common.InstrumentLinkState.Idle;
            this.pnlSignalFlow.Name = "pnlSignalFlow";
            this.pnlSignalFlow.PowerOn = false;
            this.pnlSignalFlow.PowerState = A205AutoTestSystem.Common.InstrumentLinkState.Idle;
            this.pnlSignalFlow.ReceiverState = A205AutoTestSystem.Common.InstrumentLinkState.Idle;
            this.pnlSignalFlow.RfOutputOn = false;
            this.pnlSignalFlow.RfState = A205AutoTestSystem.Common.InstrumentLinkState.Idle;
            this.pnlSignalFlow.Size = new System.Drawing.Size(383, 199);
            this.pnlSignalFlow.SpectrumState = A205AutoTestSystem.Common.InstrumentLinkState.Idle;
            this.pnlSignalFlow.TabIndex = 54;
            this.pnlSignalFlow.TabStop = false;
            // 
            // ReceiverControlPanel
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.White;
            this.Controls.Add(this.grpInstrumentControl);
            this.Controls.Add(this.grpSerialConnection);
            this.Controls.Add(this.pnlSignalFlow);
            this.Controls.Add(this.grpParameterConfig);
            this.Controls.Add(this.grpInstrumentStatus);
            this.Controls.Add(this.grpCommunicationLog);
            this.Name = "ReceiverControlPanel";
            this.Size = new System.Drawing.Size(844, 718);
            this.grpSerialConnection.ResumeLayout(false);
            this.grpParameterConfig.ResumeLayout(false);
            this.pnlTempGroup.ResumeLayout(false);
            this.pnlTempGroup.PerformLayout();
            this.pnlModeGroup.ResumeLayout(false);
            this.pnlModeGroup.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numAgcAtten)).EndInit();
            this.grpInstrumentStatus.ResumeLayout(false);
            this.grpCommunicationLog.ResumeLayout(false);
            this.grpInstrumentControl.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Button btnOpenPower;
        private System.Windows.Forms.GroupBox grpInstrumentControl;

        // 仪表控制区（频谱仪快捷按钮）
        private System.Windows.Forms.Button btnSaPeakSearch;
        private System.Windows.Forms.Button btnSaMarkerAllOff;
        private System.Windows.Forms.Button btnSaReset;
        private System.Windows.Forms.Button btnSaMaxHold;
        private System.Windows.Forms.Button btnSaPeakAuto;
        private System.Windows.Forms.Button btnSaBwAuto;
        private System.Windows.Forms.Button btnSaTraceAvg;
        private System.Windows.Forms.Button btnSaMinHold;
        private System.Windows.Forms.Button btnSaDeltaMarker;
        private System.Windows.Forms.Button btnSaHarmonics;
        private System.Windows.Forms.Button btnSaRefLevelUp;
        private System.Windows.Forms.Button btnSaRefLevelDown;

        // 信号链路拓扑面板（纯自绘，无事件订阅）
        private A205AutoTestSystem.UI.SignalFlowPanel pnlSignalFlow;
    }
}
