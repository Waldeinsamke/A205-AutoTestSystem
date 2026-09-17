using System;
using System.Collections.Generic;
using A205AutoTestSystem.ReceiverControl;

namespace A205AutoTestSystem.Tests.BuilderTests
{
    /// <summary>
    /// ReceiverCommandBuilder 的参数化测试主程序。
    /// 在 .NET Framework 4.8 控制台进程中运行，不引入 NUnit / xUnit / MSTest。
    /// <para>数据源：<c>控制命令.md</c>。</para>
    /// <para>运行：直接执行 <c>ReceiverCommandBuilder.Tests.exe</c>；可通过
    /// <c>--filter=Channel|Mode|Temperature|Band|Attenuation|Error</c> 只跑某类。</para>
    /// </summary>
    public static class Program
    {
        // 单条命令尾部常量（5 个 0x00 后接 0x0A 帧尾）
        private static readonly byte[] Tail5 = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00 };

        private static byte[] Frame(byte b1, byte b2, byte b3, byte b4, byte b5)
        {
            return new byte[] { 0x0D, b1, b2, b3, b4, b5, 0x0A };
        }

        private static byte[] Frame(byte b1, byte b2, byte b3)
        {
            return new byte[] { 0x0D, b1, b2, b3, 0x00, 0x00, 0x0A };
        }

