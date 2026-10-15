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
    /// AGC 控制特性测试。
    /// <para>业务定义：手动设接收机 AGC 衰减档（4 / 8 / 16 / 32 / 60 dB 共 5 档），
    /// 测 IF 输出实际衰减量与期望档位的偏离。</para>
    /// <para>判据：</para>
    /// <list type="bullet">
    ///   <item>单步进误差 = <c>|abs(delta_i) − 期望档位_i|</c>，单档 <c>≤ 1 dB</c> 单步进合格</item>
    ///   <item>累计误差 = <c>Σ |abs(delta_i) − 期望档位_i|</c>（5 档求和），<c>≤ 3 dB</c> 累计误差 + 增益控制范围合格</item>
    /// </list>
    /// <para>执行流程（每频点）：</para>
    /// <list type="number">
    ///   <item>模式切换 + 频段切换 + RF/LO 设频 + SA 中心配置（同 GainTest/P-1 模式）</item>
    ///   <item>RF/LO 输出打开（持续保持）</item>
    ///   <item>AGC 衰减 = 0 dB 设参考点：读 IF peak → 设 DELTA marker（屏幕显示 0.00 dB）</item>
    ///   <item>循环 5 档：发 AGC 衰减命令 → 等 500ms → 读 abs(delta) → 记单档 Result</item>
    ///   <item>第 5 条 Result 额外记累计误差汇总到 Remarks</item>
    ///   <item>finally 关闭 RF/LO 输出</item>
    /// </list>
    /// </summary>
    public class AgcControlTest : ITestItem
    {
        // ============================================================
        // 常量（与 GainTest / BandwidthTest / InputP1Test 风格统一）
        // ============================================================

        private const double SaReferenceLevelDbm = 10.0;
        private const double SaSpanHz = 50_000_000.0;
        private const int ModeStableMs = 1500;
        private const int BandStableMs = 500;
        private const int RfLoStableMs = 200;
        private const int MaxHoldSettleMs = 500;
        private const int AgcStepStableMs = 500;     // AGC 衰减切换后稳定时间

        private const double SaCenterFreqMode12Hz = 70_000_000.0;
        private const double SaCenterFreqMode3Hz = 750_000_000.0;
        private const double StartRfPowerMode13Dbm = -20.0;
        private const double StartRfPowerMode2Dbm = -10.0;
        private const double LoPowerDbm = 3.0;

        // 单步进 / 累计误差判据
        private const double SingleStepToleranceDb = 1.0;
        private const double CumulativeToleranceDb = 3.0;

        /// <summary>
        /// AGC 衰减测试档位（dB）：4 / 8 / 16 / 32 / 60。
        /// 产品手册指定的 5 个测试档位，覆盖小/中/大档位。
        /// </summary>
        public static readonly int[] AgcTestAttenDbSteps = { 4, 8, 16, 32, 60 };

        // ============================================================
        // 频点配置（每模式代表频点）
        // ============================================================
        public static readonly IReadOnlyDictionary<ReceiverMode, double[]> DefaultTestFreqsMHzPerMode =
            new Dictionary<ReceiverMode, double[]>
            {
                { ReceiverMode.Mode1, new[] { 225.0 } },
                { ReceiverMode.Mode2, new[] { 1090.0 } },
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
        public AgcControlTest(
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
            _modes = modes;
            _testFreqsMHzPerMode = testFreqsMHzPerMode ?? DefaultTestFreqsMHzPerMode;
        }

        public string TestName => "AGC控制特性";

        /// <summary>单位：dB。报表 Value = abs(delta) IF 实际衰减量。</summary>
        public string Unit => "dB";

        /// <summary>判据下限（dB），null。AGC 衰减量 ≥ 0，不设下限。</summary>
        public double? LowerLimit => null;

        /// <summary>判据上限（dB），null。报表 Value 是单档 abs(delta)，本身没有"上限"语义；累计误差单独算。</summary>
        public double? UpperLimit => null;

        public async Task<TestResult> ExecuteAsync(IProgress<string> progress, CancellationToken ct)
        {
            if (progress == null) throw new ArgumentNullException(nameof(progress));
            ct.ThrowIfCancellationRequested();

            if (!_rf.IsConnected || !_lo.IsConnected || !_sa.IsConnected)
            {
                throw new InvalidOperationException("AgcControlTest 需要三台仪表（RF/LO/SA）已连接。");
            }

            // 频谱仪入口
            try { _sa.Reset(); } catch { /* best-effort */ }
            _sa.SetContinuousScan(true);

            // 测试开始时打开 RF/LO 输出；finally 关闭
            try
            {
                _rf.SetOutputState(true);
                _lo.SetOutputState(true);
                Logger.Log("[AgcControlTest] RF/LO 输出已开启。");
            }
            catch (Exception ex)
            {
                Logger.Log("[AgcControlTest] RF/LO :OUTP 1 失败：" + ex.Message);
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
                    progress.Report($"[AgcControlTest] === 进入 {mode} ===");
                    Logger.Log($"[AgcControlTest] === 进入 {mode} ===");

                    double ifFreqHz = (mode == ReceiverMode.Mode3) ? SaCenterFreqMode3Hz : SaCenterFreqMode12Hz;
                    double saCenterHz = ifFreqHz;
                    double rfStartPowerDbm = (mode == ReceiverMode.Mode2) ? StartRfPowerMode2Dbm : StartRfPowerMode13Dbm;

                    // 模式切换：先通道后模式
                    _receiver.SendChannelThenModeCommand(_channel, mode);

                    // 频谱仪：中心 + Span + 参考电平
                    _sa.SetCenterFrequency(saCenterHz);
                    _sa.SetSpan(SaSpanHz);
                    _sa.SetReferenceLevel(SaReferenceLevelDbm);

                    // 模式切换稳定
                    progress.Report($"[AgcControlTest] {mode} 模式切换稳定 {ModeStableMs}ms…");
                    await Task.Delay(ModeStableMs, ct).ConfigureAwait(false);

                    if (!_testFreqsMHzPerMode.TryGetValue(mode, out double[] freqs) || freqs == null || freqs.Length == 0)
                    {
                        string warn = $"未配置 {mode} 的测试频点，跳过。";
                        progress.Report("[AgcControlTest] " + warn);
                        Logger.Log("[AgcControlTest] " + warn);
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
                // 关闭输出
                try { _rf.SetOutputState(false); } catch { /* best-effort */ }
                try { _lo.SetOutputState(false); } catch { /* best-effort */ }
                Logger.Log("[AgcControlTest] RF/LO 输出已关闭（finally）。");
            }

            return lastResult;
        }

        // ============================================================
        // 单频点 AGC 扫描
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
            // 反查 BandInfo
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
                progress.Report("[AgcControlTest] " + err);
                Logger.Log("[AgcControlTest] " + err);
                return null;
            }

            double rfFreqHz = freqMHz * 1_000_000.0;
            double loFreqHz = LoFrequencyRule.Calculate(rfFreqHz, (int)mode);

            // 设频（与 P-1 同款：RF/LO 设频在频段命令之前）
            _rf.SetFrequency(rfFreqHz);
            _lo.SetFrequency(loFreqHz);
            await Task.Delay(RfLoStableMs, ct).ConfigureAwait(false);

            // 频段命令
            _receiver.SendBandCommand(mode, band.BandIndex);
            await Task.Delay(BandStableMs, ct).ConfigureAwait(false);

            // 重置 SA 状态：先关再开最大保持 + 清 marker
            _sa.SetMaxHold(false);
            _sa.SetMaxHold(true);
            _sa.SetAllMarkOFF();

            // 起步 RF 功率
            _rf.SetPower(rfStartPowerDbm);
            await Task.Delay(RfLoStableMs, ct).ConfigureAwait(false);

            // AGC = 0 dB 设参考点（不记 Result）
            _receiver.SendAttenuationCommand(_channel, 0);
            await Task.Delay(MaxHoldSettleMs, ct).ConfigureAwait(false);

            // 激活 marker1 + 设 DELTA 模式
            try
            {
                _sa.QueryString(":CALC:MARK1:STAT ON");
                _sa.QueryString(":CALC:MARK1:MAX:PEAK");
                _sa.QueryString(":CALC:MARK1:MAX:PEAK:SEARCH");
                _sa.EnableDeltaMarker();   // :CALC:MARK1:MODE DELTA，参考点 = 当前 peak
            }
            catch (Exception ex)
            {
                string err = $"{mode} {band.DisplayName} 设 DELTA marker 失败：{ex.Message}";
                progress.Report("[AgcControlTest] " + err);
                Logger.Log("[AgcControlTest] " + err);
                return null;
            }

            // 5 档扫描
            // 收集：每档 abs(delta)、单步进误差；最后算累计误差
            var stepResults = new List<(int AttenDb, double AbsDeltaDb, double StepErrorDb)>(AgcTestAttenDbSteps.Length);
            bool anyStepFailed = false;
            string stepFailReason = null;

            for (int idx = 0; idx < AgcTestAttenDbSteps.Length; idx++)
            {
                ct.ThrowIfCancellationRequested();
                int targetAtten = AgcTestAttenDbSteps[idx];

                // 发 AGC 衰减命令
                try
                {
                    _receiver.SendAttenuationCommand(_channel, targetAtten);
                }
                catch (Exception ex)
                {
                    stepFailReason = $"发 AGC={targetAtten}dB 失败：{ex.Message}";
                    break;
                }
                await Task.Delay(AgcStepStableMs, ct).ConfigureAwait(false);

                // 读 delta 累积值
                double currentDeltaDb;
                try
                {
                    string resp = _sa.QueryString(":CALC:MARK1:Y?");
                    if (!double.TryParse(resp, NumberStyles.Float, CultureInfo.InvariantCulture, out currentDeltaDb))
                    {
                        stepFailReason = $"读 delta Y 失败：'{resp}'";
                        break;
                    }
                }
                catch (Exception ex)
                {
                    stepFailReason = $"读 delta Y 异常：{ex.Message}";
                    break;
                }

                double absDelta = Math.Abs(currentDeltaDb);
                double stepError = Math.Abs(absDelta - targetAtten);

                stepResults.Add((targetAtten, absDelta, stepError));

                progress.Report(string.Format(
                    CultureInfo.InvariantCulture,
                    "[AgcControlTest] {0} {1} ({2}/{3}) step {4}/{5}: AGC={6}dB, abs(delta)={7:F2}dB, 单步进误差={8:F2}dB",
                    mode, band.DisplayName, done, total, idx + 1, AgcTestAttenDbSteps.Length,
                    targetAtten, absDelta, stepError));
                Logger.Log(string.Format(
                    CultureInfo.InvariantCulture,
                    "[AgcControlTest] {0} {1} step {2}/{3}: AGC={4}dB, abs(delta)={5:F2}dB, 单步进误差={6:F2}dB",
                    mode, band.DisplayName, idx + 1, AgcTestAttenDbSteps.Length,
                    targetAtten, absDelta, stepError));

                if (stepError > SingleStepToleranceDb) anyStepFailed = true;
            }

            if (stepFailReason != null)
            {
                progress.Report($"[AgcControlTest] {mode} {band.DisplayName}: {stepFailReason}");
                Logger.Log($"[AgcControlTest] {mode} {band.DisplayName}: {stepFailReason}");
                return null;
            }

            // 累计误差 = Σ |abs(delta_i) − 期望档位_i|  (F1)
            double cumulativeError = 0;
            for (int i = 0; i < stepResults.Count; i++)
            {
                cumulativeError += stepResults[i].StepErrorDb;
            }
            bool cumulativeOk = cumulativeError <= CumulativeToleranceDb;

            // P3: Judgment = (单步进全 ≤ 1 dB) AND (累计 ≤ 3 dB)
            bool allOk = !anyStepFailed && cumulativeOk;
            string judgment = allOk ? "Pass" : "Fail";

            // 写 5 条 Result（R2：前 4 条 Remarks 简洁；第 5 条含累计误差汇总）
            TestResult lastWritten = null;
            for (int i = 0; i < stepResults.Count; i++)
            {
                var (atten, absDelta, stepErr) = stepResults[i];
                bool stepPass = stepErr <= SingleStepToleranceDb;

                string remarks;
                if (i < stepResults.Count - 1)
                {
                    // 前 N-1 条：只记自己的 abs(delta) + 单步进误差
                    remarks = string.Format(
                        CultureInfo.InvariantCulture,
                        "AGC={0}dB, abs(delta)={1:F2}dB, 单步进误差={2:F2}dB ({3})",
                        atten, absDelta, stepErr,
                        stepPass ? "≤1dB合格" : ">1dB不合格");
                }
                else
                {
                    // 末条：含累计误差汇总
                    remarks = string.Format(
                        CultureInfo.InvariantCulture,
                        "AGC={0}dB, abs(delta)={1:F2}dB, 单步进误差={2:F2}dB ({3}); 累计误差={4:F2}dB (≤3dB{5}); 5档 abs(delta)=[4:{6:F2}, 8:{7:F2}, 16:{8:F2}, 32:{9:F2}, 60:{10:F2}]",
                        atten, absDelta, stepErr,
                        stepPass ? "≤1dB合格" : ">1dB不合格",
                        cumulativeError,
                        cumulativeOk ? "合格" : "不合格",
                        stepResults[0].AbsDeltaDb, stepResults[1].AbsDeltaDb, stepResults[2].AbsDeltaDb,
                        stepResults[3].AbsDeltaDb, stepResults[4].AbsDeltaDb);
                }

                TestResult r = new TestResult
                {
                    TestTime = DateTime.Now,
                    Channel = _channel,
                    Mode = mode.ToString(),
                    BandIndex = band.BandIndex,
                    BandDisplayName = band.DisplayName,
                    TestItem = TestName,
                    Value = absDelta,
                    Unit = Unit,
                    Judgment = judgment,   // 5 条共用同一 judgment（P3 = 单步进全过 AND 累计 ≤ 3 dB）
                    Remarks = remarks,
                };
                TestReportManager.Instance.Record(r);
                lastWritten = r;
            }

            progress.Report(string.Format(
                CultureInfo.InvariantCulture,
                "[AgcControlTest] {0} {1} ({2}/{3}): 累计误差={4:F2}dB → {5}",
                mode, band.DisplayName, done, total, cumulativeError, judgment));
            Logger.Log(string.Format(
                CultureInfo.InvariantCulture,
                "[AgcControlTest] {0} {1}: 累计误差={2:F2}dB → {3}",
                mode, band.DisplayName, cumulativeError, judgment));

            // 复位：关最大保持（切回 WRITe），DELTA 在下个频点重设
            _sa.SetMaxHold(false);
            await Task.Delay(MaxHoldSettleMs, ct).ConfigureAwait(false);

            return lastWritten;
        }
    }
}
