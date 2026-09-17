using System;
using System.Collections.Generic;
using System.Linq;
using A205AutoTestSystem.InstrumentControl;
using Ivi.Visa;

namespace A205AutoTestSystem.Tests.InstrumentTests
{
    /// <summary>
    /// M4 阶段：VISA 仪表控制单测主程序。
    /// 通过 <see cref="FakeVisaInstrument"/> 注入伪传输，验证
    /// <list type="bullet">
    /// <item><see cref="SignalGenerator"/> / <see cref="SignalGeneratorLO"/> 命令打包正确（culture-invariant 数值）。</item>
    /// <item><see cref="SpectrumAnalyzer"/> 读峰值路径（Query → Write+ReadString）。</item>
    /// <item><c>VisaBaseInstrument.ExecuteWithRetry</c> 重试机制（瞬态错误重试 3 次后放弃）。</item>
    /// <item>非瞬态错误 / 非重试场景下不重试，直接抛出。</item>
    /// <item>连接 / 断开事件正确触发。</item>
    /// </list>
    /// </summary>
    public static class Program
    {
        private const int VisaErrorTimeout = -1073807339;        // 0xBFFF0015
        private const int VisaErrorSystemError = -1073807347;    // 0xBFFF000D
        private const int VisaErrorConnectionLost = -1073807330; // 0xBFFF001E

        public static int Main(string[] args)
        {
            Console.WriteLine("=== Instrument.Tests（M4 VISA 仪表控制）===");
            Console.WriteLine($"启动时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine();

            HashSet<string> filter = ParseFilter(args);

            if (filter.Contains("Command"))  TestCommandFormatting();
            if (filter.Contains("Query"))    TestQueryPath();
            if (filter.Contains("Retry"))    TestRetryTransient();
            if (filter.Contains("NoRetry"))  TestNoRetryOnNonTransient();
            if (filter.Contains("Events"))   TestConnectionEvents();
            if (filter.Contains("Guard"))    TestGuardChecks();

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
                set.Add("Command"); set.Add("Query"); set.Add("Retry");
                set.Add("NoRetry"); set.Add("Events"); set.Add("Guard");
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
                set.Add("Command"); set.Add("Query"); set.Add("Retry");
                set.Add("NoRetry"); set.Add("Events"); set.Add("Guard");
            }
            return set;
        }

        // ====================================================================
        // Command formatting
        // ====================================================================

