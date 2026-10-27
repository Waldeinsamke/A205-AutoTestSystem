using System;
using System.Reflection;
using System.Windows.Forms;
using A205AutoTestSystem.Common;

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

        /// <summary>
        /// 窗体关闭前的收口编排：停止测试 → 硬件收口 → 二次兜底关输出。
        /// <para><b>为什么必须有</b>：关闭 VISA session 只释放句柄，仪表侧的
        /// <b>远程模式锁定</b>（<c>SYSTem:REMote</c>）与<b>输出开关</b>（<c>:OUTP:STAT 1</c>）
        /// 是记在仪表里的状态，不显式收口就会残留——电源面板被锁住无法手动操作、
        /// 产品持续带电、信号源持续发射载波。</para>
        /// <para><b>时机</b>：<c>OnFormClosing</c> 在 <c>Dispose</c> 之前触发，
        /// 此时 VISA session 仍存活，SCPI 命令能真正发到仪表上。
        /// 若放到 <c>FormClosed</c> 则进程可能已退出，收口会落空。</para>
        /// <para><b>三步顺序不可调换</b>：</para>
        /// <list type="number">
        ///   <item><b>先取消运行中的测试</b>（<c>RequestStopAll</c>）。否则收口把 RF/LO
        ///     输出关掉之后，仍在运行的测试项会继续走到下一个频点并再次执行
        ///     <c>SetOutputState(true)</c>，使信号源在进程退出后仍残留载波输出。</item>
        ///   <item><b>再做硬件收口</b>（<c>ShutdownInstruments</c>）：关 RF/LO 输出、
        ///     关电源输出、交还电源本地面板控制。</item>
        ///   <item><b>最后二次兜底</b>（<c>EnsureSignalOutputsOff</c>）。取消是协作式的，
        ///     测试项要等到下一个检查点才真正停下，这个间隔不可控（一次频谱仪查询
        ///     最长可达默认 5 s 超时），因此收口之后测试项仍有可能再开一次输出。
        ///     收口完成后再确认一次，把最终状态钉死为"关闭"。</item>
        /// </list>
        /// <para><b>行为</b>：全程静默——异常只写日志，不弹窗、不阻塞关闭。</para>
        /// </summary>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // ① 先请求停止运行中的测试（不发信号源命令，不影响收口）
            try
            {
                autoTestPanel?.RequestStopAll();
            }
            catch (Exception ex)
            {
                Logger.Log("[Error] 退出前停止测试异常: " + ex.Message);
            }

            // ② 硬件收口：关 RF/LO 输出、关电源输出、交还电源本地面板控制
            try
            {
                receiverControlPanel?.ShutdownInstruments();
            }
            catch (Exception ex)
            {
                Logger.Log("[Error] 程序退出收口异常: " + ex.Message);
            }

            // ③ 二次兜底：收口与测试项存在竞态，这里再确认一次输出为关闭
            try
            {
                receiverControlPanel?.EnsureSignalOutputsOff();
            }
            catch (Exception ex)
            {
                Logger.Log("[Error] 退出前二次关输出异常: " + ex.Message);
            }

            base.OnFormClosing(e);
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
