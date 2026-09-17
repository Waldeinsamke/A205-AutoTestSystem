using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using A205AutoTestSystem.Common;

namespace A205AutoTestSystem.InstrumentControl.TestItems
{
    /// <summary>
    /// 信号带宽测试。
    /// <para>用途：测量 A205 接收模块在某中心频率附近的信号带宽（如 -3 dB 带宽）。</para>
    /// <para>实现（M6 阶段简化版）：以 RF 中心频率为基准，按等间距扫 ±N 个频率点，
    /// 通过频谱仪读取每个频点下的中频峰值，搜索两个 -3dB 边界点，计算带宽。</para>
    /// <list type="number">
    ///   <item>配置 RF 信号源（fCenter, pRf, ON）</item>
    ///   <item>配置频谱仪（中心 = fIf, Span = 100kHz, RBW = 1kHz）</item>
    ///   <item>读中心点峰值 peakCenter</item>
    ///   <item>计算 -3dB 阈值 = peakCenter - 3.0</item>
    ///   <item>按频率偏移扫描左右两侧，找两个 -3dB 边界点</item>
    ///   <item>带宽 = upperFreq - lowerFreq</item>
    ///   <item>判定 Pass / Fail（区间 [LowerLimit, UpperLimit]，单位 MHz）</item>
    /// </list>
    /// <para>注：M6 阶段通过调节 RF 频率模拟扫描；生产环境中频谱仪有原生扫描命令，
    /// 协议到位后可替换为原生 SCPI（`SWE:POIN` / `INIT:CONT OFF` 等）。</para>
    /// </summary>
    public class BandwidthTest : ITestItem
    {
        private readonly SignalGenerator _rf;
        private readonly SpectrumAnalyzer _sa;

        private readonly double _centerRfFreqHz;
        private readonly double _rfPowerDbm;
        private readonly double _ifFreqHz;
        private readonly int _scanPoints;

        /// <summary>频率扫描总宽度（Hz）。M6 阶段固定 ±50kHz，共 11 个点。</summary>
        private const double ScanWidthHz = 100_000.0;

        public BandwidthTest(
            SignalGenerator rfGenerator,
            SpectrumAnalyzer spectrumAnalyzer,
            double centerRfFreqHz,
            double rfPowerDbm,
            double ifFreqHz,
            int scanPoints = 11)
        {
            if (rfGenerator == null) throw new ArgumentNullException(nameof(rfGenerator));
            if (spectrumAnalyzer == null) throw new ArgumentNullException(nameof(spectrumAnalyzer));
            if (centerRfFreqHz <= 0) throw new ArgumentOutOfRangeException(nameof(centerRfFreqHz));
            if (ifFreqHz <= 0) throw new ArgumentOutOfRangeException(nameof(ifFreqHz));
            if (scanPoints < 3) throw new ArgumentOutOfRangeException(nameof(scanPoints), "扫描点数 ≥ 3");

            _rf = rfGenerator;
            _sa = spectrumAnalyzer;
            _centerRfFreqHz = centerRfFreqHz;
            _rfPowerDbm = rfPowerDbm;
            _ifFreqHz = ifFreqHz;
            _scanPoints = scanPoints;
        }

        public string TestName => "信号带宽";
        public string Unit => "MHz";

        /// <summary>判据下限（MHz）。null 表示不设下限。M6 阶段占位 0.1。</summary>
        public double? LowerLimit { get; set; } = 0.1;

        /// <summary>判据上限（MHz）。null 表示不设上限。M6 阶段占位 50.0。</summary>
        public double? UpperLimit { get; set; } = 50.0;

