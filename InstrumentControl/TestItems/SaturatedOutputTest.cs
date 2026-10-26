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
    /// 饱和输出电平测试。
    /// <para>业务逻辑：在 P-1 测试已稳定的基础上，按 1dB 步进继续增大 RF 信号源功率，
    /// 同步读频谱仪 DELTA marker 累积值；若某次步进增量 &lt; 0.2 dB（增益压缩 ≥ 0.8 dB），
    /// 记录此时的 RF 信号源功率作为"饱和输出电平"指标值（取已饱和档）。</para>
    /// <para>判据：饱和点 RF 功率 ≤ 10 dBm 为 Pass（10 dBm 是当前硬件保护上限，
    /// 后期调宽到 18 dBm 后改 UpperLimit 常量即可）。
    /// 若触达 10 dBm 上限仍未饱和，判为测试异常 = Fail，不再继续增大功率避免烧毁限幅器。</para>
    /// <para>与 InputP1Test 风格一致（DELTA marker + 1dB 步进机制已验证可行）：
    /// 本测试复用了 DELTA marker 设置、SA 入口处理、RF/LO 时序等现成骨架，
    /// 只换判停阈值（0.2 dB vs P-1 的 0.95 dB）与取值语义（饱和档 vs 触发档）。</para>
    /// <list type="number">
    ///   <item>按"模式 → 频点"两层循环遍历；本测试只测几个代表频点（复用 <see cref="InputP1Test.DefaultTestFreqsMHzPerMode"/>）。</item>
    ///   <item>每 (mode, freq) 生成一条 <see cref="TestResult"/>（取已饱和那一档的 RF 功率）。</item>
    ///   <item>RF/LO 输出按"每频点开关"管理；异常或正常 return 时 finally 统一关。</item>
    /// </list>
    /// <para>单频点执行流程：</para>
    /// <list type="number">
    ///   <item>模式切换 + 等待稳定（与 InputP1Test 同款）</item>
    ///   <item>RF/LO SetFrequency</item>
    ///   <item>频段命令 + 等频段稳定</item>
    ///   <item>SA:参考电平 25 dBm + 关所有 marker（与 P-1 一致：容纳大步进到 10 dBm 时的 IF 峰值）</item>
    ///   <item>设起步 RF 功率 → 开 RF/LO 输出 → 等 IF 稳定 → 读 IF peak（作为 DELTA 参考点）</item>
    ///   <item>开 DELTA marker（参考点 = 上一步的 IF peak）</item>
    ///   <item>步进循环：RF 功率 +1 dB → 等 500ms → 读 delta Y → 计算单步增量 &lt; 0.2 dB 则记饱和值 = 当前 RF 功率</item>
    ///   <item>写 TestResult；频点末清理 DELTA marker、关 RF/LO 输出</item>
    /// </list>
    /// </summary>
    public class SaturatedOutputTest : ITestItem
    {
        // ============================================================
        // 常量（与 InputP1Test / GainTest 风格统一；如修改需同步两处）
        // ============================================================

        /// <summary>频谱仪参考电平（dBm）。设 25 dBm 以容纳大步进到 10 dBm 时的 IF 峰值。</summary>
        private const double SaReferenceLevelDbm = 25.0;

        /// <summary>频谱仪带宽（Hz）。50 MHz 通用，对所有 mode 都够。</summary>
        private const double SaSpanHz = 50_000_000.0;

        /// <summary>模式切换后稳定时间（ms）。与 InputP1Test / GainTest 保持一致。</summary>
        private const int ModeStableMs = 1000;

        /// <summary>频段切换后稳定时间（ms）。</summary>
        private const int BandStableMs = 500;

        /// <summary>RF/LO 频率切换 + PLL 锁相稳定（ms）。</summary>
        private const int RfLoStableMs = 200;

        /// <summary>读 IF peak 前等最大保持累积一帧（ms）。</summary>
        private const int IfStableMs = 300;

        /// <summary>步进后余量（让 SA 扫到下一帧）（ms）。</summary>
        private const int PostStepDelayMs = 500;

        /// <summary>步进后等 RF 功率切换 + 频谱仪更新（ms）。</summary>
        private const int StepStableMs = 500;

        /// <summary>RF 功率步进（dB）。按用户要求 1 dB。</summary>
        private const double PowerStepDbm = 1.0;

        /// <summary>
        /// RF 信号源最大允许输出功率（dBm）。
        /// 当前设 10 dBm 作硬件保护（防限幅器烧毁），后期业务调宽到 18 dBm 时改此常量与
        /// <see cref="UpperLimit"/> 即可同步生效。
        /// </summary>
        private const double MaxRfPowerDbm = 10.0;

        /// <summary>
        /// 单步增量阈值（dB）。低于此值判为已饱和。
        /// 0.2 dB = 压缩 ≥ 0.8 dB（步进 1 dB 实际只增 0.2 dB）。
        /// </summary>
        private const double StepIncrementThresholdDb = 0.2;

        // ============================================================
        // 模式联动参数
        // ============================================================

        /// <summary>SA 中心频率 = IF 中心：Mode1/2 = 70 MHz，Mode3 = 750 MHz。</summary>
        private const double SaCenterFreqMode12Hz = 70_000_000.0;
        private const double SaCenterFreqMode3Hz = 750_000_000.0;

        /// <summary>起步 RF 功率：Mode1/3 = -20 dBm，Mode2 = -10 dBm（与 InputP1Test / GainTest 起步功率同款）。</summary>
        private const double StartRfPowerMode13Dbm = -20.0;
        private const double StartRfPowerMode2Dbm = -10.0;

        /// <summary>LO 功率（三个模式均为 +3 dBm）。</summary>
        private const double LoPowerDbm = 3.0;

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

        public SaturatedOutputTest(
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
            // 复用 P-1 的代表频点表（避免重新维护一份）
            _testFreqsMHzPerMode = testFreqsMHzPerMode ?? InputP1Test.DefaultTestFreqsMHzPerMode;
        }

        public string TestName => "饱和输出电平";

        /// <summary>单位：dBm。报表 Value 是已饱和那一档的 RF 信号源输出功率。</summary>
        public string Unit => "dBm";

        /// <summary>判据下限（dBm），null = 不设下限。</summary>
        public double? LowerLimit => null;

        /// <summary>
        /// 判据上限（dBm）：饱和点 RF 功率 ≤ 此值判合格。
        /// 当前 = 硬件保护上限 10 dBm；后期业务放宽到 18 dBm 时同步改
        /// <see cref="MaxRfPowerDbm"/> 与本常量即可。
        /// </summary>
        public double? UpperLimit => MaxRfPowerDbm;

        // ============================================================
        // 执行入口
        // ============================================================

        public async Task<TestResult> ExecuteAsync(IProgress<string> progress, CancellationToken ct)
        {
            if (progress == null) throw new ArgumentNullException(nameof(progress));
            ct.ThrowIfCancellationRequested();

            if (!_rf.IsConnected || !_lo.IsConnected || !_sa.IsConnected)
            {
                throw new InvalidOperationException("SaturatedOutputTest 需要三台仪表（RF/LO/SA）已连接。");
            }

            // 频谱仪入口：清残留状态 + 切连续扫描
            try { _sa.Reset(); } catch { /* best-effort */ }
            _sa.SetContinuousScan(true);

            // RF/LO 输出按"每频点开/关"管理（与 P-1 同款），finally 兜底。

            TestResult lastResult = null;
            int total = 0;
            foreach (var mode in _modes)
            {
                if (_testFreqsMHzPerMode.TryGetValue(mode, out var freqs) && freqs != null)
                {
                    total += freqs.Length;
                }
            }
            int done = 0;

            try
            {
                foreach (var mode in _modes)
                {
                    ct.ThrowIfCancellationRequested();
                    progress.Report($"[SaturatedOutputTest] === 进入 {mode} ===");
                    Logger.Log($"[SaturatedOutputTest] === 进入 {mode} ===");

                    // 模式联动参数
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
                    progress.Report($"[SaturatedOutputTest] {mode} 模式切换稳定 {ModeStableMs}ms…");
                    await Task.Delay(ModeStableMs, ct).ConfigureAwait(false);

                    // 取该模式下的代表频点
                    if (!_testFreqsMHzPerMode.TryGetValue(mode, out double[] freqs) || freqs == null || freqs.Length == 0)
                    {
                        string warn = $"未配置 {mode} 的测试频点，跳过。";
                        progress.Report("[SaturatedOutputTest] " + warn);
                        Logger.Log("[SaturatedOutputTest] " + warn);
                        continue;
                    }

                    foreach (double freqMHz in freqs)
                    {
                        ct.ThrowIfCancellationRequested();
                        lastResult = await ExecuteOneFreqAsync(
                            mode, freqMHz, rfStartPowerDbm, ifFreqHz, ++done, total, progress, ct).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                // 测试完成后再关闭信号输出（异常路径兜底）
                try { _rf.SetOutputState(false); } catch { /* best-effort */ }
                try { _lo.SetOutputState(false); } catch { /* best-effort */ }
                Logger.Log("[SaturatedOutputTest] RF/LO 输出已关闭（finally）。");
            }

            return lastResult;
        }

        // ============================================================
        // 单频点饱和输出测量
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
            // 按"显示 MHz"反查 bandIndex / BandNumber
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
                progress.Report("[SaturatedOutputTest] " + err);
                Logger.Log("[SaturatedOutputTest] " + err);
                return FailResult(mode, freqMHz, double.NaN, "未找到频段", ifFreqHz, rfStartPowerDbm, 0, "0.0/0.0");
            }

            double rfFreqHz = freqMHz * 1_000_000.0;
            double loFreqHz = LoFrequencyRule.Calculate(rfFreqHz, (int)mode);

            // 设频（按"推测 9 + Q2"：RF/LO 设频在频段命令之前）
            _rf.SetFrequency(rfFreqHz);
            _lo.SetFrequency(loFreqHz);

            // 等待 RF/LO 频率切换 + PLL 稳定
            await Task.Delay(RfLoStableMs, ct).ConfigureAwait(false);

            // 频段命令
            _receiver.SendBandCommand(mode, band.BandIndex);
            await Task.Delay(BandStableMs, ct).ConfigureAwait(false);

            // 重置 SA 状态：参考电平设 25 dBm，关所有 marker（与 P-1 同款：容纳大步进到 10 dBm 时的 IF 峰值）
            _sa.SetReferenceLevel(SaReferenceLevelDbm);
            await Task.Delay(50, ct).ConfigureAwait(false);
            _sa.SetAllMarkOFF();

            // 设置起步 RF 功率
            _rf.SetPower(rfStartPowerDbm);

            // 打开 RF/LO 输出（每频点开关：先设完所有参数再开输出）
            try
            {
                _rf.SetOutputState(true);
                _lo.SetOutputState(true);
            }
            catch (Exception ex)
            {
                Logger.Log($"[SaturatedOutputTest] {mode} {band.DisplayName} 开 RF/LO 输出失败：{ex.Message}");
            }

            // 等 IF 稳定，读 IF peak（作为 DELTA 参考点）
            await Task.Delay(IfStableMs, ct).ConfigureAwait(false);
            double referencePeakDbm;
            try
            {
                referencePeakDbm = _sa.ReadPeakAmplitude();
            }
            catch (Exception ex)
            {
                string err = $"{mode} {band.DisplayName} 读 IF peak 失败：{ex.Message}";
                progress.Report("[SaturatedOutputTest] " + err);
                Logger.Log("[SaturatedOutputTest] " + err);
                return FailResult(mode, freqMHz, double.NaN, "读 IF peak 失败", ifFreqHz, rfStartPowerDbm, 0, "0.0/0.0");
            }

            // 开 DELTA marker
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
                progress.Report("[SaturatedOutputTest] " + err);
                Logger.Log("[SaturatedOutputTest] " + err);
                return FailResult(mode, freqMHz, double.NaN, "设 DELTA marker 失败", ifFreqHz, rfStartPowerDbm, referencePeakDbm, "0.0/0.0");
            }

            // 步进循环
            double currentPowerDbm = rfStartPowerDbm;
            double lastDeltaDb = 0.0;            // 上一次累积 delta
            double satResultValue = double.NaN;  // 报表 Value = 已饱和档的 RF 功率
            int stepCount = 0;
            string finalDeltaStr = "0.00";
            bool reachedUpperLimit = false;      // 触达 10 dBm 上限仍未饱和 → 测试异常

            while (true)
            {
                ct.ThrowIfCancellationRequested();
                stepCount++;

                // 步进：功率 +1 dB
                currentPowerDbm += PowerStepDbm;

                // 触顶保护：先强制夹到上限，再判停。
                //   - 夹到上限后立刻记 Value = MaxRfPowerDbm（按 Q14A：触达上限也判为未饱和测试异常）
                //   - 用 ">=" 而非 ">"，避免刚好到上限时多跑一轮
                if (currentPowerDbm >= MaxRfPowerDbm)
                {
                    currentPowerDbm = MaxRfPowerDbm;
                    satResultValue = currentPowerDbm;
                    reachedUpperLimit = true;
                    string msg = $"[SaturatedOutputTest] {mode} {band.DisplayName} 触达 {MaxRfPowerDbm}dBm 上限仍未饱和，测试异常";
                    progress.Report(msg);
                    Logger.Log(msg);
                    break;
                }

                _rf.SetPower(currentPowerDbm);
                await Task.Delay(StepStableMs, ct).ConfigureAwait(false);

                // 读 delta 累积值
                double currentDeltaDb;
                try
                {
                    string resp = _sa.QueryString(":CALC:MARK1:Y?");
                    if (!double.TryParse(resp, NumberStyles.Float, CultureInfo.InvariantCulture, out currentDeltaDb))
                    {
                        string err = $"{mode} {band.DisplayName} 读 delta Y 失败：'{resp}'";
                        progress.Report("[SaturatedOutputTest] " + err);
                        Logger.Log("[SaturatedOutputTest] " + err);
                        break;
                    }
                }
                catch (Exception ex)
                {
                    string err = $"{mode} {band.DisplayName} 读 delta Y 异常：{ex.Message}";
                    progress.Report("[SaturatedOutputTest] " + err);
                    Logger.Log("[SaturatedOutputTest] " + err);
                    break;
                }
                finalDeltaStr = currentDeltaDb.ToString("F2", CultureInfo.InvariantCulture);

                double stepIncrement = currentDeltaDb - lastDeltaDb;

                progress.Report(string.Format(
                    CultureInfo.InvariantCulture,
                    "[SaturatedOutputTest] {0} {1} ({2}/{3}) step {4}: P={5:F1}dBm, delta={6:F2}dB, Δstep={7:F2}dB",
                    mode, band.DisplayName, done, total, stepCount,
                    currentPowerDbm, currentDeltaDb, stepIncrement));
                Logger.Log(string.Format(
                    CultureInfo.InvariantCulture,
                    "[SaturatedOutputTest] {0} {1} step {2}: P={3:F1}dBm, delta={4:F2}dB, Δstep={5:F2}dB",
                    mode, band.DisplayName, stepCount,
                    currentPowerDbm, currentDeltaDb, stepIncrement));

                // 判停：单步增量 < 0.2 dB → 已饱和
                //   取值 = 当前步 RF 功率（已饱和档）
                if (stepIncrement < StepIncrementThresholdDb)
                {
                    satResultValue = currentPowerDbm;
                    break;
                }

                lastDeltaDb = currentDeltaDb;
            }

            // 判据：饱和点 RF 功率 ≤ MaxRfPowerDBm = Pass
            bool pass = !double.IsNaN(satResultValue)
                     && satResultValue <= MaxRfPowerDbm;

            // 备注：起步功率 + refPeak + 步数 + 饱和点 + finalDelta + 是否触顶
            string remarks = string.Format(
                CultureInfo.InvariantCulture,
                "RF={0:F3}MHz, IF={1:F3}MHz, startP={2}dBm, refPeak={3:F2}dBm, steps={4}, satP={5:F2}dBm, finalDelta={6}dB{7}",
                freqMHz, ifFreqHz / 1e6, rfStartPowerDbm, referencePeakDbm, stepCount,
                double.IsNaN(satResultValue) ? 0.0 : satResultValue,
                finalDeltaStr,
                reachedUpperLimit ? ", 触达10dBm上限仍未饱和（测试异常）" : "");

            TestResult result = new TestResult
            {
                TestTime = DateTime.Now,
                Channel = _channel,
                Mode = mode.ToString(),
                BandIndex = band.BandIndex,
                BandDisplayName = band.DisplayName,
                TestItem = TestName,
                Value = satResultValue,
                Unit = Unit,
                Judgment = pass ? "Pass" : "Fail",
                Remarks = remarks,
            };
            TestReportManager.Instance.Record(result);

            progress.Report(string.Format(
                CultureInfo.InvariantCulture,
                "[SaturatedOutputTest] {0} {1} ({2}/{3}): 饱和点 RF={4:F2} dBm → {5}",
                mode, band.DisplayName, done, total,
                double.IsNaN(satResultValue) ? 0.0 : satResultValue,
                result.Judgment));
            Logger.Log(string.Format(
                CultureInfo.InvariantCulture,
                "[SaturatedOutputTest] {0} {1}: 饱和点 RF={2:F2} dBm → {3}",
                mode, band.DisplayName,
                double.IsNaN(satResultValue) ? 0.0 : satResultValue,
                result.Judgment));

            // 关闭所有 marker（频点末清理 DELTA marker，避免下个频点残留）
            _sa.SetAllMarkOFF();

            // 关闭 RF/LO 输出（每频点末关：等下个频点设完所有参数后再开）
            try
            {
                _rf.SetOutputState(false);
                _lo.SetOutputState(false);
            }
            catch (Exception ex)
            {
                Logger.Log($"[SaturatedOutputTest] {mode} {band.DisplayName} 关 RF/LO 输出失败：{ex.Message}");
            }

            // 步进后余量：让 SA 扫到下一帧
            await Task.Delay(PostStepDelayMs, ct).ConfigureAwait(false);

            return result;
        }

        private TestResult FailResult(
            ReceiverMode mode, double freqMHz, double value, string reason,
            double ifFreqHz, double rfStartPowerDbm, double refPeak, string finalDelta)
        {
            return new TestResult
            {
                TestTime = DateTime.Now,
                Channel = _channel,
                Mode = mode.ToString(),
                BandIndex = -1,
                BandDisplayName = "频段" + ((int)Math.Round(freqMHz)).ToString(CultureInfo.InvariantCulture),
                TestItem = TestName,
                Value = value,
                Unit = Unit,
                Judgment = "Fail",
                Remarks = string.Format(
                    CultureInfo.InvariantCulture,
                    "RF={0:F3}MHz, IF={1:F3}MHz, startP={2}dBm, refPeak={3:F2}dBm, finalDelta={4}, error: {5}",
                    freqMHz, ifFreqHz / 1e6, rfStartPowerDbm, refPeak, finalDelta, reason),
            };
        }
    }
}