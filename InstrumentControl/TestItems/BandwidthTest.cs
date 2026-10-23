using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using A205AutoTestSystem.Common;
using A205AutoTestSystem.ReceiverControl;

namespace A205AutoTestSystem.InstrumentControl.TestItems
{
    /// <summary>
    /// 信号带宽测试（M6.x 阶段重写）。
    /// <para>老逻辑（M6）：自己扫所有频点 + 算 -3dB 边界。</para>
    /// <para>新逻辑（M6.x）：用信号源原生扫频 + 频谱仪 PTP 读数。
    /// RF 信号源在中心 ± 范围内扫 1000 个点（扫频参数设完立即打开 RF 输出），
    /// LO 信号源按 RF 中心切到对应本振频率（不扫频，单频），
    /// 频谱仪按模式联动设中心 + Span 做最大保持，等待 settleDelayMs 后
    /// 调 <see cref="SpectrumAnalyzer.ReadPeakToPeak"/> 读一次 PTP dB 差。</para>
    /// <para>流程（每个模式）：</para>
    /// <list type="number">
    ///   <item>校验 receiver / 三仪表就绪</item>
    ///   <item>按模式取参数：IF=70/750 MHz、SA 中心=70/750 MHz、SA Span=8/300 MHz、
    ///         RF 功率=Mode1/3=-20 dBm / Mode2=-10 dBm、LO=3 dBm</item>
    ///   <item>接收机：先通道后模式（receiver.SendChannelThenModeCommand）+ 频段（receiver.SendBandCommand）</item>
    ///   <item>等模式切换稳定 1000ms（ModeStableMs），接收机内部重整通路后再配置仪表</item>
    ///   <item>LO 单频：按 RF 中心算 LoFrequencyRule，再 SetFrequency + SetPower + SetOutputState(true)</item>
    ///   <item>RF 扫频：FREQ:MODE SWE / FREQ:STAR / FREQ:STOP / SWE:POIN 1000 / POW / INIT:CONT 1；
    ///         扫频参数设完立即 OUTP 1（SetOutputState(true)）</item>
    ///   <item>频谱仪 Reset + 开启连续扫描 + 中心（Mode1/2=70 MHz，Mode3=750 MHz）+ Span（Mode1/2=8 MHz，Mode3=300 MHz）+ 参考电平 10 dBm + 轨迹最大保持</item>
    ///   <item>等 settleDelayMs（真机 15s）</item>
    ///   <item>ReadPeakToPeak() 读一次 PTP dB 差</item>
    ///   <item>清当前模式轨迹（SetMaxHold(false)，切回 WRITe 模式，下一模式会重新 SetMaxHold(true)）</item>
    ///   <item>写一条 TestResult，Value=PTP dB，Unit="dB"，Judgment=Pass/Fail</item>
    ///   <item>全部模式完成后（finally）：RF 停止扫频（SetContinuousSweep(false)）+ RF/LO 关闭输出（SetOutputState(false)）</item>
    /// </list>
    /// </summary>
    public class BandwidthTest : ITestItem
    {
        private readonly ReceiverSerialPort _receiver;
        private readonly SignalGenerator _rf;
        private readonly SignalGeneratorLO _lo;
        private readonly SpectrumAnalyzer _sa;
        private readonly int _channel;
        private readonly IReadOnlyList<ReceiverMode> _modes;
        private readonly int _settleDelayMs;

        /// <summary>扫频点数。M6.x 阶段固定 1000。</summary>
        private const int DefaultSweepPoints = 1000;

        /// <summary>真机模式下频谱仪最大保持的稳定时间（ms）。默认 10 s，后续如测试有冗余可调小。</summary>
        private const int RealMachineSettleDelayMs = 10_000;

        /// <summary>
        /// 模式切换后稳定时间（ms）。
        /// <para>取 1000ms，与 GainTest / InputP1Test 口径一致：
        /// 通道+模式命令发完后接收机内部要重整本振与滤波通路，
        /// 紧接着就配置 RF 扫频与频谱仪，全程只有数十毫秒，等不到接收机真正切稳。</para>
        /// </summary>
        private const int ModeStableMs = 1000;

        // 模式联动频谱仪参数（与 GainTest / ReceiverControlPanel 保持一致；如修改需同步）
        // SA 中心 = IF 中心；SA Span 按模式区分带宽。
        private const double SaSpanMode12Hz = 8_000_000.0;     // Mode1/2: 8 MHz
        private const double SaSpanMode3Hz = 300_000_000.0;    // Mode3:   300 MHz
        private const double SaCenterFreqMode12Hz = 70_000_000.0;  // Mode1/2: 70 MHz
        private const double SaCenterFreqMode3Hz = 750_000_000.0;  // Mode3:   750 MHz
        private const double RfPowerMode13Dbm = -20.0;
        private const double RfPowerMode2Dbm = -10.0;
        private const double LoPowerModeDbm = 3.0;

