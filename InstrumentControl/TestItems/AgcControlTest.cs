using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using A205AutoTestSystem.AutoTest;
using A205AutoTestSystem.AutoTest.Shared;
using A205AutoTestSystem.Common;
using A205AutoTestSystem.InstrumentControl;
using A205AutoTestSystem.Models;
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
        /// <summary>
        /// AGC=60 dB 单档专用参考电平（dBm）。AGC=60 时 IF 信号已沉到噪声底附近，
        /// 默认 10 dBm 参考电平下信号看不清；切到 −10 dBm 把屏顶压缩到 −10 附近
        /// （让 IF 信号在更密的单位刻度下浮出来），从而能读到真实 delta。
        /// </summary>
        private const double Agc60dBReferenceLevelDbm = -10.0;
        private const double SaSpanHz = 50_000_000.0;
        private const int ModeStableMs = 1500;
        private const int BandStableMs = 500;
        private const int RfLoStableMs = 200;
        // AGC=0dB 设参考点前等待时间 + 频点末切换频段前等待时间。
        // 早先带 "MaxHold" 前缀是因为配合 :TRACE1:MODE MAXH 用，现已不用 MAXH（理由见
        // ExecuteOneFreqAsync 入口注释）。但这个常量语义仍是"等频谱仪在标准 sweep 模式下
        // 把新频点的 trace 扫稳定、marker1 找到真实 peak 后再设 DELTA 模式锁定参考点"，
        // 因此改名 TraceSettleMs 保留。
        // 实测 500ms 已够用，无需再放大。
        private const int TraceSettleMs = 500;
        /// <summary>
        /// AGC=0 设完 marker1 + DELTA 之后、5 档循环启动前的额外等待。
        /// 让频谱仪在 MAX:PEAK 状态上多扫几帧，
        /// 确保 marker1 已锁在真 peak 上、DELTA 参考点是有效基准，
        /// 再开始切各档衰减。500ms 紧贴 marker1 设上即读会读到 dangle 值。
        /// </summary>
        private const int MarkerPostSetupDelayMs = 500;
        // AGC 衰减切换后稳定时间：等待 IF 信号在 AGC 衰减器物理切换 + 接收机内部重锁相 +
        // 频谱仪在标准 sweep 模式（不用 MAXH，理由见 ExecuteOneFreqAsync 入口注释）
        // 下扫到新档位的真实 IF peak 之后再读 :CALC:MARK1:Y?，
        // 避免读到 dangle state 导致 abs(delta)=0。
        // 500ms 实测不够稳定（每档读数全为 0），改为 1500ms 留余量。
        private const int AgcStepStableMs = 1500;    // AGC 衰减切换后稳定时间

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
                { ReceiverMode.Mode1, new[] { 220.0 } },
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

                // 复位 AGC 衰减到 0 dB。
                // 本测试项在 5 档扫描（4/8/16/32/60 dB）后会话残留最后一档的衰减，
                // 若不在此归零，接收机的衰减状态会被后续测试项继承：
                //   - ImageRejectionTest：不发任何衰减命令，直接读 IF 真信号峰作 DELTA 参考点，
                //     带着 60 dB 衰减会把参考点压到噪声底，导致镜频抑制读数虚高（虚假 Pass）。
                //   - 手动单独运行本测试项后，接收机也会停留在高衰减状态。
                // 注意：频点之间无需复位——每个频点开头（ExecuteOneFreqAsync 内）本就会
                // 发 SendAttenuationCommand(_channel, 0) 设 DELTA 参考点。
                try { _receiver.SendAttenuationCommand(_channel, 0); } catch { /* best-effort */ }

                Logger.Log("[AgcControlTest] RF/LO 输出已关闭，AGC 衰减已复位 0dB（finally）。");
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

            // 重置 SA 状态：清 marker，**不开 MAXH**。
            // MAXH（最大保持）会保留历史最高值，而 AGC 测试是要测 IF 峰值
            // 在 AGC 衰减下逐档变低；MAXH 模式下 trace 不会随信号降低而更新，
            // marker1 PEAK 在每个 AGC 档都跑回到 MAXH 最初累积的最大峰上，
            // DELTA 的"参考点"也被锁在这个旧峰，导致 :CALC:MARK1:Y? 持续读到
            // 异常值（Dangle 状态，5 档全为 0）。
            // 这里只切到标准 sweep 模式（默认即 WRITe，不再 :TRACE1:MODE MAXH），
            // 同时清掉 marker 让频点从干净状态开始。
            _sa.SetAllMarkOFF();

            // 起步 RF 功率
            _rf.SetPower(rfStartPowerDbm);
            await Task.Delay(RfLoStableMs, ct).ConfigureAwait(false);

            // AGC = 0 dB 设参考点（不记 Result）。
            // 等 TraceSettleMs 让 marker1 在标准 sweep 模式下找到真 peak，
            // 再设 :CALC:MARK1:MODE DELTA 锁定参考点（下面 try 块）。
            _receiver.SendAttenuationCommand(_channel, 0);
            await Task.Delay(TraceSettleMs, ct).ConfigureAwait(false);

            // 激活 marker1 + 设 DELTA 模式
            try
            {
                // 注意：SCPI 的 STAT ON / MAX:PEAK / MAX:PEAK:SEARCH / MODE DELTA 都是 set 命令（无 '?'），
                //       必须用 SendCommand；用 QueryString 会因为"等不到响应"触发 IOTimeoutException，
                //       而该异常的 HResult 又不被 ExecuteWithRetry 识别为瞬态错误，
                //       最终会走 non-transient 分支触发 MarkInstrumentLost，导致频谱仪被误判为"已断开"。
                _sa.SendCommand(":CALC:MARK1:STAT ON");
                _sa.SendCommand(":CALC:MARK1:MAX:PEAK");
                _sa.SendCommand(":CALC:MARK1:MAX:PEAK:SEARCH");
                _sa.EnableDeltaMarker();   // :CALC:MARK1:MODE DELTA，参考点 = 当前 peak
            }
            catch (Exception ex)
            {
                string err = $"{mode} {band.DisplayName} 设 DELTA marker 失败：{ex.Message}";
                progress.Report("[AgcControlTest] " + err);
                Logger.Log("[AgcControlTest] " + err);
                return null;
            }

            // 设完 marker1 + DELTA 之后、5 档循环启动之前多等一拍，
            // 让频谱仪在标准 sweep 模式下多扫几帧，marker1 锁到真 peak，
            // DELTA 参考点为有效基准，避免首档衰减读到 dangle 值。
            await Task.Delay(MarkerPostSetupDelayMs, ct).ConfigureAwait(false);

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

                // 仅 AGC=60 dB：参考电平压到 −15 dBm 把 IF 信号从噪声底里抬出来
                // 让频谱仪在更密的单位刻度下能读到真实 delta；其余档保持默认 10 dBm。
                if (targetAtten == 60)
                {
                    try
                    {
                        _sa.SetReferenceLevel(Agc60dBReferenceLevelDbm);
                    }
                    catch (Exception ex)
                    {
                        stepFailReason = $"AGC=60dB 切参考电平到 {Agc60dBReferenceLevelDbm}dBm 失败：{ex.Message}";
                        break;
                    }
                }

                // 衰减命令发出后立即切参考电平（仅 60dB 档），然后等 AgcStepStableMs
                // 让接收机内部稳态 + 频谱仪 sweeptime 收敛，再读 Y?。
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

            // 复位：清 marker + 恢复参考电平（AGC=60 档可能被切到了 −15 dBm） + 等下一频点 trace 收敛。
            // 不再切 MAXH（见 ExecuteOneFreqAsync 入口注释），
            // DELTA 在下个频点重设即可。
            _sa.SetAllMarkOFF();
            try
            {
                _sa.SetReferenceLevel(SaReferenceLevelDbm);   // 恢复默认 10 dBm
            }
            catch (Exception ex)
            {
                // 恢复失败只记日志，不影响主流程
                Logger.Log($"[AgcControlTest] {mode} {band.DisplayName} 恢复参考电平到 " +
                           $"{SaReferenceLevelDbm}dBm 失败：{ex.Message}");
            }
            await Task.Delay(TraceSettleMs, ct).ConfigureAwait(false);

            return lastWritten;
        }
    }
}
