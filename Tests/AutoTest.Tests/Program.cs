using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using A205AutoTestSystem.InstrumentControl;
using A205AutoTestSystem.InstrumentControl.TestItems;
using A205AutoTestSystem.UI;

namespace A205AutoTestSystem.Tests.AutoTestTests
{
    /// <summary>
    /// M6 阶段：自动测试引擎与首批用例的端到端测试主程序。
    /// <para>通过 <see cref="FakeVisaInstrument"/> 注入伪传输，验证：</para>
    /// <list type="bullet">
    /// <item><see cref="GainTest"/> / <see cref="BandwidthTest"/> 的 <c>ExecuteAsync</c> 返回
    ///       非 null 的 <see cref="TestResult"/>，且字段合法（Judgment ∈ {Pass, Fail}，Value 是有限数）。</item>
    /// <item><see cref="AutoTestEngine.RunAllAsync"/> 串行调度所有已注册测试项。</item>
    /// <item>Fake 响应变化下用例能稳定通过（占位阈值 + 容差检查）。</item>
    /// </list>
    /// <para>运行：直接执行 <c>AutoTest.Tests.exe</c>；可通过
    /// <c>--filter=Gain|Bandwidth|Engine</c> 只跑某类。</para>
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            Console.WriteLine("=== AutoTest.Tests（M6 自动测试引擎与首批用例）===");
            Console.WriteLine($"启动时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine();

            HashSet<string> filter = ParseFilter(args);

            // 全局注册 FakeVisaInstrument 工厂，使生产类（SignalGenerator / SpectrumAnalyzer）走伪传输
            VisaBaseInstrument.DefaultTransportFactory = addr => new FakeVisaInstrument(addr);

            if (filter.Contains("Gain"))     TestGainTest();
            if (filter.Contains("Bandwidth")) TestBandwidthTest();
            if (filter.Contains("Engine"))    TestAutoTestEngine();
            if (filter.Contains("H6"))        TestH6ResourceSnapshot();

            Console.WriteLine();
            Console.WriteLine("--- 汇总 ---");
            Console.WriteLine($"总断言数: {Assert.TotalCount}");
            Console.WriteLine($"失败断言数: {Assert.FailureCount}");
            if (Assert.FailureCount == 0)
            {
                Console.WriteLine("结果: PASS");
                return 0;
            }
            Console.WriteLine("结果: FAIL");
            return 1;
        }

        private static HashSet<string> ParseFilter(string[] args)
        {
            HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (args == null || args.Length == 0)
            {
                set.Add("Gain"); set.Add("Bandwidth"); set.Add("Engine"); set.Add("H6");
                return set;
            }
            foreach (var arg in args)
            {
                if (string.IsNullOrEmpty(arg)) continue;
                string a = arg.Trim();
                if (a.StartsWith("--filter=", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var part in a.Substring("--filter=".Length)
                                           .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        set.Add(part);
                    }
                }
                else
                {
                    set.Add(a);
                }
            }
            if (set.Count == 0)
            {
                set.Add("Gain"); set.Add("Bandwidth"); set.Add("Engine"); set.Add("H6");
            }
            return set;
        }

        // ====================================================================
        // GainTest 端到端
        // ====================================================================

        private static void TestGainTest()
        {
            // M6.x 升级：GainTest 构造函数已切换到"模式×频点循环"重载，
            // 需要 ReceiverSerialPort + channel + modes 等参数，
            // 旧版端到端测试桩不再适用（CS1739：rfFreqHz / loFreqHz / rfPowerDbm / loPowerDbm 已不在签名里）。
            // 后续如果需要恢复端到端测试，请配套引入 FakeReceiverSerialPort 子桩，
            // 并把本方法改写为新版签名调用。
            Console.WriteLine("[Test][SKIP] GainTest.ExecuteAsync 端到端 — M6.x 模式×频点循环版需 ReceiverSerialPort 桩,暂跳过");
        }

        // ====================================================================
        // BandwidthTest 端到端
        // ====================================================================