        private static void TestCommandFormatting()
        {
            Console.WriteLine("[Test] Command 打包正确（culture-invariant 数值）");

            // --- SignalGenerator.SetFrequency(100e6) → "FREQ 100000000"
            {
                var transport = new FakeVisaInstrument("TCPIP0::1.2.3.4::INSTR");
                var sg = new SignalGenerator("TCPIP0::1.2.3.4::INSTR");
                InjectAndConnect(sg, transport);

                sg.SetFrequency(100_000_000.0); // 100 MHz

                Assert.Equal("FREQ 100000000\n", transport.SendHistory[0]);
                Assert.True(transport.SendHistory.Count == 1, "应只发出一条命令");
                Assert.CurrentTestName = "SignalGenerator.SetFrequency(100MHz)";
                sg.Dispose();
            }

            // --- SignalGenerator.SetFrequency 含小数：1.5 GHz
            {
                var transport = new FakeVisaInstrument("TCPIP0::x::INSTR");
                var sg = new SignalGenerator("TCPIP0::x::INSTR");
                InjectAndConnect(sg, transport);

                sg.SetFrequency(1_500_000_000.0);

                Assert.CurrentTestName = "SignalGenerator.SetFrequency(1.5GHz)";
                Assert.Equal("FREQ 1500000000\n", transport.SendHistory[0]);

                sg.Dispose();
            }

            // --- SignalGenerator.SetPower(-12.5 dBm)
            {
                var transport = new FakeVisaInstrument("TCPIP0::x::INSTR");
                var sg = new SignalGenerator("TCPIP0::x::INSTR");
                InjectAndConnect(sg, transport);

                sg.SetPower(-12.5);

                Assert.CurrentTestName = "SignalGenerator.SetPower(-12.5)";
                Assert.Equal("POW -12.5\n", transport.SendHistory[0]);

                sg.Dispose();
            }

            // --- SignalGenerator.SetOutputState(true/false)
            {
                var transport = new FakeVisaInstrument("TCPIP0::x::INSTR");
                var sg = new SignalGenerator("TCPIP0::x::INSTR");
                InjectAndConnect(sg, transport);

                sg.SetOutputState(true);
                sg.SetOutputState(false);

                Assert.CurrentTestName = "SignalGenerator.SetOutputState";
                Assert.Equal("OUTP ON\n", transport.SendHistory[0]);
                Assert.Equal("OUTP OFF\n", transport.SendHistory[1]);

                sg.Dispose();
            }

            // --- SignalGeneratorLO.SetFrequency
            {
                var transport = new FakeVisaInstrument("TCPIP0::lo::INSTR");
                var lo = new SignalGeneratorLO("TCPIP0::lo::INSTR");
                InjectAndConnect(lo, transport);

                lo.SetFrequency(2_400_000_000.0);
                lo.SetPower(5.0);
                lo.SetOutputState(true);

                Assert.CurrentTestName = "SignalGeneratorLO 三连发";
                Assert.Equal("FREQ 2400000000\n", transport.SendHistory[0]);
                Assert.Equal("POW 5\n", transport.SendHistory[1]);
                Assert.Equal("OUTP ON\n", transport.SendHistory[2]);

                lo.Dispose();
            }

            // --- ConfigureAndEnable
            {
                var transport = new FakeVisaInstrument("TCPIP0::x::INSTR");
                var sg = new SignalGenerator("TCPIP0::x::INSTR");
                InjectAndConnect(sg, transport);

                sg.ConfigureAndEnable(100e6, -10.0);

                Assert.CurrentTestName = "SignalGenerator.ConfigureAndEnable";
                Assert.Equal("FREQ 100000000\n", transport.SendHistory[0]);
                Assert.Equal("POW -10\n", transport.SendHistory[1]);
                Assert.Equal("OUTP ON\n", transport.SendHistory[2]);

                sg.Dispose();
            }
        }

        // ====================================================================
        // Query path
        // ====================================================================

        private static void TestQueryPath()
        {
            Console.WriteLine("[Test] SpectrumAnalyzer 查询路径（Write → ReadString → 解析）");

            // --- ReadPeakAmplitude：先发 CALC:MARK:MAX，再发 CALC:MARK:Y? 并解析返回值
            {
                var transport = new FakeVisaInstrument("TCPIP0::spec::INSTR");
                var sa = new SpectrumAnalyzer("TCPIP0::spec::INSTR");
                InjectAndConnect(sa, transport);

                transport.SetResponse("CALC:MARK:MAX", "");
                transport.SetResponse("CALC:MARK:Y?", "-42.75");

                double peak = sa.ReadPeakAmplitude();

                Assert.CurrentTestName = "SpectrumAnalyzer.ReadPeakAmplitude";
                Assert.Equal("CALC:MARK:MAX\n", transport.SendHistory[0]);
                Assert.Equal("CALC:MARK:Y?\n", transport.SendHistory[1]);
                Assert.Equal(-42.75, peak);

                sa.Dispose();
            }

            // --- SetCenterFrequency 命令格式化
            {
                var transport = new FakeVisaInstrument("TCPIP0::spec::INSTR");
                var sa = new SpectrumAnalyzer("TCPIP0::spec::INSTR");
                InjectAndConnect(sa, transport);

                sa.SetCenterFrequency(2_450_000_000.0);
                sa.SetSpan(1_000_000.0);
                sa.SetRbw(10_000.0);

                Assert.CurrentTestName = "SpectrumAnalyzer 三连发";
                Assert.Equal("FREQ:CENT 2450000000\n", transport.SendHistory[0]);
                Assert.Equal("FREQ:SPAN 1000000\n", transport.SendHistory[1]);
                Assert.Equal("BAND:RES 10000\n", transport.SendHistory[2]);

                sa.Dispose();
            }
        }

