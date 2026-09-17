using System.Drawing;
using System.Windows.Forms;

namespace A205AutoTestSystem.UI
{
    /// <summary>
    /// A205 自动测试系统主窗体。完全代码生成，无 Designer.cs / .resx。
    /// <para>结构：TabControl 容器，包含"接收机控制"与"自动测试"两个 Tab 页。</para>
    /// </summary>
    public class MainForm : Form
    {
        private TabControl _tabControl;
        private TabPage _tabReceiverControl;
        private TabPage _tabAutoTest;

        public MainForm()
        {
            Text = "A205 自动测试系统";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(800, 600);
            ClientSize = new Size(960, 640);
            AutoScaleMode = AutoScaleMode.Font;

            _tabControl = new TabControl
            {
                Dock = DockStyle.Fill
            };

            _tabReceiverControl = new TabPage("接收机控制")
            {
                Dock = DockStyle.Fill
            };

            _tabAutoTest = new TabPage("自动测试")
            {
                Dock = DockStyle.Fill
            };

            var receiverPanel = new ReceiverControlPanel
            {
                Dock = DockStyle.Fill
            };
            _tabReceiverControl.Controls.Add(receiverPanel);

            var autoTestPanel = new AutoTestPanel
            {
                Dock = DockStyle.Fill
            };
            _tabAutoTest.Controls.Add(autoTestPanel);

            _tabControl.TabPages.Add(_tabReceiverControl);
            _tabControl.TabPages.Add(_tabAutoTest);

            Controls.Add(_tabControl);
        }
    }
}