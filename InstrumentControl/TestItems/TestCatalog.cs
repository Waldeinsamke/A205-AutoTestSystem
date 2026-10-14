using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using A205AutoTestSystem.Common;
using A205AutoTestSystem.ReceiverControl;

namespace A205AutoTestSystem.InstrumentControl.TestItems
{
    /// <summary>
    /// 测试项目录。M6 阶段集中维护当前可用的全部测试项清单；
    /// UI（AutoTestPanel）在初始化完成后直接从这里拉取列表并填入 DataGridView，
    /// 不再依赖"加载测试项"按钮触发。
    /// <para>当前进度：2 项已实现（线性最大增益 / 信号带宽），其余按温度分支占位：
    /// 常温 19 项 Stub、高/低温 1 项 Stub（电源功耗）。
    /// Stub 在点击"运行"时会立即返回一条带"未实现"提示的 Fail 结果，不会抛异常，
    /// 便于在 UI 上预览完整行布局。</para>
    /// <para>M6.x：新增 <see cref="CreateProduction(ReceiverSerialPort, SignalGenerator, SignalGeneratorLO, SpectrumAnalyzer, int, TemperatureRange)"/>，
    /// 资源完整时由 AutoTestPanel 调用，按"测试温度"返回不同长度的测试项清单：
    /// 常温 21 项 / 高温 3 项 / 低温 3 项。</para>
    /// </summary>
    public static class TestCatalog
    {
        /// <summary>
        /// 创建默认的 16 项测试项清单（已实现 + 未实现 Stub）。
        /// <para>这是 M6 阶段单点兼容版本，GainTest 不接收 receiver，
        /// 仅做单点（100 MHz）测量；适合资源未就绪（receiver 未连接）的演示场景。</para>
        /// </summary>
        /// <param name="rf">射频信号源（已连接）；Stub 项不强制使用。</param>
        /// <param name="lo">本振信号源（已连接）；仅 GainTest 等需要。</param>
        /// <param name="sa">频谱仪（已连接）；GainTest / BandwidthTest 需要。</param>
        /// <param name="mode">工作模式 1/2/3；用于 LO 频率规则。</param>
        public static IReadOnlyList<ITestItem> CreateDefault(
            SignalGenerator rf,
            SignalGeneratorLO lo,
            SpectrumAnalyzer sa,
            int mode)
        {
            var list = new List<ITestItem>(16)
            {
                // ---- 已实现（M6 兼容：单点）----
                new GainTest(rf, lo, sa, rfFreqHz: 100e6, rfPowerDbm: -20,
                             mode: mode, loPowerDbm: 0, ifFreqHz: 70e6),
                new BandwidthTest(rf, sa, centerRfFreqHz: 100e6, rfPowerDbm: -20,
                                  ifFreqHz: 70e6, scanPoints: 11),

                // ---- 未实现 Stub（占位，UI 仍显示该行，运行时报"未实现"）----
                new NotImplementedTestItem("噪声系数"),
                new NotImplementedTestItem("输入P-1"),
                new NotImplementedTestItem("饱和输出电平"),
                new NotImplementedTestItem("AGC控制特性"),
                new NotImplementedTestItem("AGC响应时间"),
                new NotImplementedTestItem("通道延时"),
                new NotImplementedTestItem("阻塞信号"),
                new NotImplementedTestItem("镜频抑制"),
                new NotImplementedTestItem("中频抑制"),
                new NotImplementedTestItem("中频带外抑制"),
                new NotImplementedTestItem("三阶互调抑制"),
                new NotImplementedTestItem("谐波抑制"),
                new NotImplementedTestItem("带内虚假信号"),
                new NotImplementedTestItem("通道间隔离度"),
            };
            return list;
        }

