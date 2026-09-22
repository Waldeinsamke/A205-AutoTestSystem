using System;
using System.Windows.Forms;
using A205AutoTestSystem.InstrumentControl;
using A205AutoTestSystem.UI;

namespace A205自动测试系统
{
    internal static class Program
    {
        /// <summary>
        /// 应用程序的主入口点。
        /// </summary>
        [STAThread]
        private static void Main()
        {
            // 注册生产环境默认 VISA 传输工厂。
            // 测试覆盖：M4 阶段 UnitTest 通过 SetTransport() 注入 FakeVisaInstrument，
            // 不调用此工厂，因此测试工程不需要引用 NationalInstruments.Visa.dll。
            VisaBaseInstrument.DefaultTransportFactory =
                address => new NationalInstrumentsVisaTransport(address);

            // .NET 8 由源生成器 ApplicationConfiguration 自动生成
            // EnableVisualStyles / SetCompatibleTextRenderingDefault / SetHighDpiMode(PerMonitorV2) 调用。
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
        }
    }
}
