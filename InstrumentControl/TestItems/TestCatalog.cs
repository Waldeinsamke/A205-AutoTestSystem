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
    /// <para>当前进度：2 项已实现（线性最大增益 / 信号带宽），其余 14 项为 Stub。
    /// Stub 在点击"运行"时会立即返回一条带"未实现"提示的 Fail 结果，不会抛异常，
    /// 便于在 UI 上预览完整 16 行布局。</para>
    /// <para>M6.x：新增 <see cref="CreateProduction"/>，资源完整时由 AutoTestPanel 调用，
    /// 加载"模式×频点遍历"版本的 GainTest（替代 M6 单点兼容版本）。</para>
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
        /// 创建 M6.x 全功能 16 项测试项清单。
        /// <para>与 <see cref="CreateDefault"/> 的差异：第 1 项 GainTest 接收 receiver
        /// 和共享的 RF/LO/SA 实例，按"模式 → 频点"两层循环扫测，每点一条 TestResult。
        /// 通道号 <paramref name="channel"/> 当前固定传 1（留扩展点）。</para>
        /// </summary>
        /// <param name="receiver">接收机串口（必须已 Open）。</param>
        /// <param name="rf">射频信号源（必须已 Connect）。</param>
        /// <param name="lo">本振信号源（必须已 Connect）。</param>
        /// <param name="sa">频谱仪（必须已 Connect）。</param>
        /// <param name="channel">通道号（1..8）；当前实现阶段固定传 1。</param>
        public static IReadOnlyList<ITestItem> CreateProduction(
            ReceiverSerialPort receiver,
            SignalGenerator rf,
            SignalGeneratorLO lo,
            SpectrumAnalyzer sa,
            int channel)
        {
            if (receiver == null) throw new ArgumentNullException(nameof(receiver));
            if (rf == null) throw new ArgumentNullException(nameof(rf));
            if (lo == null) throw new ArgumentNullException(nameof(lo));
            if (sa == null) throw new ArgumentNullException(nameof(sa));
            if (channel < 1 || channel > 8)
                throw new ArgumentOutOfRangeException(nameof(channel), channel, "通道号必须在 1~8 范围内。");

            // 默认遍历全部 3 个模式
            IReadOnlyList<ReceiverMode> allModes = new ReceiverMode[]
            {
                ReceiverMode.Mode1,
                ReceiverMode.Mode2,
                ReceiverMode.Mode3
            };

            var list = new List<ITestItem>(16)
            {
                // ---- 已实现（M6.x 全功能：模式×频点遍历）----
                new GainTest(
                    receiver: receiver,
                    rfGenerator: rf,
                    loGenerator: lo,
                    spectrumAnalyzer: sa,
                    channel: channel,
                    modes: allModes,
                    settleDelayMs: 200),
                // BandwidthTest（M6.x 全功能：3 模式扫频 + PTP 读数）。
                // settleDelayMs 使用真机默认值 10000ms（频谱仪最大保持稳定时间）。
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
                new NotImplementedTestItem("中频带外抑制"),
                new NotImplementedTestItem("三阶互调抑制"),
                new NotImplementedTestItem("谐波抑制"),
                new NotImplementedTestItem("带内虚假信号"),
                new NotImplementedTestItem("通道间隔离度"),
            };
            return list;
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