        /// <summary>
        /// M6.x 全功能测试项清单（兼容重载，默认走"常温"分支）。
        /// <para>与 <see cref="CreateDefault"/> 的差异：第 1 项 GainTest 接收 receiver
        /// 和共享的 RF/LO/SA 实例，按"模式 → 频点"两层循环扫测，每点一条 TestResult。</para>
        /// </summary>
        /// <param name="receiver">接收机串口（必须已 Open）。</param>
        /// <param name="rf">射频信号源（必须已 Connect）。</param>
        /// <param name="lo">本振信号源（必须已 Connect）。</param>
        /// <param name="sa">频谱仪（必须已 Connect）。</param>
        /// <param name="channel">通道号（1..8）。</param>
        public static IReadOnlyList<ITestItem> CreateProduction(
            ReceiverSerialPort receiver,
            SignalGenerator rf,
            SignalGeneratorLO lo,
            SpectrumAnalyzer sa,
            int channel)
        {
            // 兼容老调用方：默认走常温分支。
            return CreateProduction(receiver, rf, lo, sa, channel, TemperatureRange.Normal);
        }

        /// <summary>
        /// M6.x 全功能测试项清单（按"测试温度"分支）。
        /// <para>三档测试温度：</para>
        /// <list type="bullet">
        ///   <item><see cref="TemperatureRange.Normal"/>：常温，21 项</item>
        ///   <item><see cref="TemperatureRange.High"/> / <see cref="TemperatureRange.Low"/>：高/低温，共用 3 项</item>
        /// </list>
        /// </summary>
        /// <param name="receiver">接收机串口（必须已 Open）。</param>
        /// <param name="rf">射频信号源（必须已 Connect）。</param>
        /// <param name="lo">本振信号源（必须已 Connect）。</param>
        /// <param name="sa">频谱仪（必须已 Connect）。</param>
        /// <param name="channel">通道号（1..8）。</param>
        /// <param name="temperature">测试温度档位。</param>
        public static IReadOnlyList<ITestItem> CreateProduction(
            ReceiverSerialPort receiver,
            SignalGenerator rf,
            SignalGeneratorLO lo,
            SpectrumAnalyzer sa,
            int channel,
            TemperatureRange temperature)
        {
            if (receiver == null) throw new ArgumentNullException(nameof(receiver));
            if (rf == null) throw new ArgumentNullException(nameof(rf));
            if (lo == null) throw new ArgumentNullException(nameof(lo));
            if (sa == null) throw new ArgumentNullException(nameof(sa));
            if (channel < 1 || channel > 8)
                throw new ArgumentOutOfRangeException(nameof(channel), channel, "通道号必须在 1~8 范围内。");

            switch (temperature)
            {
                case TemperatureRange.Normal:
                    return BuildNormalTempItems(receiver, rf, lo, sa, channel);
                case TemperatureRange.High:
                case TemperatureRange.Low:
                    return BuildHighLowTempItems(receiver, rf, lo, sa, channel);
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(temperature), temperature, "未定义的测试温度档位。");
            }
        }

