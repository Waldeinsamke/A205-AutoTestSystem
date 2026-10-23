using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using A205AutoTestSystem.Common;
using A205AutoTestSystem.ReceiverControl;

namespace A205AutoTestSystem.InstrumentControl.TestItems
{
    /// <summary>
    /// 输入 P-1（1dB 压缩点）测试。
    /// <para>业务逻辑：在线性最大增益已稳定的基础上，按 1dB 步进逐次增大 RF 信号源功率，
    /// 同步读频谱仪 DELTA marker 累积值；若某次步进增量 < 0.90 dB（增益压缩 ≥ 0.1 dB），
    /// 记录此时的 RF 信号源功率作为 P-1 指标值。
    /// （业务上 P-1 越大代表接收机线性工作区越宽，所以下限判定：Mode1/3 ≥ -10 dBm、Mode2 ≥ 0 dBm。）</para>
    /// <para>与 GainTest 风格一致：</para>
    /// <list type="bullet">
    ///   <item>按"模式 → 频点"两层循环遍历；本测试只测几个代表频点（见 <see cref="DefaultTestFreqsMHzPerMode"/>）。</item>
    ///   <item>每 (mode, freq) 生成一条 <see cref="TestResult"/>。</item>
    ///   <item>测试开始时打开 RF/LO 输出，整个循环期间保持开启；异常或正常 return 时再关闭（finally）。</item>
    /// </list>
    /// <para>单频点执行流程：</para>
    /// <list type="number">
    ///   <item>频段切换 + 等待稳定（与 GainTest 同款）</item>
    ///   <item>RF/LO SetFrequency（不动 LO/频段命令以外的命令）</item>
    ///   <item>开最大保持 + 等一帧 + 读 IF peak（作为 DELTA 参考点）</item>
    ///   <item>设 DELTA marker（参考点 = 上一步的 IF peak）</item>
    ///   <item>步进循环：RF 功率 +1 dB → 等 500ms → 读 delta Y → 计算单步增量 < 0.90 则记 P-1 = 当前 RF 功率</item>
    ///   <item>写 TestResult；复位：关最大保持</item>
    ///   <item>下个频点重新设 DELTA marker（Q5：单频点只设一次）</item>
    /// </list>
    /// </summary>
    public class InputP1Test : ITestItem
    {
        // ============================================================
        // 常量（与 GainTest / BandwidthTest 风格统一；如修改需同步两处）
        // ============================================================

        /// <summary>频谱仪参考电平（dBm）。与 GainTest 保持一致。</summary>
        private const double SaReferenceLevelDbm = 10.0;

        /// <summary>频谱仪带宽（Hz）。50 MHz 通用，对所有 mode 都够。</summary>
        private const double SaSpanHz = 50_000_000.0;

        /// <summary>模式切换后稳定时间（ms）。与 GainTest 保持一致。</summary>
        private const int ModeStableMs = 1000;

        /// <summary>频段切换后稳定时间（ms）。</summary>
        private const int BandStableMs = 500;

        /// <summary>RF/LO 频率切换 + PLL 锁相稳定（ms）。</summary>
        private const int RfLoStableMs = 200;

        /// <summary>读 IF peak 前等最大保持累积一帧（ms）。</summary>
        private const int IfStableMs = 300;         // 频点初·开 RF/LO 输出后等多久再调 ReadPeak
        private const int PostStepDelayMs = 500;    // 频点末·步进后余量（让 SA 扫到下一帧）

        /// <summary>步进后等 RF 功率切换 + 频谱仪更新（ms）。按用户要求 500ms。</summary>
        private const int StepStableMs = 500;

        /// <summary>RF 功率步进（dB）。按用户要求 1 dB。</summary>
        private const double PowerStepDbm = 1.0;

        /// <summary>RF 信号源最大允许输出功率（dBm）。按用户要求不超过 15 dBm。</summary>
        private const double MaxRfPowerDbm = 15.0;

        /// <summary>单步增量阈值（dB）。低于此值判停为 1dB 压缩点。0.90 = 压缩 ≥ 0.1 dB。</summary>
        private const double StepIncrementThresholdDb = 0.90;

        // ============================================================
        // 模式联动参数
        // ============================================================

