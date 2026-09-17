using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using A205AutoTestSystem.InstrumentControl;
using A205AutoTestSystem.UI;

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
            // 注册生产环境默认 VISA 传输工厂。
            // 测试覆盖：M4 阶段 UnitTest 通过 SetTransport() 注入 FakeVisaInstrument，
            // 不调用此工厂，因此测试工程不需要引用 NationalInstruments.Visa.dll。
            VisaBaseInstrument.DefaultTransportFactory =
                address => new NationalInstrumentsVisaTransport(address);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