        /// <summary>频谱仪参考电平（dBm）。与 GainTest 保持一致；如修改需同步两处。</summary>
        private const double SaReferenceLevelDbm = 10.0;

        /// <summary>
        /// 三个模式的扫频计划：中心 / 扫频范围 / 频段号（ModeBandTable 中的 BandIndex）。
        /// <para>注意：225 MHz 在 Mode1 频段表里对应 `频段220`（BandIndex 7, BN 0x07）；
        /// 1090 MHz 对应 `频段1090`（BandIndex 4, BN 0x04）；
        /// 2000 MHz 对应 `频段2000`（BandIndex 12, BN 0x0C）。</para>
        /// </summary>
        private readonly struct SweepPlan
        {
            public ReceiverMode Mode { get; }
            public double CenterMHz { get; }
            public double StartMHz { get; }
            public double StopMHz { get; }
            public int BandIndex { get; }
            public SweepPlan(ReceiverMode mode, double centerMHz, double startMHz, double stopMHz, int bandIndex)
            {
                Mode = mode;
                CenterMHz = centerMHz;
                StartMHz = startMHz;
                StopMHz = stopMHz;
                BandIndex = bandIndex;
            }
        }

        private static readonly SweepPlan[] DefaultSweepPlans =
        {
            new SweepPlan(ReceiverMode.Mode1, 225.0,  221.0,  229.0,  7),
            new SweepPlan(ReceiverMode.Mode2, 1090.0, 1086.0, 1094.0, 4),
            new SweepPlan(ReceiverMode.Mode3, 2000.0, 1840.0, 2160.0, 12),
        };

        public BandwidthTest(
            ReceiverSerialPort receiver,
            SignalGenerator rfGenerator,
            SignalGeneratorLO loGenerator,
            SpectrumAnalyzer spectrumAnalyzer,
            int channel,
            IReadOnlyList<ReceiverMode> modes = null,
            int settleDelayMs = RealMachineSettleDelayMs)
        {
            if (receiver == null) throw new ArgumentNullException(nameof(receiver));
            if (rfGenerator == null) throw new ArgumentNullException(nameof(rfGenerator));
            if (loGenerator == null) throw new ArgumentNullException(nameof(loGenerator));
            if (spectrumAnalyzer == null) throw new ArgumentNullException(nameof(spectrumAnalyzer));
            if (channel < 1 || channel > 8)
                throw new ArgumentOutOfRangeException(nameof(channel), channel, "通道号必须在 1~8 范围内。");
            if (modes == null || modes.Count == 0)
                throw new ArgumentException("必须至少传入一个工作模式。", nameof(modes));
            if (settleDelayMs < 0)
                throw new ArgumentOutOfRangeException(nameof(settleDelayMs), settleDelayMs, "settleDelayMs 不能为负。");

            _receiver = receiver;
            _rf = rfGenerator;
            _lo = loGenerator;
            _sa = spectrumAnalyzer;
            _channel = channel;
            _modes = modes;
            _settleDelayMs = settleDelayMs;
        }

        public string TestName => "信号带宽";

        /// <summary>
        /// 单位：dB（PTP 读数为 dB 差）。
        /// <para>注意：与 M6 阶段 "MHz" 不同——M6.x 重写后语义改为 PTP 差（dB）。</para>
        /// </summary>
        public string Unit => "dB";

        /// <summary>
        /// 判据下限（dB）。null 表示不设下限。
        /// 信号带宽测试要求 |PTP| ≤ 3 dB，对应 Value 取绝对值后落在 [0, 3]，原始 PTP 落在 [-3, +3]。
        /// 这里 LowerLimit 保留为 -3.0，便于"原值落在 [-3, +3]"的对称判定；UI 展示仍按 UpperLimit 看。
        /// </summary>
        public double? LowerLimit { get; set; } = -3.0;

        /// <summary>
        /// 判据上限（dB）。null 表示不设上限。
        /// M6.x 业务要求：|PTP| ≤ 3 dB 才 Pass，对应原始 PTP 落在 [-3, +3]（LowerLimit=-3.0, UpperLimit=+3.0）。
        /// 报表 Value 已取 Math.Abs(ptpDb)，落在 [0, 3] 区间。
        /// </summary>
        public double? UpperLimit { get; set; } = 3.0;

