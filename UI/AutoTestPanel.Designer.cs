namespace A205AutoTestSystem.UI
{
    /// <summary>
    /// AutoTestPanel 的设计器文件（绝对定位布局）。
    /// 所有控件使用显式 Location/Size，可在 VS 设计器中自由拖拽、缩放调整。
    /// 动态测试项行由 AutoTestPanel.cs 的 AddTestRow 按固定列坐标添加到 grpTestItems。
    /// </summary>
    partial class AutoTestPanel
    {
        private System.ComponentModel.IContainer components = null;

        // ============================================================
        // 控件字段
        // ============================================================

        // 测试配置区
        private System.Windows.Forms.GroupBox grpTestConfig;
        private System.Windows.Forms.Label lblConfigChannel;
        private System.Windows.Forms.ComboBox cmbChannel;
        private System.Windows.Forms.Label lblConfigMode;
        private System.Windows.Forms.ComboBox cmbMode;
        private System.Windows.Forms.Label lblConfigTemp;
        private System.Windows.Forms.ComboBox cmbTemp;
        private System.Windows.Forms.Button btnInitEnv;
        private System.Windows.Forms.Button btnLoadTests;
        private System.Windows.Forms.Button btnExportExcel;

        // 测试项面板（表头 5 个标签；动态行直接添加到本 GroupBox）
        private System.Windows.Forms.GroupBox grpTestItems;
        private System.Windows.Forms.Label lblHeaderName;
        private System.Windows.Forms.Label lblHeaderAction;
        private System.Windows.Forms.Label lblHeaderUnit;
        private System.Windows.Forms.Label lblHeaderLimit;
        private System.Windows.Forms.Label lblHeaderStatus;

        // 进度与日志区
        private System.Windows.Forms.GroupBox grpProgressLog;
        private System.Windows.Forms.ProgressBar progressBar;
        private System.Windows.Forms.Label lblCurrentStep;
        private System.Windows.Forms.RichTextBox rtbLog;
        private System.Windows.Forms.Button btnClearLog;

        // 注：Dispose(bool) override 保留在 AutoTestPanel.cs 中，
        //     业务资源（Logger / 引擎 / 仪表）的清理与 components.Dispose 都在那里。

        #region Windows 窗体设计器生成的代码

        private void InitializeComponent()
        {
            this.grpTestConfig = new System.Windows.Forms.GroupBox();
            this.lblConfigChannel = new System.Windows.Forms.Label();
            this.cmbChannel = new System.Windows.Forms.ComboBox();
            this.lblConfigMode = new System.Windows.Forms.Label();
            this.cmbMode = new System.Windows.Forms.ComboBox();
            this.lblConfigTemp = new System.Windows.Forms.Label();
            this.cmbTemp = new System.Windows.Forms.ComboBox();
            this.btnInitEnv = new System.Windows.Forms.Button();
            this.btnLoadTests = new System.Windows.Forms.Button();
            this.btnExportExcel = new System.Windows.Forms.Button();
            this.grpTestItems = new System.Windows.Forms.GroupBox();
            this.lblHeaderName = new System.Windows.Forms.Label();
            this.lblHeaderAction = new System.Windows.Forms.Label();
            this.lblHeaderUnit = new System.Windows.Forms.Label();
            this.lblHeaderLimit = new System.Windows.Forms.Label();
            this.lblHeaderStatus = new System.Windows.Forms.Label();
            this.grpProgressLog = new System.Windows.Forms.GroupBox();
            this.progressBar = new System.Windows.Forms.ProgressBar();
            this.lblCurrentStep = new System.Windows.Forms.Label();
            this.rtbLog = new System.Windows.Forms.RichTextBox();
            this.btnClearLog = new System.Windows.Forms.Button();
            this.grpTestConfig.SuspendLayout();
            this.grpTestItems.SuspendLayout();
            this.grpProgressLog.SuspendLayout();
            this.SuspendLayout();
            // 
            // grpTestConfig
            // 
            this.grpTestConfig.Controls.Add(this.lblConfigChannel);
            this.grpTestConfig.Controls.Add(this.cmbChannel);
            this.grpTestConfig.Controls.Add(this.lblConfigMode);
            this.grpTestConfig.Controls.Add(this.cmbMode);
            this.grpTestConfig.Controls.Add(this.lblConfigTemp);
            this.grpTestConfig.Controls.Add(this.cmbTemp);
            this.grpTestConfig.Controls.Add(this.btnInitEnv);
            this.grpTestConfig.Controls.Add(this.btnLoadTests);
            this.grpTestConfig.Controls.Add(this.btnExportExcel);
            this.grpTestConfig.Location = new System.Drawing.Point(0, 8);
            this.grpTestConfig.Name = "grpTestConfig";
            this.grpTestConfig.Padding = new System.Windows.Forms.Padding(10, 5, 10, 10);
            this.grpTestConfig.Size = new System.Drawing.Size(841, 68);
            this.grpTestConfig.TabIndex = 0;
            this.grpTestConfig.TabStop = false;
            this.grpTestConfig.Text = "测试配置";
            // 
            // lblConfigChannel
            // 
            this.lblConfigChannel.Location = new System.Drawing.Point(14, 29);
            this.lblConfigChannel.Name = "lblConfigChannel";
            this.lblConfigChannel.Size = new System.Drawing.Size(43, 21);
            this.lblConfigChannel.TabIndex = 0;
            this.lblConfigChannel.Text = "通道";
            this.lblConfigChannel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // cmbChannel
            // 
            this.cmbChannel.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbChannel.Location = new System.Drawing.Point(63, 27);
            this.cmbChannel.Name = "cmbChannel";
            this.cmbChannel.Size = new System.Drawing.Size(100, 23);
            this.cmbChannel.TabIndex = 1;
            // 
            // lblConfigMode
            // 
            this.lblConfigMode.Location = new System.Drawing.Point(174, 29);
            this.lblConfigMode.Name = "lblConfigMode";
            this.lblConfigMode.Size = new System.Drawing.Size(44, 20);
            this.lblConfigMode.TabIndex = 2;
            this.lblConfigMode.Text = "模式";
            this.lblConfigMode.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // cmbMode
            // 
            this.cmbMode.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbMode.Location = new System.Drawing.Point(224, 26);
            this.cmbMode.Name = "cmbMode";
            this.cmbMode.Size = new System.Drawing.Size(100, 23);
            this.cmbMode.TabIndex = 3;
            // 
            // lblConfigTemp
            // 
            this.lblConfigTemp.Location = new System.Drawing.Point(336, 29);
            this.lblConfigTemp.Name = "lblConfigTemp";
            this.lblConfigTemp.Size = new System.Drawing.Size(38, 21);
            this.lblConfigTemp.TabIndex = 4;
            this.lblConfigTemp.Text = "温度";
            this.lblConfigTemp.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // cmbTemp
            // 
            this.cmbTemp.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbTemp.Location = new System.Drawing.Point(380, 26);
            this.cmbTemp.Name = "cmbTemp";
            this.cmbTemp.Size = new System.Drawing.Size(100, 23);
            this.cmbTemp.TabIndex = 5;
            // 
            // btnInitEnv
            // 
            this.btnInitEnv.Location = new System.Drawing.Point(486, 24);
            this.btnInitEnv.Name = "btnInitEnv";
            this.btnInitEnv.Size = new System.Drawing.Size(77, 28);
            this.btnInitEnv.TabIndex = 6;
            this.btnInitEnv.Text = "初始化";
            this.btnInitEnv.UseVisualStyleBackColor = true;
            this.btnInitEnv.Click += new System.EventHandler(this.OnInitEnvClicked);
            // 
            // btnLoadTests
            // 
            this.btnLoadTests.Location = new System.Drawing.Point(567, 24);
            this.btnLoadTests.Name = "btnLoadTests";
            this.btnLoadTests.Size = new System.Drawing.Size(95, 28);
            this.btnLoadTests.TabIndex = 7;
            this.btnLoadTests.Text = "加载测试项";
            this.btnLoadTests.UseVisualStyleBackColor = true;
            this.btnLoadTests.Click += new System.EventHandler(this.OnLoadTestsClicked);
            // 
            // btnExportExcel
            // 
            this.btnExportExcel.Enabled = false;
            this.btnExportExcel.Location = new System.Drawing.Point(668, 25);
            this.btnExportExcel.Name = "btnExportExcel";
            this.btnExportExcel.Size = new System.Drawing.Size(48, 28);
            this.btnExportExcel.TabIndex = 8;
            this.btnExportExcel.Text = "导出";
            this.btnExportExcel.UseVisualStyleBackColor = true;
            this.btnExportExcel.Click += new System.EventHandler(this.OnExportExcelClicked);
            // 
            // grpTestItems
            // 
            this.grpTestItems.Controls.Add(this.lblHeaderName);
            this.grpTestItems.Controls.Add(this.lblHeaderAction);
            this.grpTestItems.Controls.Add(this.lblHeaderUnit);
            this.grpTestItems.Controls.Add(this.lblHeaderLimit);
            this.grpTestItems.Controls.Add(this.lblHeaderStatus);
            this.grpTestItems.Location = new System.Drawing.Point(0, 82);
            this.grpTestItems.Name = "grpTestItems";
            this.grpTestItems.Padding = new System.Windows.Forms.Padding(10, 5, 10, 10);
            this.grpTestItems.Size = new System.Drawing.Size(841, 301);
            this.grpTestItems.TabIndex = 1;
            this.grpTestItems.TabStop = false;
            this.grpTestItems.Text = "测试项目";
            // 
            // lblHeaderName
            // 
            this.lblHeaderName.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Bold);
            this.lblHeaderName.Location = new System.Drawing.Point(14, 27);
            this.lblHeaderName.Name = "lblHeaderName";
            this.lblHeaderName.Size = new System.Drawing.Size(200, 24);
            this.lblHeaderName.TabIndex = 0;
            this.lblHeaderName.Text = "测试项目";
            this.lblHeaderName.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // lblHeaderAction
            // 
            this.lblHeaderAction.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Bold);
            this.lblHeaderAction.Location = new System.Drawing.Point(220, 27);
            this.lblHeaderAction.Name = "lblHeaderAction";
            this.lblHeaderAction.Size = new System.Drawing.Size(90, 24);
            this.lblHeaderAction.TabIndex = 1;
            this.lblHeaderAction.Text = "操作";
            this.lblHeaderAction.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // lblHeaderUnit
            // 
            this.lblHeaderUnit.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Bold);
            this.lblHeaderUnit.Location = new System.Drawing.Point(318, 27);
            this.lblHeaderUnit.Name = "lblHeaderUnit";
            this.lblHeaderUnit.Size = new System.Drawing.Size(70, 24);
            this.lblHeaderUnit.TabIndex = 2;
            this.lblHeaderUnit.Text = "单位";
            this.lblHeaderUnit.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // lblHeaderLimit
            // 
            this.lblHeaderLimit.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Bold);
            this.lblHeaderLimit.Location = new System.Drawing.Point(396, 27);
            this.lblHeaderLimit.Name = "lblHeaderLimit";
            this.lblHeaderLimit.Size = new System.Drawing.Size(200, 24);
            this.lblHeaderLimit.TabIndex = 3;
            this.lblHeaderLimit.Text = "阈值区间";
            this.lblHeaderLimit.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // lblHeaderStatus
            // 
            this.lblHeaderStatus.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Bold);
            this.lblHeaderStatus.Location = new System.Drawing.Point(604, 27);
            this.lblHeaderStatus.Name = "lblHeaderStatus";
            this.lblHeaderStatus.Size = new System.Drawing.Size(400, 24);
            this.lblHeaderStatus.TabIndex = 4;
            this.lblHeaderStatus.Text = "状态";
            this.lblHeaderStatus.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // grpProgressLog
            // 
            this.grpProgressLog.Controls.Add(this.progressBar);
            this.grpProgressLog.Controls.Add(this.lblCurrentStep);
            this.grpProgressLog.Controls.Add(this.rtbLog);
            this.grpProgressLog.Controls.Add(this.btnClearLog);
            this.grpProgressLog.Location = new System.Drawing.Point(0, 389);
            this.grpProgressLog.Name = "grpProgressLog";
            this.grpProgressLog.Padding = new System.Windows.Forms.Padding(10, 5, 10, 10);
            this.grpProgressLog.Size = new System.Drawing.Size(841, 326);
            this.grpProgressLog.TabIndex = 2;
            this.grpProgressLog.TabStop = false;
            this.grpProgressLog.Text = "日志";
            // 
            // progressBar
            // 
            this.progressBar.Location = new System.Drawing.Point(13, 26);
            this.progressBar.Name = "progressBar";
            this.progressBar.Size = new System.Drawing.Size(815, 25);
            this.progressBar.TabIndex = 0;
            // 
            // lblCurrentStep
            // 
            this.lblCurrentStep.ForeColor = System.Drawing.Color.DarkBlue;
            this.lblCurrentStep.Location = new System.Drawing.Point(12, 58);
            this.lblCurrentStep.Name = "lblCurrentStep";
            this.lblCurrentStep.Size = new System.Drawing.Size(816, 24);
            this.lblCurrentStep.TabIndex = 1;
            this.lblCurrentStep.Text = "当前步骤：（无）";
            this.lblCurrentStep.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // rtbLog
            // 
            this.rtbLog.BackColor = System.Drawing.Color.Black;
            this.rtbLog.DetectUrls = false;
            this.rtbLog.Font = new System.Drawing.Font("Consolas", 9F);
            this.rtbLog.ForeColor = System.Drawing.Color.LightGreen;
            this.rtbLog.Location = new System.Drawing.Point(4, 83);
            this.rtbLog.Name = "rtbLog";
            this.rtbLog.ReadOnly = true;
            this.rtbLog.Size = new System.Drawing.Size(835, 215);
            this.rtbLog.TabIndex = 2;
            this.rtbLog.Text = "";
            // 
            // btnClearLog
            // 
            this.btnClearLog.Location = new System.Drawing.Point(769, 298);
            this.btnClearLog.Name = "btnClearLog";
            this.btnClearLog.Size = new System.Drawing.Size(70, 26);
            this.btnClearLog.TabIndex = 3;
            this.btnClearLog.Text = "清除";
            this.btnClearLog.UseVisualStyleBackColor = true;
            this.btnClearLog.Click += new System.EventHandler(this.OnBtnClearLogClicked);
            // 
            // AutoTestPanel
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.grpTestConfig);
            this.Controls.Add(this.grpTestItems);
            this.Controls.Add(this.grpProgressLog);
            this.Name = "AutoTestPanel";
            this.Size = new System.Drawing.Size(844, 718);
            this.grpTestConfig.ResumeLayout(false);
            this.grpTestItems.ResumeLayout(false);
            this.grpProgressLog.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion
    }
}
