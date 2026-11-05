using A205AutoTestSystem.Common;
using A205AutoTestSystem.InstrumentControl;
using A205AutoTestSystem.UI;
using System;
using System.Windows.Forms;

namespace A205自动测试系统
{
    static class Program
    {
        /// <summary>
        /// 应用程序的主入口点。
        /// </summary>
        [STAThread]
        static void Main()
        {
            // 注册全局异常处理器：async void 事件处理器的异常、未捕获的 UI 线程异常
            // 不再静默消失；至少写盘 + 弹提示。
            // AppDomain.UnhandledException 只写日志不弹窗——它会在非 UI 线程触发，
            // 弹窗会阻塞。
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) =>
            {
                Logger.Log($"[Unhandled] ThreadException: {e.Exception}");
                MessageBox.Show(e.Exception.ToString(), "程序异常",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                Logger.Log($"[Unhandled] AppDomain: {e.ExceptionObject}");
            };

            // 注册生产环境默认 VISA 传输工厂。
            // 测试覆盖：通过 SetTransport() 注入测试工程内的伪传输，
            // 不调用此工厂，因此测试工程不需要引用 NationalInstruments.Visa.dll。
            VisaBaseInstrument.DefaultTransportFactory =
                address => new NationalInstrumentsVisaTransport(address);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