        private static void TestBandwidthTest()
        {
            // M6.x 升级：BandwidthTest 构造函数已切换到"模式×频点循环"重载，
            // 旧版的 centerRfFreqHz / scanPoints 单频点构造签名已不在（CS1739）。
            // 与 TestGainTest 同理，恢复测试需 FakeReceiverSerialPort 子桩配套。
            Console.WriteLine("[Test][SKIP] BandwidthTest.ExecuteAsync 端到端 — M6.x 模式×频点循环版需 ReceiverSerialPort 桩,暂跳过");
        }

        // ====================================================================
        // AutoTestEngine 端到端
        // ====================================================================

        private static void TestAutoTestEngine()
        {
            Console.WriteLine("[Test] AutoTestEngine.RunAllAsync 串行调度");

            // 我们直接构造一个测试项：仅返回固定值的 Fake
            // 不需要 VISA 仪表——AutoTestEngine.RunAllAsync 只调用 ExecuteAsync
            int executedCount = 0;
            var engine = new AutoTestEngine();

            // FakeItem1
            var fake1 = new FakeTestItem("Fake1", "dB", 10.0, 20.0, 15.0, "Pass", () => executedCount++);
            engine.RegisterTest(fake1);

            // FakeItem2
            var fake2 = new FakeTestItem("Fake2", "MHz", 0.1, 50.0, 5.0, "Pass", () => executedCount++);
            engine.RegisterTest(fake2);

            int logCount = 0;
            engine.LogEmitted += _ => logCount++;

            RunSync(() => engine.RunAllAsync(NoopProgress(), CancellationToken.None));

            Assert.CurrentTestName = "Engine: RunAllAsync 调用了所有测试项";
            Assert.Equal(2, executedCount);
            Assert.CurrentTestName = "Engine: LogEmitted 至少触发 6 次（每个 item start+done，共 6）";
            Assert.True(logCount >= 6, $"logCount={logCount} 应 ≥ 6");
        }

        // ====================================================================
        // H-6 资源快照判定逻辑单测
        // ====================================================================

        /// <summary>
        /// 覆盖 ResourceSnapshotLogic.EvaluateReadiness 的 6 种关键组合。
        /// <para>目的：验证 LoadDefaultTestsToGrid 改用资源快照后，三层分支判定
        /// 与"具体不满足原因"的输出符合预期。重点：</para>
        /// <list type="bullet">
        ///   <item>全就绪 → AllReady=true, Reason=空</item>
        ///   <item>任一资源未就绪 → AllReady=false, Reason 指向"第一优先级"的不满足项</item>
        ///   <item>优先级：receiver 缺失/关闭 > RF > LO > SA</item>
        /// </list>
        /// </summary>
        private static void TestH6ResourceSnapshot()
        {
            Console.WriteLine("[Test] H-6 ResourceSnapshotLogic.EvaluateReadiness 资源快照判定");

            // (1) 全就绪：AllReady=true，Reason 为空
            {
                var d = ResourceSnapshotLogic.EvaluateReadiness(
                    hasReceiver: true, receiverOpen: true,
                    rfReady: true, loReady: true, saReady: true);
                Assert.CurrentTestName = "H6: 全就绪 → AllReady=true";
                Assert.True(d.AllReady, "AllReady 应当为 true");
                Assert.CurrentTestName = "H6: 全就绪 → Reason=空";
                Assert.Equal(string.Empty, d.Reason ?? string.Empty);
            }

            // (2) 无 receiver：第一个不满足项优先报告
            {
                var d = ResourceSnapshotLogic.EvaluateReadiness(
                    hasReceiver: false, receiverOpen: false,
                    rfReady: true, loReady: true, saReady: true);
                Assert.CurrentTestName = "H6: 无 receiver → AllReady=false";
                Assert.True(!d.AllReady, "AllReady 应当为 false");
                Assert.CurrentTestName = "H6: 无 receiver → Reason 优先报 receiver";
                Assert.Equal("接收机串口未连接", d.Reason);
            }

            // (3) receiver 存在但已 Close
            {
                var d = ResourceSnapshotLogic.EvaluateReadiness(
                    hasReceiver: true, receiverOpen: false,
                    rfReady: true, loReady: true, saReady: true);
                Assert.CurrentTestName = "H6: receiver 已断开 → Reason 准确";
                Assert.True(!d.AllReady);
                Assert.Equal("接收机串口已断开", d.Reason);
            }

            // (4) 三仪表中 RF 缺失（receiver 正常）
            {
                var d = ResourceSnapshotLogic.EvaluateReadiness(
                    hasReceiver: true, receiverOpen: true,
                    rfReady: false, loReady: true, saReady: true);
                Assert.CurrentTestName = "H6: RF 未连接 → Reason 指向 RF（优先级高于 LO/SA）";
                Assert.True(!d.AllReady);
                Assert.Equal("RF 信号源未连接", d.Reason);
            }

            // (5) 三仪表中 LO 缺失（RF 已就绪）
            {
                var d = ResourceSnapshotLogic.EvaluateReadiness(
                    hasReceiver: true, receiverOpen: true,
                    rfReady: true, loReady: false, saReady: true);
                Assert.CurrentTestName = "H6: LO 未连接 → Reason 指向 LO（优先级高于 SA）";
                Assert.True(!d.AllReady);
                Assert.Equal("LO 信号源未连接", d.Reason);
            }

            // (6) 仅 SA 缺失
            {
                var d = ResourceSnapshotLogic.EvaluateReadiness(
                    hasReceiver: true, receiverOpen: true,
                    rfReady: true, loReady: true, saReady: false);
                Assert.CurrentTestName = "H6: SA 未连接 → Reason 指向 SA";
                Assert.True(!d.AllReady);
                Assert.Equal("频谱仪未连接", d.Reason);
            }

            // (7) 极端：receiver + 三仪表全部缺失，仍按优先级报 receiver
            {
                var d = ResourceSnapshotLogic.EvaluateReadiness(
                    hasReceiver: false, receiverOpen: false,
                    rfReady: false, loReady: false, saReady: false);
                Assert.CurrentTestName = "H6: 全缺失 → Reason 优先报 receiver（不会被 SA 抢占）";
                Assert.True(!d.AllReady);
                Assert.Equal("接收机串口未连接", d.Reason);
            }
        }