        /// <summary>SA 中心频率 = IF 中心：Mode1/2 = 70 MHz，Mode3 = 750 MHz。</summary>
        private const double SaCenterFreqMode12Hz = 70_000_000.0;
        private const double SaCenterFreqMode3Hz = 750_000_000.0;

        /// <summary>起步 RF 功率：Mode1/3 = -20 dBm，Mode2 = -10 dBm（与 GainTest 起步功率同款）。</summary>
        private const double StartRfPowerMode13Dbm = -20.0;
        private const double StartRfPowerMode2Dbm = -10.0;

        /// <summary>LO 功率（三个模式均为 +3 dBm）。</summary>
        private const double LoPowerDbm = 3.0;

        // ============================================================
        // 判据（按 mode 分）
        // ============================================================

        /// <summary>
        /// P-1 判据下限（dBm），按 mode 划分。
        /// <para>Mode1/3: P-1 ≥ -10 dBm；Mode2: P-1 ≥ 0 dBm。</para>
        /// </summary>
        private static double? GetP1LowerLimit(ReceiverMode mode)
        {
            switch (mode)
            {
                case ReceiverMode.Mode1:
                case ReceiverMode.Mode3:
                    return -10.0;
                case ReceiverMode.Mode2:
                    return 0.0;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, "未配置该模式的 P-1 判据。");
            }
        }

        // ============================================================
        // 频点配置（默认 = 用户列出的代表频点；可通过构造参数覆盖）
        // ============================================================