        public static int Main(string[] args)
        {
            Console.WriteLine("=== ReceiverCommandBuilder 参数化测试 ===");
            Console.WriteLine($"启动时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine();

            HashSet<string> filter = ParseFilter(args);

            ReceiverCommandBuilder builder = new ReceiverCommandBuilder();

            if (filter.Contains("Channel"))      TestChannels(builder);
            if (filter.Contains("Mode"))         TestModes(builder);
            if (filter.Contains("Temperature"))  TestTemperatures(builder);
            if (filter.Contains("Band"))         TestBands(builder);
            if (filter.Contains("Attenuation"))  TestAttenuations(builder);
            if (filter.Contains("Error"))        TestBoundaryErrors(builder);

            Console.WriteLine();
            Console.WriteLine($"--- 汇总 ---");
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
                // 默认全部跑
                set.Add("Channel");
                set.Add("Mode");
                set.Add("Temperature");
                set.Add("Band");
                set.Add("Attenuation");
                set.Add("Error");
                return set;
            }

            foreach (var arg in args)
            {
                if (string.IsNullOrEmpty(arg)) continue;
                string a = arg.Trim();
                if (a.StartsWith("--filter=", StringComparison.OrdinalIgnoreCase))
                {
                    string value = a.Substring("--filter=".Length).Trim();
                    foreach (var part in value.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
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
                set.Add("Channel");
                set.Add("Mode");
                set.Add("Temperature");
                set.Add("Band");
                set.Add("Attenuation");
                set.Add("Error");
            }
            return set;
        }

        // ---------- Channel ----------

        private static void TestChannels(ReceiverCommandBuilder b)
        {
            Console.WriteLine("[Test] Channel 1..8");
            // 期望字节来自 控制命令.md § 通道选择
            var cases = new Dictionary<int, byte[]>
            {
                { 1, new byte[] { 0x0D, 0x11, 0x01, 0x04, 0x00, 0x00, 0x0A } },
                { 2, new byte[] { 0x0D, 0x11, 0x02, 0x04, 0x00, 0x00, 0x0A } },
                { 3, new byte[] { 0x0D, 0x11, 0x03, 0x04, 0x00, 0x00, 0x0A } },
                { 4, new byte[] { 0x0D, 0x11, 0x04, 0x04, 0x00, 0x00, 0x0A } },
                { 5, new byte[] { 0x0D, 0x11, 0x05, 0x04, 0x00, 0x00, 0x0A } },
                { 6, new byte[] { 0x0D, 0x11, 0x06, 0x04, 0x00, 0x00, 0x0A } },
                { 7, new byte[] { 0x0D, 0x11, 0x07, 0x04, 0x00, 0x00, 0x0A } },
                { 8, new byte[] { 0x0D, 0x11, 0x08, 0x04, 0x00, 0x00, 0x0A } },
            };

            foreach (var kv in cases)
            {
                Assert.CurrentTestName = $"Channel={kv.Key}";
                Assert.Equal(kv.Value, b.BuildChannelCommand(kv.Key));
            }
        }

        // ---------- Mode ----------

        private static void TestModes(ReceiverCommandBuilder b)
        {
            Console.WriteLine("[Test] Mode 1/2/3");
            byte[][] mode1 = new byte[][]
            {
                Frame(0x31, 0x00, 0x00, 0x00, 0x00),
                Frame(0x24, 0x00, 0x00, 0x00, 0x00),
                Frame(0x24, 0x00, 0x00, 0x00, 0x00),
            };
            byte[][] mode2 = new byte[][]
            {
                Frame(0x31, 0x00, 0x00, 0x00, 0x00),
                Frame(0x24, 0x00, 0x00, 0x00, 0x00),
                Frame(0x24, 0x00, 0x00, 0x00, 0x00),
            };
            byte[][] mode3 = new byte[][]
            {
                Frame(0x31, 0x03, 0x00, 0x00, 0x00),
                Frame(0x24, 0x00, 0x00, 0x00, 0x00),
                Frame(0x24, 0x00, 0x00, 0x00, 0x00),
            };

            Assert.CurrentTestName = "Mode=Mode1";
            Assert.Equal(mode1, b.BuildModeCommandSequence(ReceiverMode.Mode1));
            Assert.CurrentTestName = "Mode=Mode2";
            Assert.Equal(mode2, b.BuildModeCommandSequence(ReceiverMode.Mode2));
            Assert.CurrentTestName = "Mode=Mode3";
            Assert.Equal(mode3, b.BuildModeCommandSequence(ReceiverMode.Mode3));
        }

        // ---------- Temperature ----------

        private static void TestTemperatures(ReceiverCommandBuilder b)
        {
            Console.WriteLine("[Test] Temperature 常温/高温/低温");
            var cases = new Dictionary<TemperatureRange, byte[][]>
            {
                { TemperatureRange.Normal, new byte[][]
                    {
                        Frame(0x30, 0x00, 0x00, 0x00, 0x00),
                        Frame(0x24, 0x00, 0x00, 0x00, 0x00),
                    }
                },
                { TemperatureRange.High, new byte[][]
                    {
                        Frame(0x30, 0x01, 0x00, 0x00, 0x00),
                        Frame(0x24, 0x00, 0x00, 0x00, 0x00),
                    }
                },
                { TemperatureRange.Low, new byte[][]
                    {
                        Frame(0x30, 0x02, 0x00, 0x00, 0x00),
                        Frame(0x24, 0x00, 0x00, 0x00, 0x00),
                    }
                },
            };

            foreach (var kv in cases)
            {
                Assert.CurrentTestName = $"Temperature={kv.Key}";
                Assert.Equal(kv.Value, b.BuildTemperatureCommandSequence(kv.Key));
            }
        }

        // ---------- Band (boundary) ----------

        private static void TestBands(ReceiverCommandBuilder b)
        {
            Console.WriteLine("[Test] Band 边界");
            var cases = new List<Tuple<ReceiverMode, int, byte[][]>>
            {
                // Mode1
                Tuple.Create(ReceiverMode.Mode1, 0,  new byte[][]
                {
                    Frame(0x31, 0x00, 0x00, 0x00, 0x00),
                    Frame(0x24, 0x00, 0x00, 0x00, 0x00),
                }),
                Tuple.Create(ReceiverMode.Mode1, 17, new byte[][]
                {
                    Frame(0x31, 0x11, 0x00, 0x00, 0x00),
                    Frame(0x24, 0x00, 0x00, 0x00, 0x00),
                }),
                // Mode2
                Tuple.Create(ReceiverMode.Mode2, 0,  new byte[][]
                {
                    Frame(0x31, 0x00, 0x00, 0x00, 0x00),
                    Frame(0x24, 0x00, 0x00, 0x00, 0x00),
                }),
                Tuple.Create(ReceiverMode.Mode2, 19, new byte[][]
                {
                    Frame(0x31, 0x13, 0x00, 0x00, 0x00),
                    Frame(0x24, 0x00, 0x00, 0x00, 0x00),
                }),
                // Mode3
                Tuple.Create(ReceiverMode.Mode3, 3,  new byte[][]
                {
                    Frame(0x31, 0x03, 0x00, 0x00, 0x00),
                    Frame(0x24, 0x00, 0x00, 0x00, 0x00),
                }),
                Tuple.Create(ReceiverMode.Mode3, 24, new byte[][]
                {
                    Frame(0x31, 0x18, 0x00, 0x00, 0x00),
                    Frame(0x24, 0x00, 0x00, 0x00, 0x00),
                }),
            };

            foreach (var t in cases)
            {
                Assert.CurrentTestName = $"Band mode={t.Item1} bandIndex={t.Item2}";
                Assert.Equal(t.Item3, b.BuildBandCommandSequence(t.Item1, t.Item2));
            }
        }

        // ---------- Attenuation (8 × 16 = 128) ----------

        // dB → 第一字节 (0x10 命令 DATAFIELD)
        private static readonly Dictionary<int, byte> FirstByte = new Dictionary<int, byte>
        {
            { 0, 0x00 }, { 4, 0x02 }, { 8, 0x04 }, { 12, 0x06 },
            { 16, 0x08 }, { 20, 0x0A }, { 24, 0x0C }, { 28, 0x0E },
            { 32, 0x0D }, { 36, 0x0F }, { 40, 0x0D }, { 44, 0x0F },
            { 48, 0x0D }, { 52, 0x0F }, { 56, 0x0D }, { 60, 0x0F },
        };

        // dB → 第二字节 (0x13 命令 DATAFIELD)
        private static readonly Dictionary<int, byte> SecondByte = new Dictionary<int, byte>
        {
            { 0, 0x00 }, { 4, 0x00 }, { 8, 0x00 }, { 12, 0x00 },
            { 16, 0x00 }, { 20, 0x00 }, { 24, 0x00 }, { 28, 0x00 },
            { 32, 0x01 }, { 36, 0x01 }, { 40, 0x03 }, { 44, 0x03 },
            { 48, 0x05 }, { 52, 0x05 }, { 56, 0x0F }, { 60, 0x0F },
        };

        private static void TestAttenuations(ReceiverCommandBuilder b)
        {
            Console.WriteLine("[Test] Attenuation 8 通道 × 16 档位 (共 128 条)");

            int[] levels = new int[] { 0, 4, 8, 12, 16, 20, 24, 28, 32, 36, 40, 44, 48, 52, 56, 60 };

            for (int channel = 1; channel <= 8; channel++)
            {
                foreach (var atten in levels)
                {
                    byte[] expected0x10 = Frame(0x10, (byte)channel, FirstByte[atten], 0x00, 0x00);
                    byte[] expected0x13 = Frame(0x13, (byte)channel, SecondByte[atten], 0x00, 0x00);
                    byte[][] expected = new byte[][] { expected0x10, expected0x13 };

                    Assert.CurrentTestName = $"Attenuation ch={channel} atten={atten}";
                    Assert.Equal(expected, b.BuildAttenuationCommandSequence(channel, atten));
                }
            }
        }

        // ---------- Boundary errors ----------

        private static void TestBoundaryErrors(ReceiverCommandBuilder b)
        {
            Console.WriteLine("[Test] 边界错误（应抛 ArgumentOutOfRangeException）");

            Assert.CurrentTestName = "Err Channel=0";
            Assert.Throws<ArgumentOutOfRangeException>(() => b.BuildChannelCommand(0));

            Assert.CurrentTestName = "Err Channel=9";
            Assert.Throws<ArgumentOutOfRangeException>(() => b.BuildChannelCommand(9));

            Assert.CurrentTestName = "Err Attenuation attenDb=1";
            Assert.Throws<ArgumentOutOfRangeException>(() => b.BuildAttenuationCommandSequence(1, 1));

            Assert.CurrentTestName = "Err Attenuation attenDb=2";
            Assert.Throws<ArgumentOutOfRangeException>(() => b.BuildAttenuationCommandSequence(1, 2));

            Assert.CurrentTestName = "Err Attenuation attenDb=64";
            Assert.Throws<ArgumentOutOfRangeException>(() => b.BuildAttenuationCommandSequence(1, 64));

            Assert.CurrentTestName = "Err Band mode=Mode1 bandIndex=18";
            Assert.Throws<ArgumentOutOfRangeException>(() => b.BuildBandCommandSequence(ReceiverMode.Mode1, 18));

            Assert.CurrentTestName = "Err Band mode=Mode3 bandIndex=2";
            Assert.Throws<ArgumentOutOfRangeException>(() => b.BuildBandCommandSequence(ReceiverMode.Mode3, 2));

            Assert.CurrentTestName = "Err Band mode=Mode3 bandIndex=25";
            Assert.Throws<ArgumentOutOfRangeException>(() => b.BuildBandCommandSequence(ReceiverMode.Mode3, 25));
        }
    }
}