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
    /// 谐波抑制测试。
    /// <para>物理含义：测量接收机 Mode3 对其自身产生谐波的抑制度，
    /// 单位 dB（数值越大越好；按用户决策：实际从频谱仪读到的是负值
    /// ——这是谐波相对于基波的 dBc，本身就该 ≤ −70 dBc 合格——
    /// 因此报表 Value 字段统一写入 <c>Math.Abs(y)</c>，Pass 按绝对值 ≥ 70 dB 判定，
    /// 业务上等价于原值 ≤ −70 dB）。</para>
    /// <para>测试方法（按用户决策）：</para>
    /// <list type="bullet">
    ///   <item>仅测 Mode3，代表频点 4 个：900 / 1500 / 2000 / 3000 MHz。</item>
    ///   <item>RF 信号源按"线性最大增益"的方式输出（Mode3 RF=−20 dBm，LO=+3 dBm），
    ///         让 Mode3 信号正常输出，确保基波 IF 信号稳定。</item>
    ///   <item>发送频点切换命令（<see cref="ReceiverSerialPort.SendBandCommand"/>），
    ///         切到目标频段。</item>
    ///   <item>让频谱仪进入谐波测量模式（<see cref="SpectrumAnalyzer.InitializeHarmonicsTest"/>
    ///         = <c>:INITiate:HARMonics</c>），等 SA 内部把所有谐波幅度算完。</item>
    ///   <item>读 <c>:FETCh:HARMonics:AMPLitude:ALL?</c>（逗号分隔：基波、二次谐波、三次谐波……），
    ///         程序只采纳第 2 个值（对应"标记点 2"），其余丢弃。</item>
    /// </list>
    /// <para>阈值（用户给定）：Mode3 ≥ 70 dB。</para>
    /// </summary>
    public class HarmonicRejectionTest : ITestItem
    {
        // ============================================================
        // 常量（与 GainTest / OutOfBandRejectionTest / IfRejectionTest 风格统一；
        //      如修改需同步 ReceiverControlPanel 等地方）
        // ============================================================

        private const double SaReferenceLevelDbm = 10.0;
        private const double SaSpanHz = 50_000_000.0;
        private const double SaCenterFreqMode3Hz = 750_000_000.0;
        private const double StartRfPowerMode3Dbm = -20.0;
        private const double LoPowerDbm = 3.0;

        private const int ModeStableMs = 1500;
        private const int BandStableMs = 500;
        private const int RfLoStableMs = 200;

        /// <summary>
        /// 谐波稳定时间（ms）。
        /// <para>从发出 <c>:INITiate:HARMonics</c> 到频谱仪把所有谐波幅度
        /// 收集好、<c>:FETCh:HARMonics:AMPLitude:ALL?</c> 能拿到稳定结果的等待，
        /// 按用户决策取 3s；个别频谱仪冷启动时还需要更久，按需调大即可。</para>
        /// </summary>
        private const int HarmonicStableMs = 3000;

        /// <summary>判据下限（dB），按用户给定。Pass 条件：abs(value) ≥ 此值。</summary>
        private const double Mode3LowerLimitDb = 70.0;

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
        public HarmonicRejectionTest(
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
            // 强制 Mode3：按用户要求"谐波抑制仅需要测试模式3"
            _modes = new ReceiverMode[] { ReceiverMode.Mode3 };
            _testFreqsMHzPerMode = testFreqsMHzPerMode ?? DefaultTestFreqsMHzPerMode;
        }

        public string TestName => "谐波抑制";
        public string Unit => "dB";

        /// <summary>判据下限（dB），Pass 条件 abs(value) ≥ 70.0。</summary>
        public double? LowerLimit => Mode3LowerLimitDb;
        public double? UpperLimit => null;

        public async Task<TestResult> ExecuteAsync(IProgress<string> progress, CancellationToken ct)
        {
            if (progress == null) throw new ArgumentNullException(nameof(progress));
            ct.ThrowIfCancellationRequested();

            if (!_rf.IsConnected || !_lo.IsConnected || !_sa.IsConnected)
            {
                throw new InvalidOperationException("HarmonicRejectionTest 需要三台仪表（RF/LO/SA）已连接。");
            }

            // 温度切换命令：测试流程发往串口的第一条命令。
            // 必须在任何通道 / 模式 / 频段命令之前——温度档位先落到硬件上，后续命令才有意义。
            TemperatureContext.SendTo(_receiver, TestName, progress);

            // 频谱仪入口：清残留状态 + 切连续扫描
            try { _sa.Reset(); } catch { /* best-effort */ }
            _sa.SetContinuousScan(true);

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
                    progress.Report($"[HarmonicRejectionTest] === 进入 {mode} ===");
                    Logger.Log($"[HarmonicRejectionTest] === 进入 {mode} ===");

                    double ifFreqHz = SaCenterFreqMode3Hz;
                    double saCenterHz = ifFreqHz;
                    double rfStartPowerDbm = StartRfPowerMode3Dbm;

                    // 模式切换：先通道后模式（与 GainTest 同款）
                    _receiver.SendChannelThenModeCommand(_channel, mode);

                    // 频谱仪：中心 + Span + 参考电平
                    _sa.SetCenterFrequency(saCenterHz);
                    _sa.SetSpan(SaSpanHz);
                    _sa.SetReferenceLevel(SaReferenceLevelDbm);

                    // 模式切换稳定
                    progress.Report($"[HarmonicRejectionTest] {mode} 模式切换稳定 {ModeStableMs}ms…");
                    await Task.Delay(ModeStableMs, ct).ConfigureAwait(false);

                    if (!_testFreqsMHzPerMode.TryGetValue(mode, out double[] freqs) || freqs == null || freqs.Length == 0)
                    {
                        string warn = $"未配置 {mode} 的测试频点，跳过。";
                        progress.Report("[HarmonicRejectionTest] " + warn);
                        Logger.Log("[HarmonicRejectionTest] " + warn);
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
                // 兜底：关 RF/LO 输出；关闭所有 marker；不再依赖 finally 退谐波测量模式
                // （频谱仪后续被下一个测试项 Reset 时会自动退出）。
                try { _rf.SetOutputState(false); } catch { /* best-effort */ }
                try { _lo.SetOutputState(false); } catch { /* best-effort */ }
                Logger.Log("[HarmonicRejectionTest] RF/LO 输出已关闭（finally）。");
            }

            return lastResult;
        }

        // ============================================================
        // 单频点谐波抑制
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
            // 反查 BandInfo（从显示名 "频段XXXX" 反查 bandIndex）
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
                progress.Report("[HarmonicRejectionTest] " + err);
                Logger.Log("[HarmonicRejectionTest] " + err);
                return null;
            }

            double rfFreqHz = freqMHz * 1_000_000.0;
            double loFreqHz = LoFrequencyRule.Calculate(rfFreqHz, (int)mode);

            // -------- "线性最大增益"同款设置：让 Mode3 IF 真信号稳定 --------

            // 设频（按"推测 9 + Q2"：RF/LO 设频在频段命令之前，避免读到上一频段尾迹）
            _rf.SetFrequency(rfFreqHz);
            _lo.SetFrequency(loFreqHz);
            await Task.Delay(RfLoStableMs, ct).ConfigureAwait(false);

            // 频段命令（用户特别强调：记得发送频点切换的控制命令）
            _receiver.SendBandCommand(mode, band.BandIndex);
            await Task.Delay(BandStableMs, ct).ConfigureAwait(false);

            // 设置起步 RF 功率
            _rf.SetPower(rfStartPowerDbm);
            _lo.SetPower(LoPowerDbm);
            await Task.Delay(RfLoStableMs, ct).ConfigureAwait(false);

            // 打开 RF/LO 输出（每频点开关：先设完所有参数再开输出）
            try
            {
                _rf.SetOutputState(true);
                _lo.SetOutputState(true);
            }
            catch (Exception ex)
            {
                Logger.Log($"[HarmonicRejectionTest] {mode} {band.DisplayName} 开 RF/LO 输出失败：{ex.Message}");
            }

            // 让基波 IF 信号建立稳定后再让 SA 找谐波（与中频抑制 IfRejectStableMs 同语义）
            await Task.Delay(BandStableMs, ct).ConfigureAwait(false);

            // -------- 进入 SA 谐波测量模式，等所有谐波幅度算完 --------

            try
            {
                _sa.InitializeHarmonicsTest();  // :INITiate:HARMonics
            }
            catch (Exception ex)
            {
                string err = $"{mode} {band.DisplayName} 进入谐波测量模式失败：{ex.Message}";
                progress.Report("[HarmonicRejectionTest] " + err);
                Logger.Log("[HarmonicRejectionTest] " + err);
                return FailResult(mode, freqMHz, double.NaN, "进入谐波测量模式失败", ifFreqHz, rfStartPowerDbm);
            }

            // 等谐波测量稳态（SA 内部算出全部谐波幅度，FETCh 一调用就能拿到稳定结果）
            progress.Report($"[HarmonicRejectionTest] {mode} {band.DisplayName} 等谐波稳定 {HarmonicStableMs}ms…");
            await Task.Delay(HarmonicStableMs, ct).ConfigureAwait(false);

            // 读所有谐波幅度，按逗号拆分，采纳第 2 个（"标记点 2"）作为本频点的实测值。
            //   :FETCh:HARMonics:AMPLitude:ALL? 返回形如 "-3.45,-72.32,-85.10,..."（基波、二次谐波、三次谐波……）
            //   索引 0 = 基波；索引 1 = 二次谐波 = marker2 = 本测试采纳项；其余按需扩展。
            double rawAllResp = double.NaN;     // 第 1 项（基波），备注里方便定位问题
            double mark2YDbm = double.NaN;      // 第 2 项 = marker2 实测值
            string rawResp = null;
            try
            {
                rawResp = _sa.FetchHarmonicsAmplitude();  // :FETCh:HARMonics:AMPLitude:ALL?
                if (string.IsNullOrWhiteSpace(rawResp))
                {
                    string err = $"{mode} {band.DisplayName} 读谐波幅度返回空响应";
                    progress.Report("[HarmonicRejectionTest] " + err);
                    Logger.Log("[HarmonicRejectionTest] " + err);
                }
                else
                {
                    var parts = rawResp.Split(new[] { ',', ';', ' ', '\t', '\r', '\n' },
                        StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2)
                    {
                        string err = $"{mode} {band.DisplayName} 谐波幅度响应条目不足 2 项：'{rawResp}'";
                        progress.Report("[HarmonicRejectionTest] " + err);
                        Logger.Log("[HarmonicRejectionTest] " + err);
                    }
                    else
                    {
                        // 第 1 项 = 基波；第 2 项 = marker2（采纳）
                        double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out rawAllResp);
                        double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out mark2YDbm);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[HarmonicRejectionTest] {mode} {band.DisplayName} 读谐波幅度异常：{ex.Message}");
            }

            double absValue = double.IsNaN(mark2YDbm) ? double.NaN : Math.Abs(mark2YDbm);

            // 判据：abs(value) ≥ 70 dB → Pass
            //   与"读到的是负值，要求绝对值 ≥ 70"语义一致：
            //     marker2 实测 -72.3 → absValue = 72.3 ≥ 70 → Pass
            bool pass = !double.IsNaN(absValue) && absValue >= Mode3LowerLimitDb;

            string remarks = string.Format(
                CultureInfo.InvariantCulture,
                "RF={0:F1}MHz@{1:F1}dBm, LO={2:F1}MHz, IF={3:F1}MHz, all='{4}', 基波={5:F1}dBc, marker2={6:F1}dBc, |抑制|={7:F1}dB (≥{8:F1}dB{9})",
                freqMHz, rfStartPowerDbm,
                loFreqHz / 1e6,
                ifFreqHz / 1e6,
                rawResp ?? "",
                rawAllResp,
                mark2YDbm,
                double.IsNaN(absValue) ? 0.0 : absValue,
                Mode3LowerLimitDb, pass ? "合格" : "不合格");

            TestResult result = new TestResult
            {
                TestTime = DateTime.Now,
                Channel = _channel,
                Mode = mode.ToString(),
                BandIndex = band.BandIndex,
                BandDisplayName = band.DisplayName,
                TestItem = TestName,
                Value = double.IsNaN(absValue) ? double.NaN : absValue,   // 报表写入绝对值（用户决策）
                Unit = Unit,
                Judgment = double.IsNaN(absValue) ? "Fail" : (pass ? "Pass" : "Fail"),
                Remarks = remarks,
            };
            TestReportManager.Instance.Record(result);

            progress.Report(string.Format(
                CultureInfo.InvariantCulture,
                "[HarmonicRejectionTest] {0} {1} ({2}/{3}): marker2={4:F1}dBc, |抑制|={5:F1}dB (≥{6:F1}dB) → {7}",
                mode, band.DisplayName, done, total,
                mark2YDbm, double.IsNaN(absValue) ? 0.0 : absValue,
                Mode3LowerLimitDb, result.Judgment));
            Logger.Log(string.Format(
                CultureInfo.InvariantCulture,
                "[HarmonicRejectionTest] {0} {1}: all='{2}', 基波={3:F1}dBc, marker2={4:F1}dBc, |抑制|={5:F1}dB → {6}",
                mode, band.DisplayName,
                rawResp ?? "",
                rawAllResp, mark2YDbm, double.IsNaN(absValue) ? 0.0 : absValue, result.Judgment));

            // 关闭所有 marker（频点末清理，避免下个频点残留 marker2）
            _sa.SetAllMarkOFF();

            // 关闭 RF/LO 输出（每频点末关：等下个频点设完所有参数后再开）
            try
            {
                _rf.SetOutputState(false);
                _lo.SetOutputState(false);
            }
            catch (Exception ex)
            {
                Logger.Log($"[HarmonicRejectionTest] {mode} {band.DisplayName} 关 RF/LO 输出失败：{ex.Message}");
            }

            // 频点末余量：让 SA 切下一频点的状态、PLL 锁定等。
            await Task.Delay(BandStableMs, ct).ConfigureAwait(false);

            return result;
        }

        private TestResult FailResult(
            ReceiverMode mode, double freqMHz, double value, string reason,
            double ifFreqHz, double rfStartPowerDbm)
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
                    "RF={0:F1}MHz, IF={1:F1}MHz, startP={2:F1}dBm, error: {3}",
                    freqMHz, ifFreqHz / 1e6, rfStartPowerDbm, reason),
            };
        }
    }
}