        /// <summary>
        /// 默认测试频点（MHz）：Mode1=108、Mode2=1090、Mode3=900/1500/2100/3000。
        /// 来自产品手册要求。
        /// </summary>
        public static readonly IReadOnlyDictionary<ReceiverMode, double[]> DefaultTestFreqsMHzPerMode =
            new Dictionary<ReceiverMode, double[]>
            {
                { ReceiverMode.Mode1, new[] { 220.0 } },
                { ReceiverMode.Mode2, new[] { 1090.0 } },
                { ReceiverMode.Mode3, new[] { 900.0, 1500.0, 2100.0, 3000.0 } },
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

        public InputP1Test(
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

        public string TestName => "输入P-1";

        /// <summary>单位：dBm。报表 Value 是触发判停时的 RF 功率（绝对值），不是 delta 累积 dB。</summary>
        public string Unit => "dBm";

        /// <summary>判据下限（dBm），按 mode 分（见 <see cref="GetP1LowerLimit"/>）。UI 单行展示 Mode1。</summary>
        public double? LowerLimit => GetP1LowerLimit(ReceiverMode.Mode1);

        /// <summary>判据上限（dBm），null = 不设上限。P-1 越大越好，不设上限。</summary>
        public double? UpperLimit => null;

        public async Task<TestResult> ExecuteAsync(IProgress<string> progress, CancellationToken ct)
        {
            if (progress == null) throw new ArgumentNullException(nameof(progress));
            ct.ThrowIfCancellationRequested();

            if (!_rf.IsConnected || !_lo.IsConnected || !_sa.IsConnected)
            {
                throw new InvalidOperationException("InputP1Test 需要三台仪表（RF/LO/SA）已连接。");
            }

            // 频谱仪入口：清残留状态 + 切连续扫描
            try { _sa.Reset(); } catch { /* best-effort */ }
            _sa.SetContinuousScan(true);

            // RF/LO 输出改成"每频点开/关"（由 ExecuteOneFreqAsync 内部管理），此处不再统一开启。
            // finally 仍保留 :OUTP 0 兜底：异常路径下保证关闭信号源。

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
                    progress.Report($"[InputP1Test] === 进入 {mode} ===");
                    Logger.Log($"[InputP1Test] === 进入 {mode} ===");

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
                    progress.Report($"[InputP1Test] {mode} 模式切换稳定 {ModeStableMs}ms…");
                    await Task.Delay(ModeStableMs, ct).ConfigureAwait(false);

                    // 取该模式下的代表频点
                    if (!_testFreqsMHzPerMode.TryGetValue(mode, out double[] freqs) || freqs == null || freqs.Length == 0)
                    {
                        string warn = $"未配置 {mode} 的测试频点，跳过。";
                        progress.Report("[InputP1Test] " + warn);
                        Logger.Log("[InputP1Test] " + warn);
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
                // 测试完成后再关闭信号输出
                try { _rf.SetOutputState(false); } catch { /* best-effort */ }
                try { _lo.SetOutputState(false); } catch { /* best-effort */ }
                Logger.Log("[InputP1Test] RF/LO 输出已关闭（finally）。");
            }

            return lastResult;
        }

        // ============================================================
        // 单频点 P-1 测量
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
                progress.Report("[InputP1Test] " + err);
                Logger.Log("[InputP1Test] " + err);
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

            // 重置 SA 状态：设参考电平 25 dBm（按用户要求，替代原最大保持；容纳大步进到 +15 dBm 时的 IF 峰值）。
            // 关闭所有 marker（包括 DELTA），让下一段从干净状态开始
            _sa.SetReferenceLevel(25.0);
            // 等 50ms 让频谱仪处理完 RLEV 命令（保证开输出前 SA 显示顶端已生效，避免屏幕读数饱和）
            await Task.Delay(50, ct).ConfigureAwait(false);
            _sa.SetAllMarkOFF();

            // 设置起步 RF 功率
            _rf.SetPower(rfStartPowerDbm);

            // 打开 RF/LO 输出（每频点开关：先设完所有参数再开输出，避免输出和参数同步在频段切换瞬间产生毛刺）
            try
            {
                _rf.SetOutputState(true);
                _lo.SetOutputState(true);
            }
            catch (Exception ex)
            {
                Logger.Log($"[InputP1Test] {mode} {band.DisplayName} 开 RF/LO 输出失败：{ex.Message}");
            }

            // 等 IF 稳定（开输出后），读 IF peak（作为 DELTA 参考点）
            await Task.Delay(IfStableMs, ct).ConfigureAwait(false);
            double referencePeakDbm;
            try
            {
                referencePeakDbm = _sa.ReadPeakAmplitude();
            }
            catch (Exception ex)
            {
                string err = $"{mode} {band.DisplayName} 读 IF peak 失败：{ex.Message}";
                progress.Report("[InputP1Test] " + err);
                Logger.Log("[InputP1Test] " + err);
                return FailResult(mode, freqMHz, double.NaN, "读 IF peak 失败", ifFreqHz, rfStartPowerDbm, 0, "0.0/0.0");
            }

            // 重设 DELTA marker：
            // 频点初关闭了所有 marker 并切到新频段，marker 状态需要重新激活。
            // 注意：SCPI 的 STAT ON / MAX:PEAK / MAX:PEAK:SEARCH / MODE DELTA 都是 set 命令（无 '?'），
            //       必须用 SendCommand；用 QueryString 会因为"等不到响应"触发 IOTimeoutException。
            try
            {
                _sa.SendCommand(":CALC:MARK1:STAT ON");
                _sa.SendCommand(":CALC:MARK1:MAX:PEAK");
                _sa.SendCommand(":CALC:MARK1:MAX:PEAK:SEARCH");
                _sa.EnableDeltaMarker();   // :CALC:MARK1:MODE DELTA
            }
            catch (Exception ex)
            {
                string err = $"{mode} {band.DisplayName} 设 DELTA marker 失败：{ex.Message}";
                progress.Report("[InputP1Test] " + err);
                Logger.Log("[InputP1Test] " + err);
                return FailResult(mode, freqMHz, double.NaN, "设 DELTA marker 失败", ifFreqHz, rfStartPowerDbm, referencePeakDbm, "0.0/0.0");
            }

            // 步进循环
            double currentPowerDbm = rfStartPowerDbm;
            double lastDeltaDb = 0.0;        // 上一次累积 delta
            double p1ResultValue = double.NaN;   // 报表 Value = 触发判停时的 RF 功率
            int stepCount = 0;
            string finalDeltaStr = "0.00";
            // H-7 修复：触达上限立即 break（不再多跑一轮重发同值 SCPI + 多读一次 delta）。
            // 保留标志是为让 Remarks 准确输出"未压缩"。
            bool reachedUpperLimit = false;

            while (true)
            {
                ct.ThrowIfCancellationRequested();
                stepCount++;

                // 步进：功率 +1 dB
                currentPowerDbm += PowerStepDbm;

                // 达上限保护：触顶立即 break，不再继续循环（修复前会多跑一轮：
                // 重发 :POW 15 dBm、等 500ms、多读一次 :CALC:MARK1:Y?，劣化用户体验）。
                // 判据故意用 ">=" 而非 ">"，让"刚好到上限"也立即退出（不留双发）。
                if (currentPowerDbm >= MaxRfPowerDbm)
                {
                    currentPowerDbm = MaxRfPowerDbm;
                    p1ResultValue = currentPowerDbm;
                    reachedUpperLimit = true;
                    string msg = $"[InputP1Test] {mode} {band.DisplayName} 触达 {MaxRfPowerDbm}dBm 上限仍未压缩，P-1 记 {p1ResultValue:F2} dBm（未压缩）";
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
                        progress.Report("[InputP1Test] " + err);
                        Logger.Log("[InputP1Test] " + err);
                        break;
                    }
                }
                catch (Exception ex)
                {
                    string err = $"{mode} {band.DisplayName} 读 delta Y 异常：{ex.Message}";
                    progress.Report("[InputP1Test] " + err);
                    Logger.Log("[InputP1Test] " + err);
                    break;
                }
                finalDeltaStr = currentDeltaDb.ToString("F2", CultureInfo.InvariantCulture);

                double stepIncrement = currentDeltaDb - lastDeltaDb;

                progress.Report(string.Format(
                    CultureInfo.InvariantCulture,
                    "[InputP1Test] {0} {1} ({2}/{3}) step {4}: P={5:F1}dBm, delta={6:F2}dB, Δstep={7:F2}dB",
                    mode, band.DisplayName, done, total, stepCount,
                    currentPowerDbm, currentDeltaDb, stepIncrement));
                Logger.Log(string.Format(
                    CultureInfo.InvariantCulture,
                    "[InputP1Test] {0} {1} step {2}: P={3:F1}dBm, delta={4:F2}dB, Δstep={5:F2}dB",
                    mode, band.DisplayName, stepCount,
                    currentPowerDbm, currentDeltaDb, stepIncrement));

                // 判停：单步增量 < 阈值
                if (stepIncrement < StepIncrementThresholdDb)
                {
                    p1ResultValue = currentPowerDbm;
                    break;
                }

                lastDeltaDb = currentDeltaDb;
            }

            // 判据
            var lowerLimit = GetP1LowerLimit(mode);
            bool pass = !double.IsNaN(p1ResultValue)
                     && lowerLimit.HasValue
                     && p1ResultValue >= lowerLimit.Value;

            // 备注：判停时累积 delta + 是否触达 15 dBm 上限
            string remarks = string.Format(
                CultureInfo.InvariantCulture,
                "RF={0:F3}MHz, IF={1:F3}MHz, refPeak={2:F2}dBm, steps={3}, P-1={4:F2}dBm, finalDelta={5}dB{6}",
                freqMHz, ifFreqHz / 1e6, referencePeakDbm, stepCount,
                double.IsNaN(p1ResultValue) ? 0.0 : p1ResultValue,
                finalDeltaStr,
                reachedUpperLimit ? ", 触达15dBm上限未压缩" : "");

            TestResult result = new TestResult
            {
                TestTime = DateTime.Now,
                Channel = _channel,
                Mode = mode.ToString(),
                BandIndex = band.BandIndex,
                BandDisplayName = band.DisplayName,
                TestItem = TestName,
                Value = p1ResultValue,
                Unit = Unit,
                Judgment = pass ? "Pass" : "Fail",
                Remarks = remarks,
            };
            TestReportManager.Instance.Record(result);

            progress.Report(string.Format(
                CultureInfo.InvariantCulture,
                "[InputP1Test] {0} {1} ({2}/{3}): P-1={4:F2} dBm → {5}",
                mode, band.DisplayName, done, total,
                double.IsNaN(p1ResultValue) ? 0.0 : p1ResultValue,
                result.Judgment));
            Logger.Log(string.Format(
                CultureInfo.InvariantCulture,
                "[InputP1Test] {0} {1}: P-1={2:F2} dBm → {3}",
                mode, band.DisplayName,
                double.IsNaN(p1ResultValue) ? 0.0 : p1ResultValue,
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
                Logger.Log($"[InputP1Test] {mode} {band.DisplayName} 关 RF/LO 输出失败：{ex.Message}");
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
