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
    /// 中频抑制测试。
    /// <para>物理含义：测量接收机对"中频干扰信号"的抑制度，单位 dBc（数值越大越好）。
    /// 测试时把 RF 信号源切换到一中频（4350 MHz）或二中频（70 / 750 MHz），
    /// 接收机原本应该输出 IF 真信号的频率位置上，因为混频器的"非理想性"会产生
    /// 一定的中频泄漏，频谱仪 DELTA marker 测出来的差值就是抑制比。</para>
    /// <para>产品手册给的测试方法为纯手动，只能部分参考，工程实现按以下流程：</para>
    /// <list type="number">
    ///   <item>按"标准增益"流程在测试频点上先让 IF 真信号稳定：
    ///         channel/mode/频段/RF 频率/LO 频率/AGC=0，与 AgcControlTest 同款</item>
    ///   <item>激活 marker1 + 设 DELTA：IF 真信号峰值作为参考点（DELTA 显示 0 dB）</item>
    ///   <item>切 RF 信号源到一中频（或二中频）频率，LO 保持不动
    ///         （L1 决策：只改 RF，LO 不动，让 IF 输出端从"真信号"变成"中频泄漏"）</item>
    ///   <item>读 DELTA Y?，记 |delta| 为对应抑制值</item>
    ///   <item>若还需测二中频：RF 切回测试频点，重新 peak → DELTA 重设，
    ///         再切 RF 到二中频频率，重复读 |delta|</item>
    /// </list>
    /// <para>频点（用户配置）：</para>
    /// <list type="bullet">
    ///   <item>Mode1：108 MHz、400 MHz → 仅测二中频 70 MHz</item>
    ///   <item>Mode2：960 MHz、1600 MHz → 仅测二中频 70 MHz</item>
    ///   <item>Mode3：2000 MHz → 测一中频 4350 MHz + 二中频 750 MHz</item>
    /// </list>
    /// </summary>
    public class IfRejectionTest : ITestItem
    {
        // ============================================================
        // 常量（沿用 AGC / GainTest / InputP1Test 风格）
        // ============================================================

        private const double SaReferenceLevelDbm = 10.0;
        private const double SaSpanHz = 50_000_000.0;

        /// <summary>频谱仪 RBW（kHz）。中频抑制测试全程 39 kHz，理由：抑制测量需要
        /// 较窄的 RBW 以降低底噪，让低强度的中频泄漏信号能被 marker 找到。</summary>
        private const double SaRbwKHz = 39.0;

        private const int ModeStableMs = 800;
        private const int BandStableMs = 500;
        private const int RfLoStableMs = 200;

        // AGC=0 设参考点后等频谱仪 trace 收敛（沿用 AGC 同款常量）。
        private const int TraceSettleMs = 500;

        // DELTA 参考点设上之后多等一拍，让频谱仪 marker1 PEAK 收敛（沿用 AGC 同款常量）。
        private const int MarkerPostSetupDelayMs = 500;

        // 切到中频频率后稳定等待（与 AGC AgcStepStableMs 同语义）。
        private const int IfRejectStableMs = 1000;

        /// <summary>切换到一中频/二中频测量时使用的参考电平（dBm）。此时 IF 输出
        /// 端只剩"中频泄漏"，信号极弱，把屏顶压到 −20 dBm 让 marker 在更密的
        /// 单位刻度下能找到真实峰。读完 |delta| 立刻恢复为 10 dBm。</summary>
        private const double IfRejectReferenceLevelDbm = -20.0;

        private const double SaCenterFreqMode12Hz = 70_000_000.0;
        private const double SaCenterFreqMode3Hz = 750_000_000.0;
        private const double StartRfPowerMode13Dbm = -20.0;
        private const double StartRfPowerMode2Dbm = -10.0;
        private const double LoPowerDbm = 3.0;

        // 中频抑制阈值（用户给定）。
        private const double FirstIfLowerLimitDb = 40.0;
        private const double SecondIfLowerLimitDb = 70.0;

        /// <summary>一中频 4350 MHz（产品手册）。</summary>
        private const double FirstIfFreqHz = 4_350_000_000.0;

        /// <summary>二中频：Mode1/2 = 70 MHz；Mode3 = 750 MHz。</summary>
        private const double SecondIfFreqMode12Hz = 70_000_000.0;
        private const double SecondIfFreqMode3Hz = 750_000_000.0;

        // ============================================================
        // 频点配置（代表性频点，不遍历全部频段）
        // ============================================================
        public static readonly IReadOnlyDictionary<ReceiverMode, double[]> DefaultTestFreqsMHzPerMode =
            new Dictionary<ReceiverMode, double[]>
            {
                { ReceiverMode.Mode1, new[] { 108.0, 400.0 } },
                { ReceiverMode.Mode2, new[] { 960.0, 1600.0 } },
                { ReceiverMode.Mode3, new[] { 2000.0 } },
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
        public IfRejectionTest(
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

        // ============================================================
        // ITestItem
        // ============================================================
        public string TestName => "中频抑制";
        public string Unit => "dB";

        /// <summary>判据下限（dB）。UI 单行展示的占位值，
        /// 不同测量类型阈值不同（详见 GetLowerLimitByType），实际判定在 ExecuteOneFreqAsync 内按 type 取。</summary>
        public double? LowerLimit => SecondIfLowerLimitDb;
        public double? UpperLimit => null;

        private static double GetLowerLimitByType(string suppressionType)
        {
            switch (suppressionType)
            {
                case "一中频":
                    return FirstIfLowerLimitDb;
                case "二中频":
                    return SecondIfLowerLimitDb;
                default:
                    throw new ArgumentOutOfRangeException(nameof(suppressionType), suppressionType, "未知的中频抑制类型。");
            }
        }

        public async Task<TestResult> ExecuteAsync(IProgress<string> progress, CancellationToken ct)
        {
            if (progress == null) throw new ArgumentNullException(nameof(progress));
            ct.ThrowIfCancellationRequested();

            if (!_rf.IsConnected || !_lo.IsConnected || !_sa.IsConnected)
            {
                throw new InvalidOperationException("IfRejectionTest 需要三台仪表（RF/LO/SA）已连接。");
            }

            // 频谱仪入口
            try { _sa.Reset(); } catch { /* best-effort */ }
            _sa.SetContinuousScan(true);

            // 测试开始时打开 RF/LO 输出；finally 关闭
            try
            {
                _rf.SetOutputState(true);
                _lo.SetOutputState(true);
                Logger.Log("[IfRejectionTest] RF/LO 输出已开启。");
            }
            catch (Exception ex)
            {
                Logger.Log("[IfRejectionTest] RF/LO :OUTP 1 失败：" + ex.Message);
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
                    progress.Report($"[IfRejectionTest] === 进入 {mode} ===");
                    Logger.Log($"[IfRejectionTest] === 进入 {mode} ===");

                    double ifFreqHz = (mode == ReceiverMode.Mode3) ? SaCenterFreqMode3Hz : SaCenterFreqMode12Hz;
                    double saCenterHz = ifFreqHz;
                    double rfStartPowerDbm = (mode == ReceiverMode.Mode2) ? StartRfPowerMode2Dbm : StartRfPowerMode13Dbm;

                    // 模式切换：先通道后模式
                    _receiver.SendChannelThenModeCommand(_channel, mode);

                    // 频谱仪：中心 + Span + 参考电平 + RBW
                    _sa.SetCenterFrequency(saCenterHz);
                    _sa.SetSpan(SaSpanHz);
                    _sa.SetReferenceLevel(SaReferenceLevelDbm);
                    _sa.SetResolutionBandwidth(SaRbwKHz);   // 测试全程 39 kHz，降低底噪助 marker 找峰

                    // 模式切换稳定
                    progress.Report($"[IfRejectionTest] {mode} 模式切换稳定 {ModeStableMs}ms…");
                    await Task.Delay(ModeStableMs, ct).ConfigureAwait(false);

                    if (!_testFreqsMHzPerMode.TryGetValue(mode, out double[] freqs) || freqs == null || freqs.Length == 0)
                    {
                        string warn = $"未配置 {mode} 的测试频点，跳过。";
                        progress.Report("[IfRejectionTest] " + warn);
                        Logger.Log("[IfRejectionTest] " + warn);
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
                Logger.Log("[IfRejectionTest] RF/LO 输出已关闭（finally）。");
            }

            return lastResult;
        }

        // ============================================================
        // 单频点 IF 抑制（按 mode 决定测一中频 / 二中频 / 两者）
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
                progress.Report("[IfRejectionTest] " + err);
                Logger.Log("[IfRejectionTest] " + err);
                return null;
            }

            double rfFreqHz = freqMHz * 1_000_000.0;
            double loFreqHz = LoFrequencyRule.Calculate(rfFreqHz, (int)mode);

            // -------- 第 1 阶段：标准增益设置（让 IF 真信号稳定）--------

            // 设频（与 P-1 / AGC 同款：RF/LO 设频在频段命令之前）
            _rf.SetFrequency(rfFreqHz);
            _lo.SetFrequency(loFreqHz);
            await Task.Delay(RfLoStableMs, ct).ConfigureAwait(false);

            // 频段命令
            _receiver.SendBandCommand(mode, band.BandIndex);
            await Task.Delay(BandStableMs, ct).ConfigureAwait(false);

            // 重置 SA 状态：清 marker（不开 MAXH，原因同 AGC）。
            _sa.SetAllMarkOFF();

            // 起步 RF 功率
            _rf.SetPower(rfStartPowerDbm);
            await Task.Delay(RfLoStableMs, ct).ConfigureAwait(false);

            // AGC=0 设参考点
            _receiver.SendAttenuationCommand(_channel, 0);
            await Task.Delay(TraceSettleMs, ct).ConfigureAwait(false);

            // 激活 marker1 + 设 DELTA
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
                progress.Report("[IfRejectionTest] " + err);
                Logger.Log("[IfRejectionTest] " + err);
                return null;
            }

            // 等几帧让 marker1 PEAK 收敛
            await Task.Delay(MarkerPostSetupDelayMs, ct).ConfigureAwait(false);

            // 累计最近一次写结果（避免多次写时丢）
            TestResult lastWritten = null;

            // -------- 第 2 阶段：一中频抑制（仅 Mode3 / 频段2000）--------

            bool measureFirstIf = (mode == ReceiverMode.Mode3);

            if (measureFirstIf)
            {
                // RF 切到一中频 4350 MHz，LO 不动（L1 决策）。
                _rf.SetFrequency(FirstIfFreqHz);
                // IF 输出端此时只剩"中频泄漏"，信号极弱；
                // 把屏顶压到 −20 dBm 让 marker 在更密的单位刻度下能找到真峰。
                _sa.SetReferenceLevel(IfRejectReferenceLevelDbm);
                await Task.Delay(IfRejectStableMs, ct).ConfigureAwait(false);

                double firstIfDeltaDb;
                try
                {
                    string resp = _sa.QueryString(":CALC:MARK1:Y?");
                    if (!double.TryParse(resp, NumberStyles.Float, CultureInfo.InvariantCulture, out firstIfDeltaDb))
                    {
                        string err = $"{mode} {band.DisplayName} 读一中频 delta Y 失败：'{resp}'";
                        progress.Report("[IfRejectionTest] " + err);
                        Logger.Log("[IfRejectionTest] " + err);
                        firstIfDeltaDb = double.NaN;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"[IfRejectionTest] {mode} {band.DisplayName} 读一中频 delta Y 异常：{ex.Message}");
                    firstIfDeltaDb = double.NaN;
                }

                double firstIfAbsDb = double.IsNaN(firstIfDeltaDb) ? double.NaN : Math.Abs(firstIfDeltaDb);
                double firstIfLimit = GetLowerLimitByType("一中频");
                bool firstIfPass = !double.IsNaN(firstIfAbsDb) && firstIfAbsDb >= firstIfLimit;
                string firstIfRemarks = string.Format(
                    CultureInfo.InvariantCulture,
                    "RF={0:F3}MHz → 一中频 {1:F0}MHz, IF={2:F0}MHz, delta={3:F2}dB, |抑制|={4:F2}dB (≥{5}dB{6})",
                    freqMHz, FirstIfFreqHz / 1e6, ifFreqHz / 1e6,
                    firstIfDeltaDb, firstIfAbsDb,
                    firstIfLimit, firstIfPass ? "合格" : "不合格");

                TestResult firstIfResult = new TestResult
                {
                    TestTime = DateTime.Now,
                    Channel = _channel,
                    Mode = mode.ToString(),
                    BandIndex = band.BandIndex,
                    BandDisplayName = band.DisplayName,
                    TestItem = "一中频抑制",
                    Value = double.IsNaN(firstIfAbsDb) ? double.NaN : firstIfAbsDb,
                    Unit = Unit,
                    Judgment = double.IsNaN(firstIfAbsDb) ? "Fail" : (firstIfPass ? "Pass" : "Fail"),
                    Remarks = firstIfRemarks,
                };
                TestReportManager.Instance.Record(firstIfResult);
                lastWritten = firstIfResult;

                progress.Report(string.Format(
                    CultureInfo.InvariantCulture,
                    "[IfRejectionTest] {0} {1} ({2}/{3}) 一中频抑制: RF→{4:F0}MHz, |抑制|={5:F2}dB (≥{6}dB) → {7}",
                    mode, band.DisplayName, done, total,
                    FirstIfFreqHz / 1e6, firstIfAbsDb, firstIfLimit, firstIfResult.Judgment));
                Logger.Log(string.Format(
                    CultureInfo.InvariantCulture,
                    "[IfRejectionTest] {0} {1} 一中频抑制: RF→{2:F0}MHz, |抑制|={3:F2}dB → {4}",
                    mode, band.DisplayName,
                    FirstIfFreqHz / 1e6, firstIfAbsDb, firstIfResult.Judgment));

                // 读完一中频 |delta| 后立刻把参考电平恢复到默认 10 dBm，
                // 避免下一阶段（恢复真信号 + 重设 DELTA）顶到屏顶导致信号被裁。
                _sa.SetReferenceLevel(SaReferenceLevelDbm);

                // -------- 第 3 阶段：恢复真实信号 → 重新设 DELTA → 测二中频 --------

                // 切 RF 回测试频点（LO 保持不动）
                _rf.SetFrequency(rfFreqHz);
                await Task.Delay(IfRejectStableMs, ct).ConfigureAwait(false);

                // 重新启 marker1 → MAX:PEAK → DELTA（让参考点重置到当前测试频点的真信号峰）
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
                    string err = $"{mode} {band.DisplayName} 重新设 DELTA marker 失败：{ex.Message}";
                    progress.Report("[IfRejectionTest] " + err);
                    Logger.Log("[IfRejectionTest] " + err);
                }
                await Task.Delay(MarkerPostSetupDelayMs, ct).ConfigureAwait(false);
            }

            // -------- 第 4 阶段：二中频抑制（所有 mode 都测）--------

            double secondIfFreqHz = (mode == ReceiverMode.Mode3) ? SecondIfFreqMode3Hz : SecondIfFreqMode12Hz;

            // RF 切到二中频频率，LO 不动。
            _rf.SetFrequency(secondIfFreqHz);
            // IF 输出端此时只剩"中频泄漏"，信号极弱；
            // 把屏顶压到 −20 dBm 让 marker 在更密的单位刻度下能找到真峰。
            _sa.SetReferenceLevel(IfRejectReferenceLevelDbm);
            await Task.Delay(IfRejectStableMs, ct).ConfigureAwait(false);

            double secondIfDeltaDb;
            try
            {
                string resp = _sa.QueryString(":CALC:MARK1:Y?");
                if (!double.TryParse(resp, NumberStyles.Float, CultureInfo.InvariantCulture, out secondIfDeltaDb))
                {
                    string err = $"{mode} {band.DisplayName} 读二中频 delta Y 失败：'{resp}'";
                    progress.Report("[IfRejectionTest] " + err);
                    Logger.Log("[IfRejectionTest] " + err);
                    secondIfDeltaDb = double.NaN;
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[IfRejectionTest] {mode} {band.DisplayName} 读二中频 delta Y 异常：{ex.Message}");
                secondIfDeltaDb = double.NaN;
            }

            double secondIfAbsDb = double.IsNaN(secondIfDeltaDb) ? double.NaN : Math.Abs(secondIfDeltaDb);
            double secondIfLimit = GetLowerLimitByType("二中频");
            bool secondIfPass = !double.IsNaN(secondIfAbsDb) && secondIfAbsDb >= secondIfLimit;
            string secondIfRemarks = string.Format(
                CultureInfo.InvariantCulture,
                "RF={0:F3}MHz → 二中频 {1:F0}MHz, IF={2:F0}MHz, delta={3:F2}dB, |抑制|={4:F2}dB (≥{5}dB{6})",
                freqMHz, secondIfFreqHz / 1e6, ifFreqHz / 1e6,
                secondIfDeltaDb, secondIfAbsDb,
                secondIfLimit, secondIfPass ? "合格" : "不合格");

            TestResult secondIfResult = new TestResult
            {
                TestTime = DateTime.Now,
                Channel = _channel,
                Mode = mode.ToString(),
                BandIndex = band.BandIndex,
                BandDisplayName = band.DisplayName,
                TestItem = "二中频抑制",
                Value = double.IsNaN(secondIfAbsDb) ? double.NaN : secondIfAbsDb,
                Unit = Unit,
                Judgment = double.IsNaN(secondIfAbsDb) ? "Fail" : (secondIfPass ? "Pass" : "Fail"),
                Remarks = secondIfRemarks,
            };
            TestReportManager.Instance.Record(secondIfResult);
            lastWritten = secondIfResult;

            progress.Report(string.Format(
                CultureInfo.InvariantCulture,
                "[IfRejectionTest] {0} {1} ({2}/{3}) 二中频抑制: RF→{4:F0}MHz, |抑制|={5:F2}dB (≥{6}dB) → {7}",
                mode, band.DisplayName, done, total,
                secondIfFreqHz / 1e6, secondIfAbsDb, secondIfLimit, secondIfResult.Judgment));
            Logger.Log(string.Format(
                CultureInfo.InvariantCulture,
                "[IfRejectionTest] {0} {1} 二中频抑制: RF→{2:F0}MHz, |抑制|={3:F2}dB → {4}",
                mode, band.DisplayName,
                secondIfFreqHz / 1e6, secondIfAbsDb, secondIfResult.Judgment));

            // 读完二中频 |delta| 后立刻把参考电平恢复到默认 10 dBm，
            // 避免下一频点（其他 mode / 其他频点）参考电平卡在 −20 dBm。
            _sa.SetReferenceLevel(SaReferenceLevelDbm);

            // 复位：清 marker（不再切 MAXH）。不再"切回测试频点"——
            // 下一频点入口会按新频点直接覆盖 RF 频率，跳过这一步省一段
            // （包括 RfLoStableMs 等稳定时间）；同时也对 Mode3 二中频 750 MHz
            // 切回 2000 MHz 这条冗余路径做了清理（如果当前频点已是同 mode
            // 最后一个，下一频点入口的 SetFrequency 会自然覆盖）。
            _sa.SetAllMarkOFF();

            return lastWritten;
        }
    }
}
