using System.ComponentModel;

namespace A205AutoTestSystem.UI
{
    /// <summary>
    /// MainForm 的设计器文件。由 VS 设计器生成，源代码中通常不应手动编辑。
    /// 修改控件属性请使用 VS 设计器（双击 MainForm.cs）。
    /// </summary>
    partial class MainForm
    {
        private IContainer components = null;

        private System.Windows.Forms.TabControl tabControl;
        private System.Windows.Forms.TabPage tabReceiverControl;
        private System.Windows.Forms.TabPage tabAutoTest;
        private ReceiverControlPanel receiverControlPanel;
        private AutoTestPanel autoTestPanel;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows 窗体设计器生成的代码

        private void InitializeComponent()
        {
            this.tabControl = new System.Windows.Forms.TabControl();
            this.tabReceiverControl = new System.Windows.Forms.TabPage();
            this.tabAutoTest = new System.Windows.Forms.TabPage();
            this.receiverControlPanel = new A205AutoTestSystem.UI.ReceiverControlPanel();
            this.autoTestPanel = new A205AutoTestSystem.UI.AutoTestPanel();
            this.tabControl.SuspendLayout();
            this.tabReceiverControl.SuspendLayout();
            this.tabAutoTest.SuspendLayout();
            this.SuspendLayout();
            // 
            // tabControl
            // 
            this.tabControl.Controls.Add(this.tabReceiverControl);
            this.tabControl.Controls.Add(this.tabAutoTest);
            this.tabControl.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tabControl.Location = new System.Drawing.Point(0, 0);
            this.tabControl.Name = "tabControl";
            this.tabControl.SelectedIndex = 0;
            this.tabControl.Size = new System.Drawing.Size(1114, 713);
            this.tabControl.TabIndex = 0;
            // 
            // tabReceiverControl
            // 
            this.tabReceiverControl.Controls.Add(this.receiverControlPanel);
            this.tabReceiverControl.Location = new System.Drawing.Point(4, 25);
            this.tabReceiverControl.Name = "tabReceiverControl";
            this.tabReceiverControl.Padding = new System.Windows.Forms.Padding(3);
            this.tabReceiverControl.Size = new System.Drawing.Size(1106, 684);
            this.tabReceiverControl.TabIndex = 0;
            this.tabReceiverControl.Text = "接收机控制";
            this.tabReceiverControl.UseVisualStyleBackColor = true;
            // 
            // tabAutoTest
            // 
            this.tabAutoTest.Controls.Add(this.autoTestPanel);
            this.tabAutoTest.Location = new System.Drawing.Point(4, 25);
            this.tabAutoTest.Name = "tabAutoTest";
            this.tabAutoTest.Padding = new System.Windows.Forms.Padding(3);
            this.tabAutoTest.Size = new System.Drawing.Size(1106, 657);
            this.tabAutoTest.TabIndex = 1;
            this.tabAutoTest.Text = "自动测试";
            this.tabAutoTest.UseVisualStyleBackColor = true;
            // 
            // receiverControlPanel
            // 
            this.receiverControlPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.receiverControlPanel.Location = new System.Drawing.Point(3, 3);
            this.receiverControlPanel.Name = "receiverControlPanel";
            this.receiverControlPanel.Size = new System.Drawing.Size(1100, 678);
            this.receiverControlPanel.TabIndex = 0;
            // 
            // autoTestPanel
            // 
            this.autoTestPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.autoTestPanel.Location = new System.Drawing.Point(3, 3);
            this.autoTestPanel.Name = "autoTestPanel";
            this.autoTestPanel.Size = new System.Drawing.Size(1100, 651);
            this.autoTestPanel.TabIndex = 0;
            // 
            // MainForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1114, 713);
            this.Controls.Add(this.tabControl);
            this.MinimumSize = new System.Drawing.Size(912, 639);
            this.Name = "MainForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "A205 自动测试系统";
            this.tabControl.ResumeLayout(false);
            this.tabReceiverControl.ResumeLayout(false);
            this.tabAutoTest.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion
    }
}