        // ============================================================
        // 常温：21 项
        //   1) 信号带宽
        //   2) 线性最大增益
        //   3) 噪声系数
        //   4) 输入P-1
        //   5) 饱和输出电平
        //   6) AGC控制特性
        //   7) AGC响应时间
        //   8) 通道延时
        //   9) 阻塞信号
        //  10) 镜频抑制
        //  11) 中频抑制
        //  12) 三阶互调
        //  13) 中频带外抑制
        //  14) 谐波抑制
        //  15) 带内虚假信号
        //  16) 通道间隔离度
        //  17) 通道间增益不一致性（AGC不起控）
        //  18) 通道间增益不一致性（AGC同时起控）
        //  19) 通道间相位差稳定度
        //  20) 通道间增益差稳定度
        //  21) 驻波
        // ============================================================
        private static IReadOnlyList<ITestItem> BuildNormalTempItems(
            ReceiverSerialPort receiver,
            SignalGenerator rf,
            SignalGeneratorLO lo,
            SpectrumAnalyzer sa,
            int channel)
        {
            // 全功能模式（信号带宽 / 线性最大增益 都按"模式 × 频点"遍历）
            IReadOnlyList<ReceiverMode> allModes = new ReceiverMode[]
            {
                ReceiverMode.Mode1,
                ReceiverMode.Mode2,
                ReceiverMode.Mode3
            };

            return new List<ITestItem>(21)
            {
                // ---- 已实现 ----
                new GainTest(
                    receiver: receiver,
                    rfGenerator: rf,
                    loGenerator: lo,
                    spectrumAnalyzer: sa,
                    channel: channel,
                    modes: allModes,
                    settleDelayMs: 200),
                new BandwidthTest(
                    receiver: receiver,
                    rfGenerator: rf,
                    loGenerator: lo,
                    spectrumAnalyzer: sa,
                    channel: channel,
                    modes: allModes),

                // ---- 未实现 Stub（占位，UI 仍显示该行，运行时报"未实现"）----
                new NotImplementedTestItem("噪声系数"),
                new NotImplementedTestItem("输入P-1"),
                new NotImplementedTestItem("饱和输出电平"),
                new NotImplementedTestItem("AGC控制特性"),
                new NotImplementedTestItem("AGC响应时间"),
                new NotImplementedTestItem("通道延时"),
                new NotImplementedTestItem("阻塞信号"),
                new NotImplementedTestItem("镜频抑制"),
                new NotImplementedTestItem("中频抑制"),
                new NotImplementedTestItem("三阶互调"),
                new NotImplementedTestItem("中频带外抑制"),
                new NotImplementedTestItem("谐波抑制"),
                new NotImplementedTestItem("带内虚假信号"),
                new NotImplementedTestItem("通道间隔离度"),
                new NotImplementedTestItem("通道间增益不一致性（AGC不起控）"),
                new NotImplementedTestItem("通道间增益不一致性（AGC同时起控）"),
                new NotImplementedTestItem("通道间相位差稳定度"),
                new NotImplementedTestItem("通道间增益差稳定度"),
                new NotImplementedTestItem("驻波"),
            };
        }

        // ============================================================
        // 高/低温：3 项
        //   1) 线性最大增益
        //   2) 输入P-1
        //   3) 电源功耗
        // ============================================================
        private static IReadOnlyList<ITestItem> BuildHighLowTempItems(
            ReceiverSerialPort receiver,
            SignalGenerator rf,
            SignalGeneratorLO lo,
            SpectrumAnalyzer sa,
            int channel)
        {
            // 高/低温也按"模式 × 频点"遍历 GainTest
            IReadOnlyList<ReceiverMode> allModes = new ReceiverMode[]
            {
                ReceiverMode.Mode1,
                ReceiverMode.Mode2,
                ReceiverMode.Mode3
            };

            return new List<ITestItem>(3)
            {
                new GainTest(
                    receiver: receiver,
                    rfGenerator: rf,
                    loGenerator: lo,
                    spectrumAnalyzer: sa,
                    channel: channel,
                    modes: allModes,
                    settleDelayMs: 200),
                new NotImplementedTestItem("输入P-1"),
                new NotImplementedTestItem("电源功耗"),
            };
        }