        public async Task<TestResult> ExecuteAsync(IProgress<string> progress, CancellationToken ct)
        {
            if (progress == null) throw new ArgumentNullException(nameof(progress));
            ct.ThrowIfCancellationRequested();

            progress.Report("[BandwidthTest] 配置 RF 信号源…");
            Logger.Log("[BandwidthTest] 配置 RF 信号源…");
            _rf.SetFrequency(_centerRfFreqHz);
            _rf.SetPower(_rfPowerDbm);
            _rf.SetOutputState(true);

            progress.Report("[BandwidthTest] 配置频谱仪…");
            _sa.SetCenterFrequency(_ifFreqHz);
            _sa.SetSpan(100_000);
            _sa.SetRbw(1_000);

            // 让信号稳定
            await Task.Delay(200, ct).ConfigureAwait(false);

            // 在中心点读取中心峰值
            progress.Report("[BandwidthTest] 读取中心点峰值…");
            _rf.SetFrequency(_centerRfFreqHz);
            await Task.Delay(50, ct).ConfigureAwait(false);
            double peakCenter = _sa.ReadPeakAmplitude();
            double thresholdDb = peakCenter - 3.0;

            // 扫描收集 (freqOffsetHz, peakDbm) 数据
            var samples = new List<Tuple<double, double>>(_scanPoints);

            double halfWidth = ScanWidthHz / 2.0;
            double stepHz = ScanWidthHz / (_scanPoints - 1);

            progress.Report(string.Format(
                CultureInfo.InvariantCulture,
                "[BandwidthTest] 开始扫描：{0} 点，步进 {1:F1} Hz，阈值 {2:F2} dBm",
                _scanPoints, stepHz, thresholdDb));
            Logger.Log(string.Format(
                "[BandwidthTest] 扫描参数：{0} 点，步进 {1:F1} Hz",
                _scanPoints, stepHz));

            // 一次循环覆盖整个扫描范围（含中心点），避免中心点重复采样
            for (int i = 0; i < _scanPoints; i++)
            {
                ct.ThrowIfCancellationRequested();
                // 对称扫描：i=0 → -halfWidth, i=1 → -halfWidth+step, ..., i=N-1 → +halfWidth
                double offsetHz = -halfWidth + stepHz * i;
                double rfFreq = _centerRfFreqHz + offsetHz;
                _rf.SetFrequency(rfFreq);
                // 短延时让 RF 输出稳定
                await Task.Delay(20, ct).ConfigureAwait(false);
                double peak = _sa.ReadPeakAmplitude();
                samples.Add(new Tuple<double, double>(offsetHz, peak));
                progress.Report(string.Format(
                    CultureInfo.InvariantCulture,
                    "[BandwidthTest]  扫描点 {0}/{1} offset={2:F1}Hz peak={3:F2}dBm",
                    i + 1, _scanPoints, offsetHz, peak));
            }

            // 排序（按 offset）确保后续二分逻辑正确
            samples.Sort((a, b) => a.Item1.CompareTo(b.Item1));

            // 寻找 -3dB 边界：
            //   左侧 = offset < 0 一侧，所有 ≥ 阈值的点中**最左**（最负）的 offset
            //   右侧 = offset > 0 一侧，所有 ≥ 阈值的点中**最右**（最正）的 offset
            // 语义：若扫描范围内全部 ≥ 阈值，则边界退到扫描的两端 → BW = ScanWidth
            double? lowerOffset = null;
            double? upperOffset = null;
            for (int i = 0; i < samples.Count; i++)
            {
                var s = samples[i];
                if (s.Item2 >= thresholdDb)
                {
                    if (s.Item1 <= 0)
                    {
                        // 左侧：保留最左（最负）的
                        if (!lowerOffset.HasValue || s.Item1 < lowerOffset.Value)
                        {
                            lowerOffset = s.Item1;
                        }
                    }
                    if (s.Item1 >= 0)
                    {
                        // 右侧：保留最右（最正）的
                        if (!upperOffset.HasValue || s.Item1 > upperOffset.Value)
                        {
                            upperOffset = s.Item1;
                        }
                    }
                }
            }

            double bandwidthHz = 0.0;
            string detail;
            if (lowerOffset.HasValue && upperOffset.HasValue)
            {
                bandwidthHz = upperOffset.Value - lowerOffset.Value;
                detail = string.Format(
                    CultureInfo.InvariantCulture,
                    "BW={0:F1} Hz（左侧 {1:F1} Hz, 右侧 {2:F1} Hz）",
                    bandwidthHz, lowerOffset.Value, upperOffset.Value);
            }
            else
            {
                detail = string.Format(
                    CultureInfo.InvariantCulture,
                    "BW=N/A（未在阈值 {0:F2} dBm 内找到两侧边界）",
                    thresholdDb);
            }

            double bandwidthMHz = bandwidthHz / 1e6;
            bool pass = (!LowerLimit.HasValue || bandwidthMHz >= LowerLimit.Value)
                     && (!UpperLimit.HasValue || bandwidthMHz <= UpperLimit.Value);

            progress.Report(string.Format(
                CultureInfo.InvariantCulture,
                "[BandwidthTest] peakCenter={0:F2} dBm, threshold={1:F2} dBm, BW={2:F4} MHz, {3}",
                peakCenter, thresholdDb, bandwidthMHz, detail));
            Logger.Log(string.Format(
                "[BandwidthTest] peakCenter={0:F2} dBm, BW={1:F4} MHz, judgment={2}",
                peakCenter, bandwidthMHz, pass ? "Pass" : "Fail"));

            TestResult result = new TestResult
            {
                TestTime = DateTime.Now,
                TestItem = TestName,
                Value = bandwidthMHz,
                Unit = Unit,
                Judgment = pass ? "Pass" : "Fail",
                Remarks = BuildRemarks(samples, thresholdDb, lowerOffset, upperOffset, peakCenter, bandwidthHz)
            };
            TestReportManager.Instance.Record(result);
            return result;
        }

        private static string BuildRemarks(
            List<Tuple<double, double>> samples,
            double thresholdDb,
            double? lowerOffset,
            double? upperOffset,
            double peakCenter,
            double bandwidthHz)
        {
            var sb = new StringBuilder();
            sb.AppendFormat(
                CultureInfo.InvariantCulture,
                "CenterRF={0:F3}MHz@{1}dBm, IF={2:F3}MHz, peakCenter={3:F2}dBm, threshold={4:F2}dBm, BW={5:F4}MHz; ",
                0, 0, 0, peakCenter, thresholdDb, bandwidthHz / 1e6);
            sb.AppendFormat("Points[{0}]: ", samples.Count);
            for (int i = 0; i < samples.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "({0:F0}Hz,{1:F2}dBm)",
                    samples[i].Item1, samples[i].Item2);
            }
            if (lowerOffset.HasValue)
            {
                sb.AppendFormat(CultureInfo.InvariantCulture, "; lower={0:F1}Hz", lowerOffset.Value);
            }
            if (upperOffset.HasValue)
            {
                sb.AppendFormat(CultureInfo.InvariantCulture, "; upper={0:F1}Hz", upperOffset.Value);
            }
            return sb.ToString();
        }
    }
}