        public async Task<TestResult> ExecuteAsync(IProgress<string> progress, CancellationToken ct)
        {
            if (progress == null) throw new ArgumentNullException(nameof(progress));
            ct.ThrowIfCancellationRequested();

            if (!_rf.IsConnected || !_lo.IsConnected || !_sa.IsConnected)
            {
                throw new InvalidOperationException(
                    "BandwidthTest 需要三台仪表（RF/LO/SA）已连接。");
            }

            TestResult lastResult = null;
            try
            {
                foreach (var plan in GetPlansForModes())
                {
                    ct.ThrowIfCancellationRequested();
                    lastResult = await ExecuteOneSweepAsync(plan, progress, ct).ConfigureAwait(false);
                }
            }
            finally
            {
                // 收尾：不论正常完成还是中途异常，都要把 RF 扫频和两台信号源的输出关掉，
                // 避免下次测试或下次上电时信号源还残留输出。
                try
                {
                    _rf.SetContinuousSweep(false);
                    _rf.SetOutputState(false);
                    _lo.SetOutputState(false);
                    Logger.Log("[BandwidthTest] 收尾：RF 扫频已停止，RF/LO 输出已关闭。");
                }
                catch (Exception ex)
                {
                    // 收尾失败只记日志，不掩盖主流程的异常
                    Logger.Log($"[BandwidthTest] 收尾关闭输出时异常：{ex.Message}");
                }
            }
            return lastResult;
        }

        private IEnumerable<SweepPlan> GetPlansForModes()
        {
            foreach (var mode in _modes)
            {
                foreach (var plan in DefaultSweepPlans)
                {
                    if (plan.Mode == mode)
                    {
                        yield return plan;
                    }
                }
            }
        }