        /// <summary>
        /// 创建"Mock 加载"模式下的测试项清单（不依赖任何真资源）。
        /// <para>用途：开发/调试阶段无真机连接时，预览"按温度分测试项 + 切温度重建 DGV"的 UI 行为。
        /// 所有项都是 <see cref="NotImplementedTestItem"/> Stub，点"运行"会立即返回"未实现"Fail 结果，
        /// <strong>不会</strong>尝试连接任何仪表或串口。</para>
        /// <para>返回结构与 <see cref="CreateProduction(ReceiverSerialPort, SignalGenerator, SignalGeneratorLO, SpectrumAnalyzer, int, TemperatureRange)"/>
        /// 一致（常温 21 / 高低温 3），便于在 mock 与真机模式下 UI 行为对齐。</para>
        /// </summary>
        /// <param name="temperature">测试温度档位。</param>
        public static IReadOnlyList<ITestItem> CreateMockItems(TemperatureRange temperature)
        {
            switch (temperature)
            {
                case TemperatureRange.Normal:
                    return BuildMockNormalTempItems();
                case TemperatureRange.High:
                case TemperatureRange.Low:
                    return BuildMockHighLowTempItems();
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(temperature), temperature, "未定义的测试温度档位。");
            }
        }

        // ============================================================
        // 常温 Mock：21 项（全部 Stub，连"线性最大增益"和"信号带宽"也用 Stub 占位）
        //   与 BuildNormalTempItems 的 21 项一一对应，便于和真机模式对照
        // ============================================================
        private static IReadOnlyList<ITestItem> BuildMockNormalTempItems()
        {
            return new List<ITestItem>(21)
            {
                new NotImplementedTestItem("线性最大增益"),
                new NotImplementedTestItem("信号带宽"),
                new NotImplementedTestItem("噪声系数"),
                new NotImplementedTestItem("输入P-1"),
                new NotImplementedTestItem("饱和输出电平"),
                new NotImplementedTestItem("AGC控制特性"),
                new NotImplementedTestItem("AGC响应时间"),
                new NotImplementedTestItem("通道延时"),
                new NotImplementedTestItem("阻塞信号"),
                new NotImplementedTestItem("镜频抑制"),
                new NotImplementedTestItem("中频抑制"),
                new NotImplementedTestItem("三阶互调"),
                new NotImplementedTestItem("中频带外抑制"),
                new NotImplementedTestItem("谐波抑制"),
                new NotImplementedTestItem("带内虚假信号"),
                new NotImplementedTestItem("通道间隔离度"),
                new NotImplementedTestItem("通道间增益不一致性（AGC不起控）"),
                new NotImplementedTestItem("通道间增益不一致性（AGC同时起控）"),
                new NotImplementedTestItem("通道间相位差稳定度"),
                new NotImplementedTestItem("通道间增益差稳定度"),
                new NotImplementedTestItem("驻波"),
            };
        }

        // ============================================================
        // 高/低温 Mock：3 项（全部 Stub）
        // ============================================================
        private static IReadOnlyList<ITestItem> BuildMockHighLowTempItems()
        {
            return new List<ITestItem>(3)
            {
                new NotImplementedTestItem("线性最大增益"),
                new NotImplementedTestItem("输入P-1"),
                new NotImplementedTestItem("电源功耗"),
            };
        }
    }

    /// <summary>
    /// 占位测试项。仅显示在 DGV 中，运行按钮点击后返回带"未实现"备注的 Fail 结果。
    /// <para>每个未实现的测试项以独立 stub 实例显示，避免在 UI 上反复实现 EmptyExecute。</para>
    /// </summary>
    public sealed class NotImplementedTestItem : ITestItem
    {
        public NotImplementedTestItem(string displayName)
        {
            TestName = displayName;
        }

        public string TestName { get; }
        public string Unit => "—";
        public double? LowerLimit => null;
        public double? UpperLimit => null;

        public Task<TestResult> ExecuteAsync(IProgress<string> progress, CancellationToken ct)
        {
            progress.Report($"[{TestName}] 未实现（M6.x 阶段补齐）");
            Logger.Log($"[{TestName}] 未实现（M6.x 阶段补齐）");

            var result = new TestResult
            {
                TestTime = System.DateTime.Now,
                TestItem = TestName,
                Value = double.NaN,
                Unit = Unit,
                Judgment = "Fail",
                Remarks = "测试项尚未实现，请等待 M6.x 阶段补齐。"
            };
            TestReportManager.Instance.Record(result);
            return Task.FromResult(result);
        }
    }
}
