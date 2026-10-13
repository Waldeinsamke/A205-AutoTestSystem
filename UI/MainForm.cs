using System;
using System.Reflection;
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
            // 高 DPI（PerMonitorV2）下切换标签页时旧页面偶发像素残留，
            // 切换后强制新页面整棵控件子树立即重绘。
            tabControl.SelectedIndexChanged += OnSelectedTabChanged;
        }

        /// <summary>
        /// 接收机控制 Tab 引用。M6.x 阶段由 AutoTestPanel.AttachToReceiver 调用，
        /// 复用其内部的 _receiver / _matrix / 三仪表资源。
        /// </summary>
        public ReceiverControlPanel ReceiverPanel => receiverControlPanel;

        /// <summary>自动测试 Tab 引用。当前阶段未被外部调用，留扩展点。</summary>
        public AutoTestPanel TestPanel => autoTestPanel;

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            // TabControl 默认关闭双缓冲，高 DPI 切换时容器层易残影/闪烁，统一开启。
            EnableDoubleBuffered(tabControl);
            EnableDoubleBuffered(receiverControlPanel);
            EnableDoubleBuffered(autoTestPanel);

            // M6.x 阶段：把 receiver 控制面板的能力注入到自动测试面板，
            // 让"测试项 → 频段命令 → 频谱仪"共享同一组资源。
            // 『加载测试』按钮已迁到 AutoTestPanel（点击时主动从 _receiverPanel 拉三仪表引用），
            // 不再需要反向注入 _autoTestPanel。
            if (autoTestPanel != null && receiverControlPanel != null)
            {
                autoTestPanel.AttachToReceiver(receiverControlPanel);
            }
        }

        private void OnSelectedTabChanged(object sender, EventArgs e)
        {
            TabPage selected = tabControl.SelectedTab;
            if (selected == null) return;

            // ① 切换消息处理中：立即重绘托管层
            selected.Refresh();

            // ② 切换完成后（新页面 HWND 已完成显示）的下一个消息循环再强制重绘一次。
            // RichTextBox 是独立 HWND，其首帧 WM_PAINT 与 TabControl 页面显示操作
            // 交叠时会残留约半帧旧页面像素；此处重绘发生在显示完成之后，残影一帧内即被覆盖。
            BeginInvoke(new Action(() =>
            {
                if (!selected.IsDisposed && selected.Visible)
                {
                    selected.Refresh();
                }
            }));
        }

        /// <summary>
        /// 打开控件受保护的 <see cref="Control.DoubleBuffered"/> 属性
        /// （WinForms 未在设计器公开此开关，.NET Framework 下的标准做法）。
        /// </summary>
        private static void EnableDoubleBuffered(Control control)
        {
            typeof(Control).InvokeMember(
                "DoubleBuffered",
                BindingFlags.SetProperty | BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                control,
                new object[] { true });
        }
    }
}
