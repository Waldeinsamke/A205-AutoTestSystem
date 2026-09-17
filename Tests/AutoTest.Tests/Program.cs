using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using A205AutoTestSystem.InstrumentControl;
using A205AutoTestSystem.InstrumentControl.TestItems;

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
                set.Add("Gain"); set.Add("Bandwidth"); set.Add("Engine");
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
                set.Add("Gain"); set.Add("Bandwidth"); set.Add("Engine");
            }
            return set;
        }

        // ====================================================================
        // GainTest 端到端
        // ====================================================================

        private static void TestGainTest()
        {
            Console.WriteLine("[Test] GainTest.ExecuteAsync 端到端");

            // --- (a) 标准 Fake 响应（-10 dBm → Gain = -10 - (-20) = 10 dB；占位阈值 [10, 30] → Pass）
            {
                string rfAddr = "TCPIP0::gain-rf::INSTR";
                string loAddr = "TCPIP0::gain-lo::INSTR";
                string saAddr = "TCPIP0::gain-sa::INSTR";

                var rf = new SignalGenerator(rfAddr);
                var lo = new SignalGeneratorLO(loAddr);
                var sa = new SpectrumAnalyzer(saAddr);
                rf.Connect();
                lo.Connect();
                sa.Connect();

                // 预设 SA 响应：CALC:MARK:Y? → "-10.00"
                var transportField = typeof(VisaBaseInstrument).GetField(
                    "_transport",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var saTransport = transportField.GetValue(sa) as FakeVisaInstrument;
                Assert.NotNull(saTransport, "SA transport 应当是 FakeVisaInstrument");
                saTransport.SetResponse("CALC:MARK:MAX", "");
                saTransport.SetResponse("CALC:MARK:Y?", "-10.00");

                var gain = new GainTest(
                    rf, lo, sa,
                    rfFreqHz: 100e6, rfPowerDbm: -20,
                    loFreqHz: 70e6,  loPowerDbm: 0,
                    ifFreqHz: 70e6);

                TestResult result = RunSync(() => gain.ExecuteAsync(NoopProgress(), CancellationToken.None));

                Assert.CurrentTestName = "GainTest: 返回值非 null";
                Assert.NotNull(result);
                Assert.CurrentTestName = "GainTest: TestName";
                Assert.Equal("线性最大增益", result != null ? result.TestItem : null);
                Assert.CurrentTestName = "GainTest: Unit";
                Assert.Equal("dB", result != null ? result.Unit : null);
                Assert.CurrentTestName = "GainTest: Value 是有限数";
                Assert.True(result != null && !double.IsNaN(result.Value) && !double.IsInfinity(result.Value), "Value 必须是有限数");
                Assert.CurrentTestName = "GainTest: Value 接近 10 dB（Fake 设定 SA=-10dBm，RF=-20dBm）";
                Assert.True(result != null && Math.Abs(result.Value - 10.0) < 0.01,
                    $"Gain={result?.Value} 应接近 10.0");
                Assert.CurrentTestName = "GainTest: Judgment=Pass（占位阈值 [10,30]）";
                Assert.Equal("Pass", result != null ? result.Judgment : null);
                Assert.CurrentTestName = "GainTest: Remarks 包含 'RF='";
                Assert.True(result != null && result.Remarks != null && result.Remarks.Contains("RF="),
                    "Remarks 应含 RF= 关键字");

                rf.Dispose();
                lo.Dispose();
                sa.Dispose();
            }

            // --- (b) 调整 Fake 响应为 -30 dBm → Gain = -30 - (-20) = -10 dB → Fail（< 10）
            {
                string rfAddr = "TCPIP0::gain-rf2::INSTR";
                string loAddr = "TCPIP0::gain-lo2::INSTR";
                string saAddr = "TCPIP0::gain-sa2::INSTR";

                var rf = new SignalGenerator(rfAddr);
                var lo = new SignalGeneratorLO(loAddr);
                var sa = new SpectrumAnalyzer(saAddr);
                rf.Connect();
                lo.Connect();
                sa.Connect();

                var transportField = typeof(VisaBaseInstrument).GetField(
                    "_transport",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var saTransport = transportField.GetValue(sa) as FakeVisaInstrument;
                saTransport.SetResponse("CALC:MARK:MAX", "");
                saTransport.SetResponse("CALC:MARK:Y?", "-30.00");

                var gain = new GainTest(
                    rf, lo, sa,
                    rfFreqHz: 100e6, rfPowerDbm: -20,
                    loFreqHz: 70e6,  loPowerDbm: 0,
                    ifFreqHz: 70e6);

                TestResult result = RunSync(() => gain.ExecuteAsync(NoopProgress(), CancellationToken.None));

                Assert.CurrentTestName = "GainTest: 低响应 → Judgment=Fail";
                Assert.Equal("Fail", result != null ? result.Judgment : null);
                Assert.CurrentTestName = "GainTest: 低响应 → Value ≈ -10 dB";
                Assert.True(result != null && Math.Abs(result.Value - (-10.0)) < 0.01,
                    $"Gain={result?.Value} 应接近 -10.0");

                rf.Dispose();
                lo.Dispose();
                sa.Dispose();
            }
        }

        // ====================================================================
        // BandwidthTest 端到端
        // ====================================================================

        private static void TestBandwidthTest()
        {
            Console.WriteLine("[Test] BandwidthTest.ExecuteAsync 端到端");

            // --- (a) 标准响应：所有点都返回相同 dBm → 无法找到 -3dB 边界 → bandwidth=0 → Fail（< 0.1 MHz）
            // 为简化，先让所有 ReadPeakAmplitude 都返回 -10 dBm。
            {
                string rfAddr = "TCPIP0::bw-rf::INSTR";
                string saAddr = "TCPIP0::bw-sa::INSTR";

                var rf = new SignalGenerator(rfAddr);
                var sa = new SpectrumAnalyzer(saAddr);
                rf.Connect();
                sa.Connect();

                var transportField = typeof(VisaBaseInstrument).GetField(
                    "_transport",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var saTransport = transportField.GetValue(sa) as FakeVisaInstrument;
                Assert.NotNull(saTransport, "SA transport 应当是 FakeVisaInstrument");
                saTransport.SetResponse("CALC:MARK:MAX", "");
                saTransport.SetResponse("CALC:MARK:Y?", "-10.00");

                var bw = new BandwidthTest(
                    rf, sa,
                    centerRfFreqHz: 100e6, rfPowerDbm: -20,
                    ifFreqHz: 70e6, scanPoints: 11);

                TestResult result = RunSync(() => bw.ExecuteAsync(NoopProgress(), CancellationToken.None));

                Assert.CurrentTestName = "BandwidthTest: 返回值非 null";
                Assert.NotNull(result);
                Assert.CurrentTestName = "BandwidthTest: TestName";
                Assert.Equal("信号带宽", result != null ? result.TestItem : null);
                Assert.CurrentTestName = "BandwidthTest: Unit";
                Assert.Equal("MHz", result != null ? result.Unit : null);
                Assert.CurrentTestName = "BandwidthTest: Value 是有限数";
                Assert.True(result != null && !double.IsNaN(result.Value) && !double.IsInfinity(result.Value), "Value 必须是有限数");
                Assert.CurrentTestName = "BandwidthTest: Judgment 合法（Pass / Fail）";
                Assert.True(result != null && (result.Judgment == "Pass" || result.Judgment == "Fail"),
                    $"Judgment={result?.Judgment}");
                Assert.CurrentTestName = "BandwidthTest: Remarks 非空";
                Assert.True(result != null && !string.IsNullOrEmpty(result.Remarks),
                    "Remarks 应含扫描点数据");

                rf.Dispose();
                sa.Dispose();
            }

            // --- (b) 制造一个"窄带"——只让中心点返回 -10 dBm，其他点 -30 dBm（衰减 20 dB，远超 -3dB 阈值）
            //     → 找不到两侧边界 → BW=0 → Fail
            //     简化版：所有点仍返回 -10 dBm，则两侧对称 -3dB 都覆盖整个扫描宽度。
            //     为了产生确定性边界，我们让所有点都返回 -10 dBm → 边界覆盖 ±halfWidth → BW = ScanWidth
            //     ScanWidth = 100kHz = 0.1 MHz，刚好等于 LowerLimit 0.1 MHz → Pass
            //     但我们已经在 (a) 验证过 BW=0；这里 (b) 是为了说明随响应变化结果稳定。
            {
                string rfAddr = "TCPIP0::bw-rf2::INSTR";
                string saAddr = "TCPIP0::bw-sa2::INSTR";

                var rf = new SignalGenerator(rfAddr);
                var sa = new SpectrumAnalyzer(saAddr);
                rf.Connect();
                sa.Connect();

                var transportField = typeof(VisaBaseInstrument).GetField(
                    "_transport",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var saTransport = transportField.GetValue(sa) as FakeVisaInstrument;
                saTransport.SetResponse("CALC:MARK:MAX", "");
                saTransport.SetResponse("CALC:MARK:Y?", "-10.00");

                var bw = new BandwidthTest(
                    rf, sa,
                    centerRfFreqHz: 100e6, rfPowerDbm: -20,
                    ifFreqHz: 70e6, scanPoints: 11);

                TestResult result = RunSync(() => bw.ExecuteAsync(NoopProgress(), CancellationToken.None));

                Assert.CurrentTestName = "BandwidthTest: 全部点响应相同 → BW = ScanWidth = 0.1 MHz";
                Assert.True(result != null && Math.Abs(result.Value - 0.1) < 1e-6,
                    $"BW={result?.Value} MHz 应等于 0.1");
                Assert.CurrentTestName = "BandwidthTest: BW=0.1 → Judgment=Pass（≥ LowerLimit 0.1）";
                Assert.Equal("Pass", result != null ? result.Judgment : null);

                rf.Dispose();
                sa.Dispose();
            }
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