        // ====================================================================
        // Retry: 模拟前 2 次 VisaException(timeout)，第 3 次成功
        // ====================================================================

        private static void TestRetryTransient()
        {
            Console.WriteLine("[Test] 重试机制：前 2 次瞬态超时 → 第 3 次成功");

            // 测试用基类不能在测试程序集直接构造（abstract），所以用 SignalGenerator 子类。
            // 我们需要让 ExecuteWithRetry 触发，重试至少 3 次。
            // 方案：调用 SendCommand；让 Write 在第 1、2 次抛 timeout，第 3 次成功。
            // SendCommand → ExecuteWithRetry(()=>{ LogRawSend; transport.Write(); return true; })
            // 第 1 次 call index = 1（Open）；第 2 次 = 2（Write attempt 1）；第 3 次 = 3（Write attempt 2）

            var transport = new FakeVisaInstrument("TCPIP0::x::INSTR");
            var sg = new SignalGenerator("TCPIP0::x::INSTR") { MaxRetry = 3, TimeoutMs = 5000 };
            InjectAndConnect(sg, transport);

            // Open() 是 call 1，Write attempt1 是 call 2，attempt2 是 call 3，attempt3 是 call 4
            transport.InjectError(2, FakeVisaException.WithHResult(VisaErrorTimeout, "fake timeout 1"));
            transport.InjectError(3, FakeVisaException.WithHResult(VisaErrorTimeout, "fake timeout 2"));
            // 第 4 次不应再抛

            Assert.DoesNotThrow(() => sg.SetFrequency(100_000_000.0));
            Assert.CurrentTestName = "Retry: 重试 2 次后成功";
            // 失败调用不会写入 history；只有第 3 次成功调用计入
            Assert.Equal(1, transport.SendHistory.Count);
            Assert.Equal("FREQ 100000000\n", transport.SendHistory[0]);

            sg.Dispose();
        }

        // ====================================================================
        // NoRetry: 超时错误（实际是 HResult == VI_ERROR_TMO）应触发重试；
        // 非瞬态错误（如 generic exception）不应重试
        // ====================================================================

