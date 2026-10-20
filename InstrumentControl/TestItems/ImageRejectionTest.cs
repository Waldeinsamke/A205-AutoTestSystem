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
    /// 镜频抑制测试（仅 Mode3）。
    /// <para>物理含义：测量接收机对"镜频干扰"的抑制度，单位 dB（数值越大越好）。
    /// 镜频是混频器 0/IF 的对称分量：RF=−LO→ IF（真信号），RF=+LO→ IF（镜频泄漏）。
    /// 因此同一中频对应两个 RF 频率：
    /// <list type="bullet">
    ///   <item>真信号 RF = <c>f0</c>（目标测试频点）。</item>
    ///   <item>一镜频 RF = <c>f0 + 2 × 4350</c> MHz（差频 = 2× 4350，对称泄漏到 IF）。</item>
    ///   <item>二镜频 RF = <c>f0 + 2 × 750</c> MHz（差频 = 2× 750）。</item>
    /// </list>
    /// 测试时让接收机本振固定在真信号频率上对应的 LO，把 RF 切到"一/二镜频"，
    /// 此时 IF 真信号沉到底噪位置，频谱仪上能看到的就是"镜频泄漏到 IF"的相对值。
    /// 再用 DELTA marker 测出它与真信号峰的差值（取绝对值），就是该镜频的抑制度。</para>
    /// <para>测试方法（按用户决策）：</para>
    /// <list type="number">
    ///   <item>仅 Mode3。工作频点 4 个：900 / 1500 / 2000 / 3000 MHz（用户给定）。</item>
    ///   <item>准备工作：模式切换 + 频段命令 + RF/LO 设频 + 开 RF/LO 输出（仿线性最大增益）。</item>
    ///   <item>SA 激活 marker1 + MAX:PEAK + DELTA（参考点 = 真信号峰，屏幕显示 0 dB）。
    ///         按用户决策：DELTA 参考点是否真锁到 ≈0 不强制校验，直接往下走。</item>
    ///   <item>RF 切到一镜频（LO 不动），读 DELTA Y?，记 <c>abs(delta_1)</c>。</item>
    ///   <item>RF 切回真信号 f0（LO 不动），重设 DELTA → RF 切到二镜频，读 <c>abs(delta_2)</c>。</item>
    /// </list>
    /// <para>频点（用户给定）仅 Mode3：900 / 1500 / 2000 / 3000 MHz。</para>
    /// <para>阈值（用户给定）：一镜频 / 二镜频均 ≥ 70 dB。报表按"每 f0 2 条（一镜频 + 二镜频）"落盘。</para>
    /// </summary>
    public class ImageRejectionTest : ITestItem
    {
        // ============================================================
        // 常量（与 IfRejectionTest / HarmonicRejectionTest / GainTest 风格统一；
        //      如修改需同步 ReceiverControlPanel 等地方）
        // ============================================================

        private const double SaReferenceLevelDbm = 10.0;
        private const double SaSpanHz = 50_000_000.0;
        private const double SaCenterFreqMode3Hz = 750_000_000.0;
        private const double StartRfPowerMode3Dbm = -20.0;
        private const double LoPowerDbm = 3.0;

        /// <summary>
        /// 镜频抑制测试全程 RBW（kHz）。按用户决策：30 kHz。窄 RBW 降低底噪，
        /// 让微弱镜频泄漏信号能被 marker 找到（与 IfRejectionTest 选窄 RBW 同款思路）。
        /// </summary>
        private const double SaRbwKHz = 30.0;

        /// <summary>
        /// 镜频抑制测试全程 VBW（kHz）。按用户决策：30 kHz。
        /// </summary>
        private const double SaVbwKHz = 30.0;

        private const int ModeStableMs = 1500;
        private const int BandStableMs = 500;
        private const int RfLoStableMs = 200;
        private const int TraceSettleMs = 500;
        private const int MarkerPostSetupDelayMs = 500;
        private const int ImageRFSwitchStableMs = 1000;

        /// <summary>
        /// 切到镜频频率后频谱仪参考电平（dBm）。
        /// 镜频泄漏信号极弱；与 IfRejectionTest 同款：屏顶压到 −20 dBm 让 marker
        /// 在更密的单位刻度下能找到真峰。读完 |delta| 立刻恢复为 10 dBm。
        /// </summary>
        private const double ImageReferenceLevelDbm = -20.0;

        // 镜频公式（差频 = 2 × 一中频 = 2 × 4350 MHz；2 × 二中频 = 2 × 750 MHz）
        private const double FirstIfOffsetHz = 4_350_000_000.0;
        private const double SecondIfOffsetHz = 750_000_000.0;

        // 判据（用户给定）：一/二镜频均 ≥ 70 dB
        private const double ImageLowerLimitDb = 70.0;

        // ============================================================
        // 频点配置（用户给定：Mode3 900 / 1500 / 2000 / 3000 MHz）
        // ============================================================
        public static readonly IReadOnlyDictionary<ReceiverMode, double[]> DefaultTestFreqsMHzPerMode =
            new Dictionary<ReceiverMode, double[]>
            {
                { ReceiverMode.Mode3, new[] { 900.0, 1500.0, 2000.0, 3000.0 } },
            };

        // ============================================================
        // 字段
        // ============================================================
        private readonly ReceiverSerialPort _receiver;
        private readonly SignalGenerator _rf;
        private readonly SignalGeneratorLO _lo;
        private readonly SpectrumAnalyzer _sa;
        private readonly int _channel;
        private readonly IReadOnlyList<ReceiverMode> _modes;
        private readonly IReadOnlyDictionary<ReceiverMode, double[]> _testFreqsMHzPerMode;

        // ============================================================
        // 构造
        // ============================================================
        public ImageRejectionTest(
            ReceiverSerialPort receiver,
            SignalGenerator rfGenerator,
            SignalGeneratorLO loGenerator,
            SpectrumAnalyzer spectrumAnalyzer,
            int channel,
            IReadOnlyList<ReceiverMode> modes = null,
            IReadOnlyDictionary<ReceiverMode, double[]> testFreqsMHzPerMode = null)
        {
            if (receiver == null) throw new ArgumentNullException(nameof(receiver));
            if (rfGenerator == null) throw new ArgumentNullException(nameof(rfGenerator));
            if (loGenerator == null) throw new ArgumentNullException(nameof(loGenerator));
            if (spectrumAnalyzer == null) throw new ArgumentNullException(nameof(spectrumAnalyzer));
            if (channel < 1 || channel > 8)
                throw new ArgumentOutOfRangeException(nameof(channel), channel, "通道号必须在 1~8 范围内。");
            if (modes == null || modes.Count == 0)
                throw new ArgumentException("必须至少传入一个工作模式。", nameof(modes));

            _receiver = receiver;
            _rf = rfGenerator;
            _lo = loGenerator;
            _sa = spectrumAnalyzer;
            _channel = channel;
            // 强制 Mode3：按用户要求"该测试项均在模式3下测试"
            _modes = new ReceiverMode[] { ReceiverMode.Mode3 };
            _testFreqsMHzPerMode = testFreqsMHzPerMode ?? DefaultTestFreqsMHzPerMode;
        }

        public string TestName => "镜频抑制";
        public string Unit => "dB";

        /// <summary>判据下限（dB），UI 单行展示。实际判定在 ExecuteOneFreqAsync 内按镜像类型取。</summary>
        public double? LowerLimit => ImageLowerLimitDb;
        public double? UpperLimit => null;

        public async Task<TestResult> ExecuteAsync(IProgress<string> progress, CancellationToken ct)
        {
            if (progress == null) throw new ArgumentNullException(nameof(progress));
            ct.ThrowIfCancellationRequested();

            if (!_rf.IsConnected || !_lo.IsConnected || !_sa.IsConnected)
            {
                throw new InvalidOperationException("ImageRejectionTest 需要三台仪表（RF/LO/SA）已连接。");
            }

            // 频谱仪入口
            try { _sa.Reset(); } catch { /* best-effort */ }
            _sa.SetContinuousScan(true);

            try
            {
                _rf.SetOutputState(true);
                _lo.SetOutputState(true);
                Logger.Log("[ImageRejectionTest] RF/LO 输出已开启（每 f0 末尾 finally 统一关）。");
            }
            catch (Exception ex)
            {
                Logger.Log("[ImageRejectionTest] RF/LO :OUTP 1 失败：" + ex.Message);
            }

            TestResult lastResult = null;
            int totalFreqs = 0;
            foreach (var mode in _modes)
            {
                if (_testFreqsMHzPerMode.TryGetValue(mode, out var fs) && fs != null) totalFreqs += fs.Length;
            }
            int doneFreqs = 0;

            try
            {
                foreach (var mode in _modes)
                {
                    ct.ThrowIfCancellationRequested();
                    progress.Report($"[ImageRejectionTest] === 进入 {mode} ===");
                    Logger.Log($"[ImageRejectionTest] === 进入 {mode} ===");

                    double ifFreqHz = SaCenterFreqMode3Hz;
                    double saCenterHz = ifFreqHz;
                    double rfStartPowerDbm = StartRfPowerMode3Dbm;

                    // 模式切换：先通道后模式（与 GainTest 同款）
                    _receiver.SendChannelThenModeCommand(_channel, mode);

                    // 频谱仪：中心 + Span + 参考电平 + RBW/VBW（按用户决策 RBW/VBW=30kHz 全程）
                    _sa.SetCenterFrequency(saCenterHz);
                    _sa.SetSpan(SaSpanHz);
                    _sa.SetReferenceLevel(SaReferenceLevelDbm);
                    _sa.SetResolutionBandwidth(SaRbwKHz);
                    _sa.SetVideoBandwidth(SaVbwKHz);

                    // 模式切换稳定
                    progress.Report($"[ImageRejectionTest] {mode} 模式切换稳定 {ModeStableMs}ms…");
                    await Task.Delay(ModeStableMs, ct).ConfigureAwait(false);

                    if (!_testFreqsMHzPerMode.TryGetValue(mode, out double[] freqs) || freqs == null || freqs.Length == 0)
                    {
                        string warn = $"未配置 {mode} 的测试频点，跳过。";
                        progress.Report("[ImageRejectionTest] " + warn);
                        Logger.Log("[ImageRejectionTest] " + warn);
                        continue;
                    }

                    foreach (double freqMHz in freqs)
                    {
                        ct.ThrowIfCancellationRequested();
                        var r = await ExecuteOneFreqAsync(
                            mode, freqMHz, rfStartPowerDbm, ifFreqHz, ++doneFreqs, totalFreqs, progress, ct).ConfigureAwait(false);
                        if (r != null) lastResult = r;
                    }
                }
            }
            finally
            {
                try { _rf.SetOutputState(false); } catch { /* best-effort */ }
                try { _lo.SetOutputState(false); } catch { /* best-effort */ }
                Logger.Log("[ImageRejectionTest] RF/LO 输出已关闭（finally）。");
            }

            return lastResult;
        }

        // ============================================================
        // 单 f0 镜频抑制（一镜频 + 二镜频）
        // ============================================================
        private async Task<TestResult> ExecuteOneFreqAsync(
            ReceiverMode mode,
            double freqMHz,
            double rfStartPowerDbm,
            double ifFreqHz,
            int done,
            int total,
            IProgress<string> progress,
            CancellationToken ct)
        {
            // 反查 BandInfo（按显示名 "频段XXXX" 找 bandIndex）
            string displayName = "频段" + ((int)Math.Round(freqMHz)).ToString(CultureInfo.InvariantCulture);
            ModeBandTable.BandInfo band = null;
            foreach (var b in ModeBandTable.GetBands(mode))
            {
                if (b.DisplayName == displayName)
                {
                    band = b;
                    break;
                }
            }
            if (band == null)
            {
                string err = $"{mode} {freqMHz} MHz：未在 ModeBandTable 中找到对应频段（{displayName}），跳过。";
                progress.Report("[ImageRejectionTest] " + err);
                Logger.Log("[ImageRejectionTest] " + err);
                return null;
            }

            double rfFreqHz = freqMHz * 1_000_000.0;
            double loFreqHz = LoFrequencyRule.Calculate(rfFreqHz, (int)mode);

            // 镜像频率（RF 目标值，LO 始终保持不动）
            double firstImageRfHz = (freqMHz * 1_000_000.0) + 2 * FirstIfOffsetHz;
            double secondImageRfHz = (freqMHz * 1_000_000.0) + 2 * SecondIfOffsetHz;

            // -------- 准备工作：按线性最大增益，让 IF 真信号稳定 --------

            // 设频（按"推测 9 + Q2"：RF/LO 设频在频段命令之前）
            _rf.SetFrequency(rfFreqHz);
            _lo.SetFrequency(loFreqHz);
            await Task.Delay(RfLoStableMs, ct).ConfigureAwait(false);

            // 频段命令（关键：用户特别强调"记得发送频点切换命令"）
            _receiver.SendBandCommand(mode, band.BandIndex);
            await Task.Delay(BandStableMs, ct).ConfigureAwait(false);

            // 重置 SA：清 marker
            _sa.SetAllMarkOFF();

            // 起步 RF 功率
            _rf.SetPower(rfStartPowerDbm);
            _lo.SetPower(LoPowerDbm);
            await Task.Delay(RfLoStableMs, ct).ConfigureAwait(false);

            // 打开 RF/LO 输出
            try
            {
                _rf.SetOutputState(true);
                _lo.SetOutputState(true);
            }
            catch (Exception ex)
            {
                Logger.Log($"[ImageRejectionTest] {mode} {band.DisplayName} 开 RF/LO 输出失败：{ex.Message}");
            }

            // 等 IF 稳定 → marker1 MAX:PEAK → DELTA
            await Task.Delay(TraceSettleMs, ct).ConfigureAwait(false);
            try
            {
                _sa.SendCommand(":CALC:MARK1:STAT ON");
                _sa.SendCommand(":CALC:MARK1:MAX:PEAK");
                _sa.SendCommand(":CALC:MARK1:MAX:PEAK:SEARCH");
                _sa.EnableDeltaMarker();
            }
            catch (Exception ex)
            {
                string err = $"{mode} {band.DisplayName} 设 DELTA marker 失败：{ex.Message}";
                progress.Report("[ImageRejectionTest] " + err);
                Logger.Log("[ImageRejectionTest] " + err);
                return null;
            }
            // 等 marker1 PEAK 收敛
            await Task.Delay(MarkerPostSetupDelayMs, ct).ConfigureAwait(false);

            // -------- 一镜频测量 --------
            double firstImageDeltaDb = await MeasureImageDeltaAsync(
                mode, band.DisplayName, firstImageRfHz, "一镜频", progress, ct).ConfigureAwait(false);

            // 一镜频完毕 → 恢复真信号 + 重设 DELTA
            _rf.SetFrequency(rfFreqHz);
            _sa.SetReferenceLevel(SaReferenceLevelDbm);
            await Task.Delay(ImageRFSwitchStableMs, ct).ConfigureAwait(false);

            try
            {
                _sa.SetAllMarkOFF();
                _sa.SendCommand(":CALC:MARK1:STAT ON");
                _sa.SendCommand(":CALC:MARK1:MAX:PEAK");
                _sa.SendCommand(":CALC:MARK1:MAX:PEAK:SEARCH");
                _sa.EnableDeltaMarker();
            }
            catch (Exception ex)
            {
                Logger.Log($"[ImageRejectionTest] {mode} {band.DisplayName} 重新设 DELTA marker（一镜频→二镜频过渡）失败：{ex.Message}");
            }
            await Task.Delay(MarkerPostSetupDelayMs, ct).ConfigureAwait(false);

            // -------- 二镜频测量 --------
            double secondImageDeltaDb = await MeasureImageDeltaAsync(
                mode, band.DisplayName, secondImageRfHz, "二镜频", progress, ct).ConfigureAwait(false);

            // -------------------------------------------------------------------
            // 落盘 2 条 Result（一镜频 + 二镜频）
            // -------------------------------------------------------------------
            TestResult lastWritten = null;
            lastWritten = RecordImageResult(
                mode, band, freqMHz, ifFreqHz,
                "一镜频", firstImageRfHz, firstImageDeltaDb,
                "RF={0}MHz（f0+2×4350MHz={1}MHz）；LO不动；屏参考电平={2}dBm",
                done, total, progress);

            RecordImageResult(
                mode, band, freqMHz, ifFreqHz,
                "二镜频", secondImageRfHz, secondImageDeltaDb,
                "RF={0}MHz（f0+2×750MHz={1}MHz）；LO不动；屏参考电平={2}dBm",
                done, total, progress);

            // 复位：恢复参考电平 + 清 marker
            _sa.SetReferenceLevel(SaReferenceLevelDbm);
            _sa.SetAllMarkOFF();

            return lastWritten;
        }

        // ----------------------------------------------------------------
        // 镜像频率 DELTA 测量（公共子流程：切 RF + 屏顶−20dBm + 读 delta）
        // ----------------------------------------------------------------
        private async Task<double> MeasureImageDeltaAsync(
            ReceiverMode mode, string bandDisplayName,
            double imageRfHz, string tag,
            IProgress<string> progress, CancellationToken ct)
        {
            // 切 RF 到镜像频率（LO 不动）
            _rf.SetFrequency(imageRfHz);
            // IF 输出端只剩"镜像泄漏"，信号极弱；与 IfRejectionTest 同款：屏顶压到 −20 dBm
            _sa.SetReferenceLevel(ImageReferenceLevelDbm);
            await Task.Delay(ImageRFSwitchStableMs, ct).ConfigureAwait(false);

            double deltaDb;
            try
            {
                string resp = _sa.QueryString(":CALC:MARK1:Y?");
                if (!double.TryParse(resp, NumberStyles.Float, CultureInfo.InvariantCulture, out deltaDb))
                {
                    string err = $"{mode} {bandDisplayName} {tag} 读 delta Y 失败：'{resp}'";
                    progress.Report("[ImageRejectionTest] " + err);
                    Logger.Log("[ImageRejectionTest] " + err);
                    deltaDb = double.NaN;
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[ImageRejectionTest] {mode} {bandDisplayName} {tag} 读 delta Y 异常：{ex.Message}");
                deltaDb = double.NaN;
            }

            progress.Report(string.Format(
                CultureInfo.InvariantCulture,
                "[ImageRejectionTest] {0} {1} {2}: RF→{3:F1}MHz, delta={4:F1}dB, |抑制|={5:F1}dB",
                mode, bandDisplayName, tag,
                imageRfHz / 1e6,
                deltaDb,
                double.IsNaN(deltaDb) ? 0.0 : Math.Abs(deltaDb)));
            Logger.Log(string.Format(
                CultureInfo.InvariantCulture,
                "[ImageRejectionTest] {0} {1} {2}: RF→{3:F1}MHz, delta={4:F1}dB, |抑制|={5:F1}dB",
                mode, bandDisplayName, tag,
                imageRfHz / 1e6,
                deltaDb,
                double.IsNaN(deltaDb) ? 0.0 : Math.Abs(deltaDb)));

            // 恢复到默认参考电平，为下一次（恢复真信号 / 测另一镜像）准备
            _sa.SetReferenceLevel(SaReferenceLevelDbm);

            return deltaDb;
        }

        // ----------------------------------------------------------------
        // 把单条镜像测量结果落盘成 1 条 TestResult（按用户决策：每 f0 落 2 条——一镜频 + 二镜频）
        // ----------------------------------------------------------------
        private TestResult RecordImageResult(
            ReceiverMode mode, ModeBandTable.BandInfo band, double freqMHz, double ifFreqHz,
            string tag, double imageRfHz, double deltaDb,
            string remarksFormat, int done, int total,
            IProgress<string> progress)
        {
            double absDb = double.IsNaN(deltaDb) ? double.NaN : Math.Abs(deltaDb);
            bool pass = !double.IsNaN(absDb) && absDb >= ImageLowerLimitDb;

            string remarks = string.Format(
                CultureInfo.InvariantCulture,
                "f0=" + remarksFormat,
                freqMHz,
                imageRfHz / 1e6,
                ImageReferenceLevelDbm)
                + string.Format(
                    CultureInfo.InvariantCulture,
                    ", IF={0:F1}MHz, delta={1:F1}dB, |抑制|={2:F1}dB (≥{3:F1}dB{4})",
                    ifFreqHz / 1e6,
                    deltaDb,
                    double.IsNaN(absDb) ? 0.0 : absDb,
                    ImageLowerLimitDb,
                    pass ? "合格" : "不合格");

            TestResult result = new TestResult
            {
                TestTime = DateTime.Now,
                Channel = _channel,
                Mode = mode.ToString(),
                BandIndex = band.BandIndex,
                BandDisplayName = band.DisplayName,
                TestItem = $"{TestName}·{tag}",
                Value = double.IsNaN(absDb) ? double.NaN : absDb,
                Unit = Unit,
                Judgment = double.IsNaN(absDb) ? "Fail" : (pass ? "Pass" : "Fail"),
                Remarks = remarks,
            };
            TestReportManager.Instance.Record(result);

            progress.Report(string.Format(
                CultureInfo.InvariantCulture,
                "[ImageRejectionTest] {0} {1} ({2}/{3}) {4}: |抑制|={5:F1}dB (≥{6:F1}dB) → {7}",
                mode, band.DisplayName, done, total, tag,
                double.IsNaN(absDb) ? 0.0 : absDb,
                ImageLowerLimitDb, result.Judgment));
            Logger.Log(string.Format(
                CultureInfo.InvariantCulture,
                "[ImageRejectionTest] {0} {1} {2}: |抑制|={3:F1}dB → {4}",
                mode, band.DisplayName, tag,
                double.IsNaN(absDb) ? 0.0 : absDb, result.Judgment));

            return result;
        }
    }
}
