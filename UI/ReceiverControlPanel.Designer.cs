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
        private System.Windows.Forms.ComboBox cmbChannel;
        private System.Windows.Forms.Label lblChannelSelect;
        private System.Windows.Forms.Label lblChannelHint;
        private System.Windows.Forms.RadioButton rbMode1;
        private System.Windows.Forms.RadioButton rbMode2;
        private System.Windows.Forms.RadioButton rbMode3;
        private System.Windows.Forms.Button btnModeSet;
        private System.Windows.Forms.Label lblModeSelect;
        private System.Windows.Forms.ComboBox cmbBand;
        private System.Windows.Forms.Button btnBandSet;
        private System.Windows.Forms.Label lblBandSelect;
        private System.Windows.Forms.Label lblBandHint;
        private System.Windows.Forms.NumericUpDown numAgcAtten;
        private System.Windows.Forms.Button btnAgcSet;
        private System.Windows.Forms.Label lblAgcUnit;
        private System.Windows.Forms.Label lblAgcAtten;

        // 仪表状态区
        private System.Windows.Forms.GroupBox grpInstrumentStatus;
        private System.Windows.Forms.Panel pnlRfStatus;
        private System.Windows.Forms.Label lblRf;
        private System.Windows.Forms.TextBox txtRfAddress;
        private System.Windows.Forms.Button btnRfConnect;
        private System.Windows.Forms.Button btnRfDisconnect;
        private System.Windows.Forms.Panel pnlLoStatus;
        private System.Windows.Forms.Label lblLo;
        private System.Windows.Forms.TextBox txtLoAddress;
        private System.Windows.Forms.Button btnLoConnect;
        private System.Windows.Forms.Button btnLoDisconnect;
        private System.Windows.Forms.Panel pnlSaStatus;
        private System.Windows.Forms.Label lblSa;
        private System.Windows.Forms.TextBox txtSaAddress;
        private System.Windows.Forms.Button btnSaConnect;
        private System.Windows.Forms.Button btnSaDisconnect;
        private System.Windows.Forms.Panel pnlPowerStatus;
        private System.Windows.Forms.Label lblPower;
        private System.Windows.Forms.TextBox txtPowerAddress;
        private System.Windows.Forms.Button btnPowerConnect;
        private System.Windows.Forms.Button btnPowerDisconnect;

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
            this.lblChannelHint = new System.Windows.Forms.Label();
            this.lblModeSelect = new System.Windows.Forms.Label();
            this.pnlModeGroup = new System.Windows.Forms.Panel();
            this.rbMode1 = new System.Windows.Forms.RadioButton();
            this.rbMode2 = new System.Windows.Forms.RadioButton();
            this.rbMode3 = new System.Windows.Forms.RadioButton();
            this.btnModeSet = new System.Windows.Forms.Button();
            this.lblBandSelect = new System.Windows.Forms.Label();
            this.cmbBand = new System.Windows.Forms.ComboBox();
            this.btnBandSet = new System.Windows.Forms.Button();
            this.lblBandHint = new System.Windows.Forms.Label();
            this.lblAgcAtten = new System.Windows.Forms.Label();
            this.numAgcAtten = new System.Windows.Forms.NumericUpDown();
            this.btnAgcSet = new System.Windows.Forms.Button();
            this.lblAgcUnit = new System.Windows.Forms.Label();
            this.grpInstrumentStatus = new System.Windows.Forms.GroupBox();
            this.pnlRfStatus = new System.Windows.Forms.Panel();
            this.lblRf = new System.Windows.Forms.Label();
            this.txtRfAddress = new System.Windows.Forms.TextBox();
            this.btnRfConnect = new System.Windows.Forms.Button();
            this.btnRfDisconnect = new System.Windows.Forms.Button();
            this.pnlLoStatus = new System.Windows.Forms.Panel();
            this.lblLo = new System.Windows.Forms.Label();
            this.txtLoAddress = new System.Windows.Forms.TextBox();
            this.btnLoConnect = new System.Windows.Forms.Button();
            this.btnLoDisconnect = new System.Windows.Forms.Button();
            this.pnlSaStatus = new System.Windows.Forms.Panel();
            this.lblSa = new System.Windows.Forms.Label();
            this.txtSaAddress = new System.Windows.Forms.TextBox();
            this.btnSaConnect = new System.Windows.Forms.Button();
            this.btnSaDisconnect = new System.Windows.Forms.Button();
            this.pnlPowerStatus = new System.Windows.Forms.Panel();
            this.lblPower = new System.Windows.Forms.Label();
            this.txtPowerAddress = new System.Windows.Forms.TextBox();
            this.btnPowerConnect = new System.Windows.Forms.Button();
            this.btnPowerDisconnect = new System.Windows.Forms.Button();
            this.grpCommunicationLog = new System.Windows.Forms.GroupBox();
            this.rtbLog = new System.Windows.Forms.RichTextBox();
            this.btnLogClear = new System.Windows.Forms.Button();
            this.btnOpenPower = new System.Windows.Forms.Button();
            this.grpSerialConnection.SuspendLayout();
            this.grpParameterConfig.SuspendLayout();
            this.pnlTempGroup.SuspendLayout();
            this.pnlModeGroup.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numAgcAtten)).BeginInit();
            this.grpInstrumentStatus.SuspendLayout();
            this.grpCommunicationLog.SuspendLayout();
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
            this.grpSerialConnection.Size = new System.Drawing.Size(662, 86);
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
            this.cmbReceiverPort.Size = new System.Drawing.Size(101, 23);
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
            this.cmbReceiverBaud.Location = new System.Drawing.Point(209, 21);
            this.cmbReceiverBaud.Name = "cmbReceiverBaud";
            this.cmbReceiverBaud.Size = new System.Drawing.Size(84, 23);
            this.cmbReceiverBaud.TabIndex = 2;
            // 
            // btnReceiverConnect
            // 
            this.btnReceiverConnect.Location = new System.Drawing.Point(299, 20);
            this.btnReceiverConnect.Name = "btnReceiverConnect";
            this.btnReceiverConnect.Size = new System.Drawing.Size(72, 26);
            this.btnReceiverConnect.TabIndex = 3;
            this.btnReceiverConnect.Text = "连接";
            this.btnReceiverConnect.UseVisualStyleBackColor = true;
            this.btnReceiverConnect.Click += new System.EventHandler(this.OnReceiverConnectClicked);
            // 
            // btnReceiverDisconnect
            // 
            this.btnReceiverDisconnect.Location = new System.Drawing.Point(377, 20);
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
            this.cmbMatrixPort.Size = new System.Drawing.Size(101, 23);
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
            this.cmbMatrixBaud.Location = new System.Drawing.Point(209, 53);
            this.cmbMatrixBaud.Name = "cmbMatrixBaud";
            this.cmbMatrixBaud.Size = new System.Drawing.Size(84, 23);
            this.cmbMatrixBaud.TabIndex = 7;
            // 
            // btnMatrixConnect
            // 
            this.btnMatrixConnect.Location = new System.Drawing.Point(299, 52);
            this.btnMatrixConnect.Name = "btnMatrixConnect";
            this.btnMatrixConnect.Size = new System.Drawing.Size(72, 26);
            this.btnMatrixConnect.TabIndex = 8;
            this.btnMatrixConnect.Text = "连接";
            this.btnMatrixConnect.UseVisualStyleBackColor = true;
            this.btnMatrixConnect.Click += new System.EventHandler(this.OnMatrixConnectClicked);
            // 
            // btnMatrixDisconnect
            // 
            this.btnMatrixDisconnect.Location = new System.Drawing.Point(377, 52);
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
            this.grpParameterConfig.Controls.Add(this.lblChannelHint);
            this.grpParameterConfig.Controls.Add(this.lblModeSelect);
            this.grpParameterConfig.Controls.Add(this.pnlModeGroup);
            this.grpParameterConfig.Controls.Add(this.btnModeSet);
            this.grpParameterConfig.Controls.Add(this.lblBandSelect);
            this.grpParameterConfig.Controls.Add(this.cmbBand);
            this.grpParameterConfig.Controls.Add(this.btnBandSet);
            this.grpParameterConfig.Controls.Add(this.lblBandHint);
            this.grpParameterConfig.Controls.Add(this.lblAgcAtten);
            this.grpParameterConfig.Controls.Add(this.numAgcAtten);
            this.grpParameterConfig.Controls.Add(this.btnAgcSet);
            this.grpParameterConfig.Controls.Add(this.lblAgcUnit);
            this.grpParameterConfig.Location = new System.Drawing.Point(0, 100);
            this.grpParameterConfig.Name = "grpParameterConfig";
            this.grpParameterConfig.Padding = new System.Windows.Forms.Padding(10, 5, 10, 10);
            this.grpParameterConfig.Size = new System.Drawing.Size(841, 188);
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
            this.pnlTempGroup.Size = new System.Drawing.Size(211, 24);
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
            this.rbTempHigh.Location = new System.Drawing.Point(76, 2);
            this.rbTempHigh.Name = "rbTempHigh";
            this.rbTempHigh.Size = new System.Drawing.Size(58, 19);
            this.rbTempHigh.TabIndex = 2;
            this.rbTempHigh.Text = "高温";
            this.rbTempHigh.UseVisualStyleBackColor = true;
            // 
            // rbTempLow
            // 
            this.rbTempLow.AutoSize = true;
            this.rbTempLow.Location = new System.Drawing.Point(148, 2);
            this.rbTempLow.Name = "rbTempLow";
            this.rbTempLow.Size = new System.Drawing.Size(58, 19);
            this.rbTempLow.TabIndex = 3;
            this.rbTempLow.Text = "低温";
            this.rbTempLow.UseVisualStyleBackColor = true;
            // 
            // btnTempSet
            // 
            this.btnTempSet.Location = new System.Drawing.Point(333, 26);
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
            this.cmbChannel.Size = new System.Drawing.Size(211, 23);
            this.cmbChannel.TabIndex = 6;
            this.cmbChannel.SelectedIndexChanged += new System.EventHandler(this.OnChannelSelected);
            // 
            // lblChannelHint
            // 
            this.lblChannelHint.AutoSize = true;
            this.lblChannelHint.ForeColor = System.Drawing.Color.Gray;
            this.lblChannelHint.Location = new System.Drawing.Point(321, 64);
            this.lblChannelHint.Name = "lblChannelHint";
            this.lblChannelHint.Size = new System.Drawing.Size(202, 15);
            this.lblChannelHint.TabIndex = 7;
            this.lblChannelHint.Text = "（选择后自动联动矩阵切换）";
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
            this.pnlModeGroup.Size = new System.Drawing.Size(223, 24);
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
            this.rbMode2.Location = new System.Drawing.Point(75, 2);
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
            this.rbMode3.Location = new System.Drawing.Point(147, 2);
            this.rbMode3.Name = "rbMode3";
            this.rbMode3.Size = new System.Drawing.Size(68, 19);
            this.rbMode3.TabIndex = 11;
            this.rbMode3.Text = "Mode3";
            this.rbMode3.UseVisualStyleBackColor = true;
            this.rbMode3.CheckedChanged += new System.EventHandler(this.OnModeChanged);
            // 
            // btnModeSet
            // 
            this.btnModeSet.Location = new System.Drawing.Point(333, 90);
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
            this.cmbBand.Size = new System.Drawing.Size(211, 23);
            this.cmbBand.TabIndex = 14;
            // 
            // btnBandSet
            // 
            this.btnBandSet.Location = new System.Drawing.Point(333, 123);
            this.btnBandSet.Name = "btnBandSet";
            this.btnBandSet.Size = new System.Drawing.Size(72, 26);
            this.btnBandSet.TabIndex = 15;
            this.btnBandSet.Text = "设置";
            this.btnBandSet.UseVisualStyleBackColor = true;
            this.btnBandSet.Click += new System.EventHandler(this.OnBandSetClicked);
            // 
            // lblBandHint
            // 
            this.lblBandHint.AutoSize = true;
            this.lblBandHint.ForeColor = System.Drawing.Color.Gray;
            this.lblBandHint.Location = new System.Drawing.Point(413, 129);
            this.lblBandHint.Name = "lblBandHint";
            this.lblBandHint.Size = new System.Drawing.Size(172, 15);
            this.lblBandHint.TabIndex = 16;
            this.lblBandHint.Text = "（依当前模式动态填充）";
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
            this.numAgcAtten.Size = new System.Drawing.Size(211, 25);
            this.numAgcAtten.TabIndex = 18;
            // 
            // btnAgcSet
            // 
            this.btnAgcSet.Location = new System.Drawing.Point(333, 154);
            this.btnAgcSet.Name = "btnAgcSet";
            this.btnAgcSet.Size = new System.Drawing.Size(72, 26);
            this.btnAgcSet.TabIndex = 19;
            this.btnAgcSet.Text = "设置";
            this.btnAgcSet.UseVisualStyleBackColor = true;
            this.btnAgcSet.Click += new System.EventHandler(this.OnAgcSetClicked);
            // 
            // lblAgcUnit
            // 
            this.lblAgcUnit.AutoSize = true;
            this.lblAgcUnit.ForeColor = System.Drawing.Color.Gray;
            this.lblAgcUnit.Location = new System.Drawing.Point(416, 160);
            this.lblAgcUnit.Name = "lblAgcUnit";
            this.lblAgcUnit.Size = new System.Drawing.Size(107, 15);
            this.lblAgcUnit.TabIndex = 20;
            this.lblAgcUnit.Text = "（步进 4 dB）";
            // 
            // grpInstrumentStatus
            // 
            this.grpInstrumentStatus.Controls.Add(this.pnlRfStatus);
            this.grpInstrumentStatus.Controls.Add(this.lblRf);
            this.grpInstrumentStatus.Controls.Add(this.txtRfAddress);
            this.grpInstrumentStatus.Controls.Add(this.btnRfConnect);
            this.grpInstrumentStatus.Controls.Add(this.btnRfDisconnect);
            this.grpInstrumentStatus.Controls.Add(this.pnlLoStatus);
            this.grpInstrumentStatus.Controls.Add(this.lblLo);
            this.grpInstrumentStatus.Controls.Add(this.txtLoAddress);
            this.grpInstrumentStatus.Controls.Add(this.btnLoConnect);
            this.grpInstrumentStatus.Controls.Add(this.btnLoDisconnect);
            this.grpInstrumentStatus.Controls.Add(this.pnlSaStatus);
            this.grpInstrumentStatus.Controls.Add(this.lblSa);
            this.grpInstrumentStatus.Controls.Add(this.txtSaAddress);
            this.grpInstrumentStatus.Controls.Add(this.btnSaConnect);
            this.grpInstrumentStatus.Controls.Add(this.btnSaDisconnect);
            this.grpInstrumentStatus.Controls.Add(this.pnlPowerStatus);
            this.grpInstrumentStatus.Controls.Add(this.lblPower);
            this.grpInstrumentStatus.Controls.Add(this.txtPowerAddress);
            this.grpInstrumentStatus.Controls.Add(this.btnPowerConnect);
            this.grpInstrumentStatus.Controls.Add(this.btnPowerDisconnect);
            this.grpInstrumentStatus.Location = new System.Drawing.Point(0, 294);
            this.grpInstrumentStatus.Name = "grpInstrumentStatus";
            this.grpInstrumentStatus.Padding = new System.Windows.Forms.Padding(10, 5, 10, 10);
            this.grpInstrumentStatus.Size = new System.Drawing.Size(841, 153);
            this.grpInstrumentStatus.TabIndex = 2;
            this.grpInstrumentStatus.TabStop = false;
            this.grpInstrumentStatus.Text = "仪表连接状态";
            // 
            // pnlRfStatus
            // 
            this.pnlRfStatus.BackColor = System.Drawing.Color.Red;
            this.pnlRfStatus.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.pnlRfStatus.Location = new System.Drawing.Point(15, 27);
            this.pnlRfStatus.Margin = new System.Windows.Forms.Padding(6);
            this.pnlRfStatus.Name = "pnlRfStatus";
            this.pnlRfStatus.Size = new System.Drawing.Size(16, 16);
            this.pnlRfStatus.TabIndex = 0;
            // 
            // lblRf
            // 
            this.lblRf.Location = new System.Drawing.Point(37, 24);
            this.lblRf.Name = "lblRf";
            this.lblRf.Size = new System.Drawing.Size(106, 24);
            this.lblRf.TabIndex = 1;
            this.lblRf.Text = "RF信号源地址";
            this.lblRf.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // txtRfAddress
            // 
            this.txtRfAddress.Location = new System.Drawing.Point(145, 24);
            this.txtRfAddress.Name = "txtRfAddress";
            this.txtRfAddress.Size = new System.Drawing.Size(280, 25);
            this.txtRfAddress.TabIndex = 3;
            this.txtRfAddress.Text = "USB0::0x0957::0x1F01::MY61252683::INSTR";
            // 
            // btnRfConnect
            // 
            this.btnRfConnect.Location = new System.Drawing.Point(435, 22);
            this.btnRfConnect.Name = "btnRfConnect";
            this.btnRfConnect.Size = new System.Drawing.Size(72, 26);
            this.btnRfConnect.TabIndex = 4;
            this.btnRfConnect.Text = "连接";
            this.btnRfConnect.UseVisualStyleBackColor = true;
            this.btnRfConnect.Click += new System.EventHandler(this.OnRfConnectClicked);
            // 
            // btnRfDisconnect
            // 
            this.btnRfDisconnect.Location = new System.Drawing.Point(513, 22);
            this.btnRfDisconnect.Name = "btnRfDisconnect";
            this.btnRfDisconnect.Size = new System.Drawing.Size(72, 26);
            this.btnRfDisconnect.TabIndex = 5;
            this.btnRfDisconnect.Text = "断开";
            this.btnRfDisconnect.UseVisualStyleBackColor = true;
            this.btnRfDisconnect.Click += new System.EventHandler(this.OnRfDisconnectClicked);
            // 
            // pnlLoStatus
            // 
            this.pnlLoStatus.BackColor = System.Drawing.Color.Red;
            this.pnlLoStatus.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.pnlLoStatus.Location = new System.Drawing.Point(15, 59);
            this.pnlLoStatus.Margin = new System.Windows.Forms.Padding(6);
            this.pnlLoStatus.Name = "pnlLoStatus";
            this.pnlLoStatus.Size = new System.Drawing.Size(16, 16);
            this.pnlLoStatus.TabIndex = 6;
            // 
            // lblLo
            // 
            this.lblLo.Location = new System.Drawing.Point(37, 56);
            this.lblLo.Name = "lblLo";
            this.lblLo.Size = new System.Drawing.Size(106, 24);
            this.lblLo.TabIndex = 7;
            this.lblLo.Text = "LO信号源地址";
            this.lblLo.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // txtLoAddress
            // 
            this.txtLoAddress.Location = new System.Drawing.Point(145, 56);
            this.txtLoAddress.Name = "txtLoAddress";
            this.txtLoAddress.Size = new System.Drawing.Size(280, 25);
            this.txtLoAddress.TabIndex = 9;
            this.txtLoAddress.Text = "USB0::0x0957::0x1F01::MY59101329::INSTR";
            // 
            // btnLoConnect
            // 
            this.btnLoConnect.Location = new System.Drawing.Point(435, 54);
            this.btnLoConnect.Name = "btnLoConnect";
            this.btnLoConnect.Size = new System.Drawing.Size(72, 26);
            this.btnLoConnect.TabIndex = 10;
            this.btnLoConnect.Text = "连接";
            this.btnLoConnect.UseVisualStyleBackColor = true;
            this.btnLoConnect.Click += new System.EventHandler(this.OnLoConnectClicked);
            // 
            // btnLoDisconnect
            // 
            this.btnLoDisconnect.Location = new System.Drawing.Point(513, 54);
            this.btnLoDisconnect.Name = "btnLoDisconnect";
            this.btnLoDisconnect.Size = new System.Drawing.Size(72, 26);
            this.btnLoDisconnect.TabIndex = 11;
            this.btnLoDisconnect.Text = "断开";
            this.btnLoDisconnect.UseVisualStyleBackColor = true;
            this.btnLoDisconnect.Click += new System.EventHandler(this.OnLoDisconnectClicked);
            // 
            // pnlSaStatus
            // 
            this.pnlSaStatus.BackColor = System.Drawing.Color.Red;
            this.pnlSaStatus.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.pnlSaStatus.Location = new System.Drawing.Point(15, 91);
            this.pnlSaStatus.Margin = new System.Windows.Forms.Padding(6);
            this.pnlSaStatus.Name = "pnlSaStatus";
            this.pnlSaStatus.Size = new System.Drawing.Size(16, 16);
            this.pnlSaStatus.TabIndex = 12;
            // 
            // lblSa
            // 
            this.lblSa.Location = new System.Drawing.Point(37, 88);
            this.lblSa.Name = "lblSa";
            this.lblSa.Size = new System.Drawing.Size(106, 24);
            this.lblSa.TabIndex = 13;
            this.lblSa.Text = "频谱仪地址";
            this.lblSa.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // txtSaAddress
            // 
            this.txtSaAddress.Location = new System.Drawing.Point(145, 88);
            this.txtSaAddress.Name = "txtSaAddress";
            this.txtSaAddress.Size = new System.Drawing.Size(280, 25);
            this.txtSaAddress.TabIndex = 15;
            this.txtSaAddress.Text = "USB0::0x0957::0x0A0B::MY53290027::INSTR";
            // 
            // btnSaConnect
            // 
            this.btnSaConnect.Location = new System.Drawing.Point(435, 86);
            this.btnSaConnect.Name = "btnSaConnect";
            this.btnSaConnect.Size = new System.Drawing.Size(72, 26);
            this.btnSaConnect.TabIndex = 16;
            this.btnSaConnect.Text = "连接";
            this.btnSaConnect.UseVisualStyleBackColor = true;
            this.btnSaConnect.Click += new System.EventHandler(this.OnSaConnectClicked);
            // 
            // btnSaDisconnect
            // 
            this.btnSaDisconnect.Location = new System.Drawing.Point(513, 86);
            this.btnSaDisconnect.Name = "btnSaDisconnect";
            this.btnSaDisconnect.Size = new System.Drawing.Size(72, 26);
            this.btnSaDisconnect.TabIndex = 17;
            this.btnSaDisconnect.Text = "断开";
            this.btnSaDisconnect.UseVisualStyleBackColor = true;
            this.btnSaDisconnect.Click += new System.EventHandler(this.OnSaDisconnectClicked);
            // 
            // pnlPowerStatus
            // 
            this.pnlPowerStatus.BackColor = System.Drawing.Color.Red;
            this.pnlPowerStatus.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.pnlPowerStatus.Location = new System.Drawing.Point(15, 123);
            this.pnlPowerStatus.Margin = new System.Windows.Forms.Padding(6);
            this.pnlPowerStatus.Name = "pnlPowerStatus";
            this.pnlPowerStatus.Size = new System.Drawing.Size(16, 16);
            this.pnlPowerStatus.TabIndex = 21;
            // 
            // lblPower
            // 
            this.lblPower.Location = new System.Drawing.Point(37, 120);
            this.lblPower.Name = "lblPower";
            this.lblPower.Size = new System.Drawing.Size(106, 24);
            this.lblPower.TabIndex = 22;
            this.lblPower.Text = "电源地址";
            this.lblPower.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // txtPowerAddress
            // 
            this.txtPowerAddress.Location = new System.Drawing.Point(145, 120);
            this.txtPowerAddress.Name = "txtPowerAddress";
            this.txtPowerAddress.Size = new System.Drawing.Size(280, 25);
            this.txtPowerAddress.TabIndex = 23;
            // 
            // btnPowerConnect
            // 
            this.btnPowerConnect.Location = new System.Drawing.Point(435, 118);
            this.btnPowerConnect.Name = "btnPowerConnect";
            this.btnPowerConnect.Size = new System.Drawing.Size(72, 26);
            this.btnPowerConnect.TabIndex = 24;
            this.btnPowerConnect.Text = "连接";
            this.btnPowerConnect.UseVisualStyleBackColor = true;
            this.btnPowerConnect.Click += new System.EventHandler(this.OnPowerConnectClicked);
            // 
            // btnPowerDisconnect
            // 
            this.btnPowerDisconnect.Location = new System.Drawing.Point(513, 118);
            this.btnPowerDisconnect.Name = "btnPowerDisconnect";
            this.btnPowerDisconnect.Size = new System.Drawing.Size(72, 26);
            this.btnPowerDisconnect.TabIndex = 25;
            this.btnPowerDisconnect.Text = "断开";
            this.btnPowerDisconnect.UseVisualStyleBackColor = true;
            this.btnPowerDisconnect.Click += new System.EventHandler(this.OnPowerDisconnectClicked);
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
            this.grpCommunicationLog.Text = "通信日志";
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
            this.btnOpenPower.Location = new System.Drawing.Point(668, 16);
            this.btnOpenPower.Name = "btnOpenPower";
            this.btnOpenPower.Size = new System.Drawing.Size(173, 78);
            this.btnOpenPower.TabIndex = 22;
            this.btnOpenPower.Text = "开启供电";
            this.btnOpenPower.UseVisualStyleBackColor = true;
            this.btnOpenPower.Click += new System.EventHandler(this.OnOpenPowerClicked);
            // 
            // ReceiverControlPanel
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.btnOpenPower);
            this.Controls.Add(this.grpSerialConnection);
            this.Controls.Add(this.grpParameterConfig);
            this.Controls.Add(this.grpInstrumentStatus);
            this.Controls.Add(this.grpCommunicationLog);
            this.Name = "ReceiverControlPanel";
            this.Size = new System.Drawing.Size(844, 718);
            this.grpSerialConnection.ResumeLayout(false);
            this.grpParameterConfig.ResumeLayout(false);
            this.grpParameterConfig.PerformLayout();
            this.pnlTempGroup.ResumeLayout(false);
            this.pnlTempGroup.PerformLayout();
            this.pnlModeGroup.ResumeLayout(false);
            this.pnlModeGroup.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numAgcAtten)).EndInit();
            this.grpInstrumentStatus.ResumeLayout(false);
            this.grpInstrumentStatus.PerformLayout();
            this.grpCommunicationLog.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Button btnOpenPower;
    }
}