        private static void TestNoRetryOnNonTransient()
        {
            Console.WriteLine("NonRetry: 非瞬态错误不重试 + 瞬态错误耗尽重试后抛出");

            // (a) 试用一个非 VisaException（如 InvalidOperationException），应不重试直接抛出
            // 我们用 SendCommand，注入的是 VisaException 但 HResult 是非瞬态码 (0x12345678)
            // VisaBaseInstrument 的 catch 子句是 `catch (VisaException vex) when (IsTransientError(vex))`
            // 非瞬态 VisaException 不会被 when 捕获，进入 catch (Exception ex)，触发 InstrumentError 并抛出
            {
                var transport = new FakeVisaInstrument("TCPIP0::a::INSTR");
                var sg = new SignalGenerator("TCPIP0::a::INSTR") { MaxRetry = 3 };
                InjectAndConnect(sg, transport);

                // InjectError 是 callIndex 触发。Open=1, Write(attempt1)=2
                transport.InjectError(2, FakeVisaException.WithHResult(0x12345678, "fake non-transient"));

                int initialHistoryCount = transport.SendHistory.Count;
                Assert.Throws<VisaException>(() => sg.SetFrequency(100e6));
                Assert.CurrentTestName = "NonRetry: 非瞬态 VisaException 不重试";
                Assert.Equal(initialHistoryCount, transport.SendHistory.Count); // 没有成功 Write

                sg.Dispose();
            }

            // (b) 超时耗尽：连续 3 次都超时（超过 MaxRetry=3），最终抛出
            // 注：VisaBaseInstrument 的循环是 `while (attempt < MaxRetry)`，attempt 从 0 递增，
            // 即 attempt 1, 2, 3 各尝试一次（MaxRetry=3 时共 3 次）
            {
                var transport = new FakeVisaInstrument("TCPIP0::b::INSTR");
                var sg = new SignalGenerator("TCPIP0::b::INSTR") { MaxRetry = 3 };
                InjectAndConnect(sg, transport);

                transport.InjectError(2, FakeVisaException.WithHResult(VisaErrorTimeout, "t1"));
                transport.InjectError(3, FakeVisaException.WithHResult(VisaErrorTimeout, "t2"));
                transport.InjectError(4, FakeVisaException.WithHResult(VisaErrorTimeout, "t3"));
                // 第 5 次 Write 会成功，但此时 attempt 已 >= MaxRetry，循环结束 → 抛 lastEx

                Assert.Throws<VisaException>(() => sg.SetFrequency(100e6));
                Assert.CurrentTestName = "NonRetry: 瞬态错误用完 MaxRetry 后抛出";

                sg.Dispose();
            }

            // (c) System Error (0xBFFF000D) 视为瞬态 → 重试
            {
                var transport = new FakeVisaInstrument("TCPIP0::c::INSTR");
                var sg = new SignalGenerator("TCPIP0::c::INSTR") { MaxRetry = 3 };
                InjectAndConnect(sg, transport);

                transport.InjectError(2, FakeVisaException.WithHResult(VisaErrorSystemError, "syserr"));
                // 第 3 次成功

                Assert.DoesNotThrow(() => sg.SetFrequency(100e6));
                Assert.CurrentTestName = "Retry: System Error 也算瞬态，触发重试";
                Assert.Equal(1, transport.SendHistory.Count);

                sg.Dispose();
            }
        }

        // ====================================================================
        // Connection events
        // ====================================================================

        private static void TestConnectionEvents()
        {
            Console.WriteLine("[Test] ConnectionChanged / InstrumentError 事件");

            var transport = new FakeVisaInstrument("TCPIP0::x::INSTR");
            var sg = new SignalGenerator("TCPIP0::x::INSTR");

            var connEvents = new List<bool>();
            sg.ConnectionChanged += connected => connEvents.Add(connected);

            InjectAndConnect(sg, transport);
            sg.Disconnect();

            Assert.CurrentTestName = "ConnectionChanged 事件";
            Assert.Equal(true, connEvents[0]);
            Assert.Equal(false, connEvents[1]);
            Assert.Equal(2, connEvents.Count);

            sg.Dispose();
        }

        // ====================================================================
        // Guard checks
        // ====================================================================

        private static void TestGuardChecks()
        {
            Console.WriteLine("[Test] Guard：未连接时 SendCommand/QueryString 抛 InvalidOperationException");

            var sg = new SignalGenerator("TCPIP0::g::INSTR");

            Assert.Throws<InvalidOperationException>(() => sg.SendCommand("FREQ 100"));
            Assert.Throws<InvalidOperationException>(() => sg.GetStatus());
            Assert.CurrentTestName = "Guard: 未连接调用抛 InvalidOperationException";

            sg.Dispose();
        }

        // ====================================================================
        // Helpers
        // ====================================================================

        /// <summary>
        /// 把 <paramref name="transport"/> 注入到 <paramref name="instrument"/>，
        /// 调用 Connect()（此时 CreateTransport 不会被调用，因为我们提前注入了）。
        /// </summary>
        private static void InjectAndConnect(VisaBaseInstrument baseInstrument, IVisaTransport transport)
        {
            // 通过反射调用 protected SetTransport
            var method = typeof(VisaBaseInstrument).GetMethod(
                "SetTransport",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (method == null)
            {
                throw new InvalidOperationException("SetTransport 反射失败。");
            }
            method.Invoke(baseInstrument, new object[] { transport });

            baseInstrument.Connect();
        }
    }
}