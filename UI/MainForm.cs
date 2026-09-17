using System.Windows.Forms;

namespace A205AutoTestSystem.UI
{
    /// <summary>
    /// A205 自动测试系统主窗体。
    /// <para>结构：TabControl 容器，包含"接收机控制"与"自动测试"两个 Tab 页。</para>
    /// <para>控件创建已移至 <c>MainForm.Designer.cs</c> 的 <c>InitializeComponent()</c>，
    /// 可在 VS 设计器中可视化编辑。</para>
    /// </summary>
    public partial class MainForm : Form
    {
        public MainForm()
        {
            InitializeComponent();
        }
    }
}