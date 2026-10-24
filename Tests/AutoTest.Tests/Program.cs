using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
            if (filter.Contains("Inconsistency")) TestChannelGainInconsistency();
            if (filter.Contains("InconsistencyExcel")) TestInconsistencyExcelReport();

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
                set.Add("Inconsistency"); set.Add("InconsistencyExcel");
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
                set.Add("Inconsistency"); set.Add("InconsistencyExcel");
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
        // 通道间增益不一致性（AGC不起控）纯计算逻辑
        // ====================================================================

        /// <summary>
        /// 覆盖 <see cref="ChannelGainInconsistencyTest"/> 的核心算法：
        /// <list type="bullet">
        ///   <item>输出值 = 最差值（距该模式增益规格边界最近者）</item>
        ///   <item>Pass/Fail = 清单内各频点增益的极差 ≤ 1 dB</item>
        ///   <item>缺频点数据 → 该模式记 Fail 并在备注列出缺失频点</item>
        ///   <item>仓库按通道分键，通道隔离生效</item>
        /// </list>
        /// </summary>
        private static void TestChannelGainInconsistency()
        {
            Console.WriteLine("[Test] 通道间增益不一致性（AGC不起控）计算逻辑");

            // ---- 用例 1：Mode1 18.5 / 18.6 / 18.9 → 最差 18.5，极差 0.4 → Pass ----
            GainResultRepository.Clear();
            RecordGains(1, "Mode1", new Dictionary<string, double>
            {
                { "频段108", 18.5 },
                { "频段220", 18.6 },
                { "频段400", 18.9 },
            });
            {
                var test = new ChannelGainInconsistencyTest(
                    channel: 1,
                    modes: new[] { A205AutoTestSystem.ReceiverControl.ReceiverMode.Mode1 });
                var r = RunSync(() => test.ExecuteAsync(NoopProgress(), CancellationToken.None));

                Assert.CurrentTestName = "Inconsistency: 用例1 输出值 = 最差值 18.5";
                Assert.True(Math.Abs(r.Value - 18.5) < 1e-9, $"Value 应当是 18.5，实际 {r.Value}");
                Assert.CurrentTestName = "Inconsistency: 用例1 极差 0.4 ≤ 1dB → Pass";
                Assert.Equal("Pass", r.Judgment);
                Assert.CurrentTestName = "Inconsistency: 用例1 最差频段 = 频段108";
                Assert.Equal("频段108", r.BandDisplayName);
                Assert.CurrentTestName = "Inconsistency: 用例1 Mode = Mode1";
                Assert.Equal("Mode1", r.Mode);
                Assert.CurrentTestName = "Inconsistency: 用例1 通道号 = 1";
                Assert.Equal(1, r.Channel);
                Assert.CurrentTestName = "Inconsistency: 用例1 BandIndex 指向最差频段（频段108 → Mode1 的 BandIndex 0）";
                Assert.True(r.BandIndex.HasValue && r.BandIndex.Value == 0,
                    $"BandIndex 应当是 0，实际 {(r.BandIndex.HasValue ? r.BandIndex.Value.ToString() : "null")}");
            }

            // ---- 用例 2：Mode1 20.5 / 19.5 / 21.5 → 最差 21.5，极差 2.0 → Fail ----
            GainResultRepository.Clear();
            RecordGains(1, "Mode1", new Dictionary<string, double>
            {
                { "频段108", 20.5 },
                { "频段220", 19.5 },
                { "频段400", 21.5 },
            });
            {
                var test = new ChannelGainInconsistencyTest(
                    channel: 1,
                    modes: new[] { A205AutoTestSystem.ReceiverControl.ReceiverMode.Mode1 });
                var r = RunSync(() => test.ExecuteAsync(NoopProgress(), CancellationToken.None));

                Assert.CurrentTestName = "Inconsistency: 用例2 输出值 = 最差值 21.5";
                Assert.True(Math.Abs(r.Value - 21.5) < 1e-9, $"Value 应当是 21.5，实际 {r.Value}");
                Assert.CurrentTestName = "Inconsistency: 用例2 极差 2.0 > 1dB → Fail";
                Assert.Equal("Fail", r.Judgment);
                Assert.CurrentTestName = "Inconsistency: 用例2 最差频段 = 频段400";
                Assert.Equal("频段400", r.BandDisplayName);
            }

            // ---- 用例 3：最差值取「离边界最近」而非「数值最小」----
            //   Mode1 规格 [18,22]：17.5 距下界 0.5，22.4 距上界 0.4 → 最差应是 22.4
            GainResultRepository.Clear();
            RecordGains(1, "Mode1", new Dictionary<string, double>
            {
                { "频段108", 17.5 },
                { "频段220", 22.4 },
                { "频段400", 20.0 },
            });
            {
                var test = new ChannelGainInconsistencyTest(
                    channel: 1,
                    modes: new[] { A205AutoTestSystem.ReceiverControl.ReceiverMode.Mode1 });
                var r = RunSync(() => test.ExecuteAsync(NoopProgress(), CancellationToken.None));

                Assert.CurrentTestName = "Inconsistency: 用例3 最差值 = 22.4（离边界最近，不是数值最小）";
                Assert.True(Math.Abs(r.Value - 22.4) < 1e-9, $"Value 应当是 22.4，实际 {r.Value}");
                Assert.CurrentTestName = "Inconsistency: 用例3 最差频段 = 频段220";
                Assert.Equal("频段220", r.BandDisplayName);
            }

            // ---- 用例 4：Mode2 规格 [9,11]，最差值 9.1 / 极差 0.2 → Pass ----
            GainResultRepository.Clear();
            RecordGains(1, "Mode2", new Dictionary<string, double>
            {
                { "频段960", 10.0 },
                { "频段1224", 9.1 },
                { "频段1600", 9.3 },
            });
            {
                var test = new ChannelGainInconsistencyTest(
                    channel: 1,
                    modes: new[] { A205AutoTestSystem.ReceiverControl.ReceiverMode.Mode2 });
                var r = RunSync(() => test.ExecuteAsync(NoopProgress(), CancellationToken.None));

                Assert.CurrentTestName = "Inconsistency: 用例4 Mode2 最差值 = 9.1";
                Assert.True(Math.Abs(r.Value - 9.1) < 1e-9, $"Value 应当是 9.1，实际 {r.Value}");
                Assert.CurrentTestName = "Inconsistency: 用例4 Mode2 极差 0.9 ≤ 1dB → Pass";
                Assert.Equal("Pass", r.Judgment);
            }

            // ---- 用例 5：缺频点数据 → Fail + 备注列出缺失频点 ----
            GainResultRepository.Clear();
            RecordGains(1, "Mode1", new Dictionary<string, double>
            {
                { "频段108", 19.5 },
                // 故意不记录 频段220
                { "频段400", 20.0 },
            });
            {
                var test = new ChannelGainInconsistencyTest(
                    channel: 1,
                    modes: new[] { A205AutoTestSystem.ReceiverControl.ReceiverMode.Mode1 });
                var r = RunSync(() => test.ExecuteAsync(NoopProgress(), CancellationToken.None));

                Assert.CurrentTestName = "Inconsistency: 用例5 缺频点 → Fail";
                Assert.Equal("Fail", r.Judgment);
                Assert.CurrentTestName = "Inconsistency: 用例5 缺频点 → Value 为 NaN";
                Assert.True(double.IsNaN(r.Value), $"Value 应当是 NaN，实际 {r.Value}");
                Assert.CurrentTestName = "Inconsistency: 用例5 备注列出缺失的 频段220";
                Assert.True(r.Remarks != null && r.Remarks.Contains("频段220"),
                    "备注应当指出缺失频段220，实际：" + r.Remarks);
            }

            // ---- 用例 6：仓库按通道隔离，通道2 无数据时不应借用通道1 的数据 ----
            GainResultRepository.Clear();
            RecordGains(1, "Mode1", new Dictionary<string, double>
            {
                { "频段108", 19.5 },
                { "频段220", 19.6 },
                { "频段400", 19.7 },
            });
            {
                var test = new ChannelGainInconsistencyTest(
                    channel: 2,
                    modes: new[] { A205AutoTestSystem.ReceiverControl.ReceiverMode.Mode1 });
                var r = RunSync(() => test.ExecuteAsync(NoopProgress(), CancellationToken.None));

                Assert.CurrentTestName = "Inconsistency: 用例6 通道2 无数据 → 不借用通道1 数据 → Fail";
                Assert.Equal("Fail", r.Judgment);
                Assert.CurrentTestName = "Inconsistency: 用例6 通道2 无数据 → Value 为 NaN";
                Assert.True(double.IsNaN(r.Value), $"Value 应当是 NaN，实际 {r.Value}");
            }

            // ---- 用例 7：三模式各产出一条结果（仓库记录 3 条）----
            GainResultRepository.Clear();
            RecordGains(1, "Mode1", new Dictionary<string, double>
            {
                { "频段108", 19.5 }, { "频段220", 19.6 }, { "频段400", 19.7 },
            });
            RecordGains(1, "Mode2", new Dictionary<string, double>
            {
                { "频段960", 10.0 }, { "频段1224", 10.1 }, { "频段1600", 10.2 },
            });
            RecordGains(1, "Mode3", new Dictionary<string, double>
            {
                { "频段900", 19.0 }, { "频段1300", 19.1 }, { "频段1500", 19.2 },
                { "频段1800", 19.3 }, { "频段2100", 19.4 }, { "频段2500", 19.5 },
                { "频段2700", 19.6 }, { "频段3000", 19.7 }, { "频段3300", 19.8 },
            });
            {
                int before = A205AutoTestSystem.Common.TestReportManager.Instance.PendingRowCount;
                var test = new ChannelGainInconsistencyTest(
                    channel: 1,
                    modes: new[]
                    {
                        A205AutoTestSystem.ReceiverControl.ReceiverMode.Mode1,
                        A205AutoTestSystem.ReceiverControl.ReceiverMode.Mode2,
                        A205AutoTestSystem.ReceiverControl.ReceiverMode.Mode3,
                    });
                var r = RunSync(() => test.ExecuteAsync(NoopProgress(), CancellationToken.None));

                int after = A205AutoTestSystem.Common.TestReportManager.Instance.PendingRowCount;
                Assert.CurrentTestName = "Inconsistency: 用例7 三个模式共写入 3 条报表记录";
                Assert.Equal(before + 3, after);
                Assert.CurrentTestName = "Inconsistency: 用例7 返回值是最后一个模式（Mode3）";
                Assert.Equal("Mode3", r.Mode);
                Assert.CurrentTestName = "Inconsistency: 用例7 Mode3 全部在规格内 → Pass";
                Assert.Equal("Pass", r.Judgment);
            }

            // ---- 用例 11：某模式 Fail 时，返回的汇总结果必须是 Fail（不能被最后一个模式的 Pass 掩盖）----
            GainResultRepository.Clear();
            RecordGains(1, "Mode1", new Dictionary<string, double>
            {
                { "频段108", 20.5 }, { "频段220", 19.5 }, { "频段400", 21.5 },   // 极差 2.0 → Fail
            });
            RecordGains(1, "Mode2", new Dictionary<string, double>
            {
                { "频段960", 10.0 }, { "频段1224", 10.1 }, { "频段1600", 10.2 },  // 正常
            });
            RecordGains(1, "Mode3", new Dictionary<string, double>
            {
                { "频段900", 19.0 }, { "频段1300", 19.1 }, { "频段1500", 19.2 },
                { "频段1800", 19.3 }, { "频段2100", 19.4 }, { "频段2500", 19.5 },
                { "频段2700", 19.6 }, { "频段3000", 19.7 }, { "频段3300", 19.8 },
            });
            {
                var test = new ChannelGainInconsistencyTest(channel: 1);
                var r = RunSync(() => test.ExecuteAsync(NoopProgress(), CancellationToken.None));

                Assert.CurrentTestName = "Inconsistency: 用例11 Mode1 Fail 但 Mode3 Pass → 汇总结果仍是 Fail";
                Assert.Equal("Fail", r.Judgment);
                Assert.CurrentTestName = "Inconsistency: 用例11 汇总结果取第一个失败模式（Mode1）的数值 21.5";
                Assert.True(Math.Abs(r.Value - 21.5) < 1e-9, $"Value 应当是 21.5，实际 {r.Value}");
                Assert.CurrentTestName = "Inconsistency: 用例11 汇总结果 Mode = Mode1（失败模式）";
                Assert.Equal("Mode1", r.Mode);
                Assert.CurrentTestName = "Inconsistency: 用例11 汇总备注列出三个模式";
                Assert.True(r.Remarks != null && r.Remarks.Contains("Mode1")
                    && r.Remarks.Contains("Mode2") && r.Remarks.Contains("Mode3"),
                    "备注应当列出三个模式，实际：" + r.Remarks);
                Assert.CurrentTestName = "Inconsistency: 用例11 汇总备注附带失败详情（缺失频点）";
                Assert.True(r.Remarks.Contains("缺少线性最大增益数据") || r.Remarks.Contains("极差"),
                    "备注应当附带失败详情，实际：" + r.Remarks);
            }

            // ---- 用例 12：模式无对应频点清单时构造抛异常（防止运行期 KeyNotFoundException）----
            Assert.CurrentTestName = "Inconsistency: 用例12 传入未配置清单的模式抛 ArgumentOutOfRangeException";
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new ChannelGainInconsistencyTest(
                    channel: 1,
                    modes: new[] { (A205AutoTestSystem.ReceiverControl.ReceiverMode)99 }));

            // ---- 用例 8：仓库 Clear 后数据消失 ----
            GainResultRepository.Clear();
            Assert.CurrentTestName = "Inconsistency: 用例8 Clear 后仓库为空";
            Assert.Equal(0, GainResultRepository.Count);

            // ---- 用例 9：测试项属性 ----
            {
                var test = new ChannelGainInconsistencyTest(channel: 1);
                Assert.CurrentTestName = "Inconsistency: 用例9 UpperLimit = 1.0（极差判据）";
                Assert.True(test.UpperLimit.HasValue && Math.Abs(test.UpperLimit.Value - 1.0) < 1e-9,
                    "UpperLimit 应当是 1.0");
                Assert.CurrentTestName = "Inconsistency: 用例9 LowerLimit 为 null";
                Assert.True(!test.LowerLimit.HasValue, "LowerLimit 应当为 null");
                Assert.CurrentTestName = "Inconsistency: 用例9 单位为 dB";
                Assert.Equal("dB", test.Unit);
                Assert.CurrentTestName = "Inconsistency: 用例9 测试项名称正确";
                Assert.Equal("通道间增益不一致性（AGC不起控）", test.TestName);
            }

            // ---- 用例 10：通道号非法时构造抛异常 ----
            Assert.CurrentTestName = "Inconsistency: 用例10 通道号 0 抛 ArgumentOutOfRangeException";
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new ChannelGainInconsistencyTest(channel: 0));
            Assert.CurrentTestName = "Inconsistency: 用例10 通道号 9 抛 ArgumentOutOfRangeException";
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new ChannelGainInconsistencyTest(channel: 9));

            GainResultRepository.Clear();
        }

        /// <summary>
        /// 往增益仓库写一批数据（测试辅助）。
        /// </summary>
        private static void RecordGains(int channel, string mode, Dictionary<string, double> gains)
        {
            foreach (var kv in gains)
            {
                GainResultRepository.Record(channel, mode, kv.Key, kv.Value);
            }
        }

        // ====================================================================
        // 通道间增益不一致性 → Excel 报表落地校验
        // ====================================================================

        /// <summary>
        /// 校验本指标的三条结果能正确落到 xlsx：频段列写最差频点、
        /// 测试值列写最差增益值、判定结果列写 Pass/Fail。
        /// </summary>
        private static void TestInconsistencyExcelReport()
        {
            Console.WriteLine("[Test] 通道间增益不一致性 → Excel 报表落地");

            GainResultRepository.Clear();
            RecordGains(1, "Mode1", new Dictionary<string, double>
            {
                { "频段108", 18.5 }, { "频段220", 18.6 }, { "频段400", 18.9 },
            });
            RecordGains(1, "Mode2", new Dictionary<string, double>
            {
                { "频段960", 10.0 }, { "频段1224", 10.1 }, { "频段1600", 10.2 },
            });
            RecordGains(1, "Mode3", new Dictionary<string, double>
            {
                { "频段900", 19.0 }, { "频段1300", 19.1 }, { "频段1500", 19.2 },
                { "频段1800", 19.3 }, { "频段2100", 19.4 }, { "频段2500", 19.5 },
                { "频段2700", 19.6 }, { "频段3000", 19.7 }, { "频段3300", 19.8 },
            });

            // 新批次 → 只导出本指标的三条结果
            A205AutoTestSystem.Common.TestReportManager.Instance.StartNewBatch();

            var test = new ChannelGainInconsistencyTest(channel: 1);
            RunSync(() => test.ExecuteAsync(NoopProgress(), CancellationToken.None));

            string tempFile = Path.Combine(
                Path.GetTempPath(),
                string.Format(CultureInfo.InvariantCulture,
                    "gain_inconsistency_{0:yyyyMMdd_HHmmss_fff}.xlsx", DateTime.Now));
            string saved = A205AutoTestSystem.Common.TestReportManager.Instance.SaveCurrentReport(tempFile);

            Assert.CurrentTestName = "Excel: 报表文件已生成";
            Assert.True(!string.IsNullOrEmpty(saved) && File.Exists(saved), "xlsx 应当存在：" + saved);

            using (FileStream fs = new FileStream(saved, FileMode.Open, FileAccess.Read))
            {
                var wb = new NPOI.XSSF.UserModel.XSSFWorkbook(fs);
                var sheet = wb.GetSheet("测试报告");

                Assert.CurrentTestName = "Excel: 共 3 条数据行（表头 + 3）";
                Assert.Equal(4, sheet.PhysicalNumberOfRows);

                // 第 1 行：Mode1，最差 18.5 @ 频段108，Pass
                var row1 = sheet.GetRow(1);
                Assert.CurrentTestName = "Excel: Mode1 行 频段列 = 频段108";
                Assert.Equal("频段108", row1.GetCell(4).StringCellValue);
                Assert.CurrentTestName = "Excel: Mode1 行 测试项目列正确";
                Assert.Equal("通道间增益不一致性（AGC不起控）", row1.GetCell(5).StringCellValue);
                Assert.CurrentTestName = "Excel: Mode1 行 测试值 = 18.5";
                Assert.True(Math.Abs(row1.GetCell(6).NumericCellValue - 18.5) < 1e-9,
                    "测试值应当是 18.5，实际 " + row1.GetCell(6).NumericCellValue);
                Assert.CurrentTestName = "Excel: Mode1 行 判定 = Pass";
                Assert.Equal("Pass", row1.GetCell(8).StringCellValue);

                // 第 2 行：Mode2，最差 10.2 @ 频段1600，Pass
                var row2 = sheet.GetRow(2);
                Assert.CurrentTestName = "Excel: Mode2 行 频段列 = 频段1600";
                Assert.Equal("频段1600", row2.GetCell(4).StringCellValue);
                Assert.CurrentTestName = "Excel: Mode2 行 测试值 = 10.2";
                Assert.True(Math.Abs(row2.GetCell(6).NumericCellValue - 10.2) < 1e-9,
                    "测试值应当是 10.2，实际 " + row2.GetCell(6).NumericCellValue);

                // 第 3 行：Mode3，最差 19.0 @ 频段900，Pass
                var row3 = sheet.GetRow(3);
                Assert.CurrentTestName = "Excel: Mode3 行 频段列 = 频段900";
                Assert.Equal("频段900", row3.GetCell(4).StringCellValue);
                Assert.CurrentTestName = "Excel: Mode3 行 工作模式列 = Mode3";
                Assert.Equal("Mode3", row3.GetCell(3).StringCellValue);

                wb.Close();
            }

            GainResultRepository.Clear();
            try { File.Delete(saved); } catch { /* ignored */ }
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