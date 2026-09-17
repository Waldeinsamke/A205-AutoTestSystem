using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using A205AutoTestSystem.Common;

namespace A205AutoTestSystem.InstrumentControl.TestItems
{
    /// <summary>
    /// 线性最大增益测试。
    /// <para>流程（按规划书 §4.3.2）：</para>
    /// <list type="number">
    ///   <item>配置 RF 信号源（fRf, pRf, ON）</item>
    ///   <item>配置 LO 信号源（fLo, pLo, ON）</item>
    ///   <item>配置频谱仪（中心 = fIf, Span = 10kHz, RBW = 1kHz）</item>
    ///   <item>等待 200ms 信号稳定</item>
    ///   <item>读取频谱仪峰值 peakDbm</item>
    ///   <item>计算 Gain = peakDbm - pRf</item>
    ///   <item>判定 Pass / Fail（区间 [LowerLimit, UpperLimit]）</item>
    /// </list>
    /// <para>M6 阶段使用占位阈值（10.0 / 30.0 dB）；阈值运行时编辑在 M6.x 阶段引入。</para>
    /// </summary>
    public class GainTest : ITestItem
    {
        private readonly SignalGenerator _rf;
        private readonly SignalGeneratorLO _lo;
        private readonly SpectrumAnalyzer _sa;

        private readonly double _rfFreqHz;
        private readonly double _rfPowerDbm;
        private readonly double _loFreqHz;
        private readonly double _loPowerDbm;
        private readonly double _ifFreqHz;

        /// <summary>等待信号稳定的延时（毫秒）。M6 阶段固定 200ms。</summary>
        private const int SettleDelayMs = 200;

        public GainTest(
            SignalGenerator rfGenerator,
            SignalGeneratorLO loGenerator,
            SpectrumAnalyzer spectrumAnalyzer,
            double rfFreqHz,
            double rfPowerDbm,
            double loFreqHz,
            double loPowerDbm,
            double ifFreqHz)
        {
            if (rfGenerator == null) throw new ArgumentNullException(nameof(rfGenerator));
            if (loGenerator == null) throw new ArgumentNullException(nameof(loGenerator));
            if (spectrumAnalyzer == null) throw new ArgumentNullException(nameof(spectrumAnalyzer));
            if (rfFreqHz <= 0) throw new ArgumentOutOfRangeException(nameof(rfFreqHz));
            if (loFreqHz <= 0) throw new ArgumentOutOfRangeException(nameof(loFreqHz));
            if (ifFreqHz <= 0) throw new ArgumentOutOfRangeException(nameof(ifFreqHz));

            _rf = rfGenerator;
            _lo = loGenerator;
            _sa = spectrumAnalyzer;
            _rfFreqHz = rfFreqHz;
            _rfPowerDbm = rfPowerDbm;
            _loFreqHz = loFreqHz;
            _loPowerDbm = loPowerDbm;
            _ifFreqHz = ifFreqHz;
        }

        public string TestName => "线性最大增益";
        public string Unit => "dB";

        /// <summary>判据下限（dB）。null 表示不设下限。</summary>
        public double? LowerLimit { get; set; } = 10.0;

        /// <summary>判据上限（dB）。null 表示不设上限。</summary>
        public double? UpperLimit { get; set; } = 30.0;

        public async Task<TestResult> ExecuteAsync(IProgress<string> progress, CancellationToken ct)
        {
            if (progress == null) throw new ArgumentNullException(nameof(progress));
            ct.ThrowIfCancellationRequested();

            progress.Report("[GainTest] 配置 RF 信号源…");
            Logger.Log("[GainTest] 配置 RF 信号源…");
            _rf.SetFrequency(_rfFreqHz);
            _rf.SetPower(_rfPowerDbm);
            _rf.SetOutputState(true);

            progress.Report("[GainTest] 配置 LO 信号源…");
            Logger.Log("[GainTest] 配置 LO 信号源…");
            _lo.SetFrequency(_loFreqHz);
            _lo.SetPower(_loPowerDbm);
            _lo.SetOutputState(true);

            progress.Report("[GainTest] 配置频谱仪…");
            Logger.Log("[GainTest] 配置频谱仪…");
            _sa.SetCenterFrequency(_ifFreqHz);
            _sa.SetSpan(10_000);
            _sa.SetRbw(1_000);

            progress.Report($"[GainTest] 等待信号稳定 {SettleDelayMs}ms…");
            await Task.Delay(SettleDelayMs, ct).ConfigureAwait(false);

            progress.Report("[GainTest] 读取频谱仪峰值…");
            double peakDbm = _sa.ReadPeakAmplitude();
            double gain = peakDbm - _rfPowerDbm;

            string msg = string.Format(
                CultureInfo.InvariantCulture,
                "[GainTest] peak={0:F2} dBm, gain={1:F2} dB",
                peakDbm, gain);
            progress.Report(msg);
            Logger.Log(msg);

            bool pass = (!LowerLimit.HasValue || gain >= LowerLimit.Value)
                     && (!UpperLimit.HasValue || gain <= UpperLimit.Value);

            TestResult result = new TestResult
            {
                TestTime = DateTime.Now,
                TestItem = TestName,
                Value = gain,
                Unit = Unit,
                Judgment = pass ? "Pass" : "Fail",
                Remarks = string.Format(
                    CultureInfo.InvariantCulture,
                    "RF={0:F3}MHz@{1}dBm, IF={2:F3}MHz, IFpeak={3:F2}dBm",
                    _rfFreqHz / 1e6, _rfPowerDbm, _ifFreqHz / 1e6, peakDbm)
            };
            TestReportManager.Instance.Record(result);
            return result;
        }
    }
}