        private async Task<TestResult> ExecuteOneSweepAsync(
            SweepPlan plan, IProgress<string> progress, CancellationToken ct)
        {
            ReceiverMode mode = plan.Mode;

            // 模式联动参数（按 mode 实时取）
            double ifFreqHz = mode == ReceiverMode.Mode3 ? SaCenterFreqMode3Hz : SaCenterFreqMode12Hz;
            double saCenterHz = ifFreqHz;
            double saSpanHz = mode == ReceiverMode.Mode3 ? SaSpanMode3Hz : SaSpanMode12Hz;
            double rfPowerDbm = mode == ReceiverMode.Mode2 ? RfPowerMode2Dbm : RfPowerMode13Dbm;
            double loPowerDbm = LoPowerModeDbm;

            progress.Report($"[BandwidthTest] === 进入 {mode} (中心 {plan.CenterMHz} MHz, 扫 {plan.StartMHz}~{plan.StopMHz} MHz) ===");
            Logger.Log($"[BandwidthTest] === 进入 {mode} (中心 {plan.CenterMHz} MHz, 扫 {plan.StartMHz}~{plan.StopMHz} MHz) ===");

            // 1) 接收机：先通道后模式（复用 UI 同款 SendChannelThenModeCommand）+ 频段
            _receiver.SendChannelThenModeCommand(_channel, mode);
            _receiver.SendBandCommand(mode, plan.BandIndex);

            // 等模式切换稳定（接收机内部重整本振/滤波通路后再配置仪表）
            progress.Report($"[BandwidthTest] {mode} 模式切换稳定 {ModeStableMs}ms…");
            Logger.Log($"[BandwidthTest] {mode} 模式切换稳定 {ModeStableMs}ms");
            await Task.Delay(ModeStableMs, ct).ConfigureAwait(false);

            // 2) LO：按 RF 中心算本振频率（LoFrequencyRule），单频 + 固定功率 + 打开输出
            double loFreqHz = LoFrequencyRule.Calculate(plan.CenterMHz * 1_000_000.0, (int)mode);
            _lo.SetFrequency(loFreqHz);
            _lo.SetPower(loPowerDbm);
            _lo.SetOutputState(true);

            // 3) RF：扫频设置（扫频参数设完立即打开 RF 输出）
            _rf.SetFrequencyModeSweep();
            _rf.SetSweepStartFrequency(plan.StartMHz * 1_000_000.0);
            _rf.SetSweepStopFrequency(plan.StopMHz * 1_000_000.0);
            _rf.SetSweepPoints(DefaultSweepPoints);
            _rf.SetPower(rfPowerDbm);
            _rf.SetContinuousSweep(true);    // 启动扫频
            _rf.SetOutputState(true);        // 扫频参数设完，立即打开 RF 输出

            // 4) 频谱仪：Reset + 开启连续扫描 + 中心（按 mode 联动）+ Span（按 mode 联动）+ 参考电平 10 dBm + 轨迹最大保持
            _sa.Reset();
            _sa.SetContinuousScan(true);
            _sa.SetCenterFrequency(saCenterHz);
            _sa.SetSpan(saSpanHz);
            _sa.SetReferenceLevel(SaReferenceLevelDbm);
            _sa.SetMaxHold(true);

            // 5) 等稳定（真机 15s）
            progress.Report($"[BandwidthTest] {mode} 等待最大保持扫描 {_settleDelayMs}ms…");
            Logger.Log($"[BandwidthTest] {mode} 等待最大保持扫描 {_settleDelayMs}ms");
            await Task.Delay(_settleDelayMs, ct).ConfigureAwait(false);

            // 6) PTP 读数
            double ptpDb;
            try
            {
                ptpDb = _sa.ReadPeakToPeak();
            }
            catch (Exception ex)
            {
                progress.Report($"[BandwidthTest] {mode} 读 PTP 失败：{ex.Message}");
                Logger.Log($"[BandwidthTest] {mode} 读 PTP 失败：{ex.Message}");

                // 失败也写一条 Fail 结果
                TestResult fail = new TestResult
                {
                    TestTime = DateTime.Now,
                    Channel = _channel,
                    Mode = mode.ToString(),
                    BandIndex = plan.BandIndex,
                    BandDisplayName = TryGetBandDisplayName(mode, plan.BandIndex),
                    TestItem = TestName,
                    Value = double.NaN,
                    Unit = Unit,
                    Judgment = "Fail",
                    Remarks = $"PTP 读数失败：{ex.Message}"
                };
                TestReportManager.Instance.Record(fail);
                return fail;
            }

            // 清当前模式轨迹：把最大保持切回 WRITe 模式，下一模式开始时会重新 SetMaxHold(true)
            _sa.SetMaxHold(false);

            // 报表 Value 取 Math.Abs，让报表里的数值始终落在 [0, 3] 区间。
            // 判据 LowerLimit=-3.0 / UpperLimit=+3.0 仍以原始 ptpDb 参与，等价于"原始落在 [-3, +3]"。
            double absPtpDb = Math.Abs(ptpDb);
            bool pass = (!LowerLimit.HasValue || ptpDb >= LowerLimit.Value)
                     && (!UpperLimit.HasValue || ptpDb <= UpperLimit.Value);

            TestResult result = new TestResult
            {
                TestTime = DateTime.Now,
                Channel = _channel,
                Mode = mode.ToString(),
                BandIndex = plan.BandIndex,
                BandDisplayName = TryGetBandDisplayName(mode, plan.BandIndex),
                TestItem = TestName,
                Value = absPtpDb,
                Unit = Unit,
                Judgment = pass ? "Pass" : "Fail",
                Remarks = string.Format(
                    CultureInfo.InvariantCulture,
                    "Center={0}MHz, Sweep={1}~{2}MHz, Points={3}, RF={4}dBm, LO={5:F3}MHz@{6}dBm, IF={7}MHz, SA(center={8}MHz, span={9}MHz), PTP={10:F2}dB, |PTP|={11:F2}dB",
                    plan.CenterMHz, plan.StartMHz, plan.StopMHz, DefaultSweepPoints,
                    rfPowerDbm, loFreqHz / 1e6, loPowerDbm, ifFreqHz / 1e6,
                    saCenterHz / 1e6, saSpanHz / 1e6, ptpDb, absPtpDb)
            };
            TestReportManager.Instance.Record(result);

            progress.Report(string.Format(
                CultureInfo.InvariantCulture,
                "[BandwidthTest] {0} 中心 {1}MHz: PTP={2:F2} dB → {3}",
                mode, plan.CenterMHz, ptpDb, result.Judgment));
            Logger.Log(string.Format(
                CultureInfo.InvariantCulture,
                "[BandwidthTest] {0} 中心 {1}MHz: PTP={2:F2} dB → {3}",
                mode, plan.CenterMHz, ptpDb, result.Judgment));

            return result;
        }

        private static string TryGetBandDisplayName(ReceiverMode mode, int bandIndex)
        {
            try
            {
                foreach (var b in ModeBandTable.GetBands(mode))
                {
                    if (b.BandIndex == bandIndex)
                    {
                        return b.DisplayName;
                    }
                }
            }
            catch
            {
                // best-effort
            }
            return null;
        }

        // 旧 ExecuteLegacySinglePointAsync（M6 兼容单点路径）已随 mock 模式移除。
    }
}
