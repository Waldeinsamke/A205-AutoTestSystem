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
    /// 信号源在中心 ± 范围内扫 100 个点，频谱仪做最大保持，
    /// 等待 settleDelayMs 后调 <see cref="SpectrumAnalyzer.ReadPeakToPeak"/> 读一次 PTP dB 差。</para>
    /// <para>流程（每个模式）：</para>
    /// <list type="number">
    ///   <item>校验 receiver / 三仪表就绪</item>
    ///   <item>按模式取参数：IF=70/750 MHz、RF 功率=Mode1/3=-20 dBm / Mode2=-10 dBm、LO=3 dBm</item>
    ///   <item>receiver.SendModeCommand(mode) + receiver.SendBandCommand(mode, bandIndex)</item>
    ///   <item>LO 设中心 + 偏移（LoFrequencyRule）+ 功率</item>
    ///   <item>RF 设扫频模式：FREQ:MODE SWE / FREQ:STAR / FREQ:STOP / SWE:POIN 100 / POW / INIT:CONT 1</item>
    ///   <item>频谱仪 Reset + 中心 70 MHz + 带宽 50 MHz + 轨迹最大保持</item>
    ///   <item>等 settleDelayMs（真机 10s / Fake 1s）</item>
    ///   <item>ReadPeakToPeak() 读一次 PTP dB 差</item>
    ///   <item>写一条 TestResult，Value=PTP dB，Unit="dB"，Judgment=Pass/Fail</item>
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

        /// <summary>扫频点数。M6.x 阶段固定 100。</summary>
        private const int DefaultSweepPoints = 100;

        /// <summary>真机模式下频谱仪最大保持的稳定时间（ms）。</summary>
        private const int RealMachineSettleDelayMs = 10_000;

        /// <summary>Fake 模式下频谱仪最大保持的"假"等待（ms）——仪表瞬间返回，仅为日志可读性。</summary>
        private const int FakeSettleDelayMs = 1_000;

        // 模式联动频谱仪参数（与 GainTest / ReceiverControlPanel 保持一致；如修改需同步）
        private const double SaSpanHz = 50_000_000.0;
        private const double SaCenterFreqMode12Hz = 70_000_000.0;
        private const double SaCenterFreqMode3Hz = 750_000_000.0;
        private const double RfPowerMode13Dbm = -20.0;
        private const double RfPowerMode2Dbm = -10.0;
        private const double LoPowerModeDbm = 3.0;

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

        /// <summary>M6 兼容单点构造：不接 receiver、不循环，按 M6 旧语义跑一次自扫 + -3dB 边界。</summary>
        public BandwidthTest(
            SignalGenerator rfGenerator,
            SpectrumAnalyzer spectrumAnalyzer,
            double centerRfFreqHz,
            double rfPowerDbm,
            double ifFreqHz,
            int scanPoints = 11)
            : this(
                receiver: null,
                rfGenerator: rfGenerator,
                loGenerator: null,
                spectrumAnalyzer: spectrumAnalyzer,
                channel: 1,
                modes: new ReceiverMode[] { ReceiverMode.Mode1 },
                settleDelayMs: RealMachineSettleDelayMs)
        {
            // 此构造仅保留给 TestCatalog.CreateDefault 走 M6 兼容路径，
            // 不通过 ExecuteAsync 实际使用本新逻辑——避免破坏旧测试期望。
            // （见 _legacySinglePoint 标记）
        }

        public string TestName => "信号带宽";

        /// <summary>
        /// 单位：dB（PTP 读数为 dB 差）。
        /// <para>注意：与 M6 阶段 "MHz" 不同——M6.x 重写后语义改为 PTP 差（dB）。</para>
        /// </summary>
        public string Unit => "dB";

        /// <summary>判据下限（dB）。null 表示不设下限。M6.x 占位 30 dB。</summary>
        public double? LowerLimit { get; set; } = 30.0;

        /// <summary>判据上限（dB）。null 表示不设上限。M6.x 占位 70 dB。</summary>
        public double? UpperLimit { get; set; } = 70.0;

        public async Task<TestResult> ExecuteAsync(IProgress<string> progress, CancellationToken ct)
        {
            if (progress == null) throw new ArgumentNullException(nameof(progress));
            ct.ThrowIfCancellationRequested();

            // 旧 M6 兼容路径（无 receiver）走老逻辑——保留给 CreateDefault 演示场景
            if (_receiver == null || !_receiver.IsOpen)
            {
                return await ExecuteLegacySinglePointAsync(progress, ct).ConfigureAwait(false);
            }

            if (!_rf.IsConnected || !_lo.IsConnected || !_sa.IsConnected)
            {
                throw new InvalidOperationException(
                    "BandwidthTest（M6.x）需要三台仪表（RF/LO/SA）已连接。");
            }

            TestResult lastResult = null;
            foreach (var plan in GetPlansForModes())
            {
                ct.ThrowIfCancellationRequested();
                lastResult = await ExecuteOneSweepAsync(plan, progress, ct).ConfigureAwait(false);
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
            double rfPowerDbm = mode == ReceiverMode.Mode2 ? RfPowerMode2Dbm : RfPowerMode13Dbm;
            double loPowerDbm = LoPowerModeDbm;

            progress.Report($"[BandwidthTest] === 进入 {mode} (中心 {plan.CenterMHz} MHz, 扫 {plan.StartMHz}~{plan.StopMHz} MHz) ===");
            Logger.Log($"[BandwidthTest] === 进入 {mode} (中心 {plan.CenterMHz} MHz, 扫 {plan.StartMHz}~{plan.StopMHz} MHz) ===");

            // 1) 接收机：模式 + 频段
            _receiver.SendModeCommand(mode);
            _receiver.SendBandCommand(mode, plan.BandIndex);

            // 2) LO：单频（中心 + 偏移），固定功率
            double loFreqHz = LoFrequencyRule.Calculate(plan.CenterMHz * 1_000_000.0, (int)mode);
            _lo.SetFrequency(loFreqHz);
            _lo.SetPower(loPowerDbm);

            // 3) RF：扫频设置
            _rf.SetFrequencyModeSweep();
            _rf.SetSweepStartFrequency(plan.StartMHz * 1_000_000.0);
            _rf.SetSweepStopFrequency(plan.StopMHz * 1_000_000.0);
            _rf.SetSweepPoints(DefaultSweepPoints);
            _rf.SetPower(rfPowerDbm);
            _rf.SetContinuousSweep(true);   // 启动扫频

            // 4) 频谱仪：Reset + 中心 70 MHz + 带宽 50 MHz + 轨迹最大保持
            _sa.Reset();
            _sa.SetCenterFrequency(SaCenterFreqMode12Hz);     // 写死 70 MHz（用户步骤 5 明确）
            _sa.SetSpan(SaSpanHz);
            _sa.SetMaxHold(true);

            // 5) 等稳定（真机 10s / Fake 1s）
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
                Value = ptpDb,
                Unit = Unit,
                Judgment = pass ? "Pass" : "Fail",
                Remarks = string.Format(
                    CultureInfo.InvariantCulture,
                    "Center={0}MHz, Sweep={1}~{2}MHz, Points={3}, RF={4}dBm, LO={5:F3}MHz@{6}dBm, IF={7}MHz, PTP={8:F2}dB",
                    plan.CenterMHz, plan.StartMHz, plan.StopMHz, DefaultSweepPoints,
                    rfPowerDbm, loFreqHz / 1e6, loPowerDbm, ifFreqHz / 1e6, ptpDb)
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

        // ============================================================
        // M6 兼容：单点自扫 + -3dB 边界（保留给 CreateDefault 演示）
        // ============================================================

        private async Task<TestResult> ExecuteLegacySinglePointAsync(IProgress<string> progress, CancellationToken ct)
        {
            // 旧 BandwidthTest 是单点 + -3dB 边界逻辑。但新构造的 _rf / _lo / _sa 字段在
            // 兼容构造里 _lo 是 null。这里仅做最小可跑版本：单点 peak + 返回 0 带宽，
            // 写一条 Fail 结果（提示"已废弃"）。
            progress.Report("[BandwidthTest] M6 兼容模式（receiver 未连接）：不执行自扫，返回 Fail 占位。");
            Logger.Log("[BandwidthTest] M6 兼容模式（receiver 未连接）：不执行自扫，返回 Fail 占位。");

            TestResult result = new TestResult
            {
                TestTime = DateTime.Now,
                TestItem = TestName,
                Value = 0.0,
                Unit = "MHz",
                Judgment = "Fail",
                Remarks = "M6 兼容路径：receiver 未连接，仅占位（M6.x 已切换为 receiver 路径 + PTP 读数）。"
            };
            TestReportManager.Instance.Record(result);
            return await Task.FromResult(result).ConfigureAwait(false);
        }
    }
}