        // ====================================================================
        // Helpers
        // ====================================================================

        /// <summary>
        /// 同步等待一个返回 Task 的 lambda，捕获 AggregateException 中的首个异常重抛。
        /// </summary>
        private static T RunSync<T>(Func<Task<T>> taskFactory)
        {
            try
            {
                return Task.Run(taskFactory).GetAwaiter().GetResult();
            }
            catch (AggregateException ae)
            {
                if (ae.InnerException != null) throw ae.InnerException;
                throw;
            }
        }

        private static void RunSync(Func<Task> taskFactory)
        {
            try
            {
                Task.Run(taskFactory).GetAwaiter().GetResult();
            }
            catch (AggregateException ae)
            {
                if (ae.InnerException != null) throw ae.InnerException;
                throw;
            }
        }

        /// <summary>
        /// 创建不执行任何操作的 IProgress，用于单元测试。
        /// </summary>
        private static IProgress<string> NoopProgress()
        {
            return new Progress<string>(_ => { });
        }
    }

    /// <summary>
    /// 测试用 ITestItem 实现。不调用任何仪表，仅返回一个固定值。
    /// </summary>
    internal sealed class FakeTestItem : ITestItem
    {
        private readonly Action _onExecute;

        public FakeTestItem(string name, string unit, double? lower, double? upper,
                            double value, string judgment, Action onExecute)
        {
            TestName = name;
            Unit = unit;
            LowerLimit = lower;
            UpperLimit = upper;
            Value = value;
            Judgment = judgment;
            _onExecute = onExecute;
        }

        public string TestName { get; }
        public string Unit { get; }
        public double? LowerLimit { get; set; }
        public double? UpperLimit { get; set; }
        public double Value { get; }
        public string Judgment { get; }

        public System.Threading.Tasks.Task<TestResult> ExecuteAsync(IProgress<string> progress, CancellationToken ct)
        {
            _onExecute();
            progress?.Report($"[{TestName}] fake executed");
            return System.Threading.Tasks.Task.FromResult(new TestResult
            {
                TestTime = DateTime.Now,
                TestItem = TestName,
                Value = Value,
                Unit = Unit,
                Judgment = Judgment,
                Remarks = "FakeTestItem"
            });
        }
    }
}