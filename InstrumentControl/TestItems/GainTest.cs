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
    /// 线性最大增益测试。
    /// <para>M6 阶段：单点测试（单模式单频点）。</para>
    /// <para>M6.x 阶段：按"模式 → 频点"两层循环遍历，每(模式,频点)生成一条
    /// <see cref="TestResult"/> 通过 <see cref="TestReportManager"/> 落盘。
    /// ExecuteAsync 返回最后一次循环的结果（用于 UI 状态展示）。</para>
    /// <para>流程（M6.x）：</para>
    /// <list type="number">
    ///   <item>发送工作模式命令序列（receiver.SendModeCommand）</item>
    ///   <item>按模式联动设置频谱仪中心/Span + RF/LO 功率</item>
    ///   <item>遍历 ModeBandTable.GetBands(mode) 的每个频点：
    ///     <list type="bullet">
    ///       <item>发送频段命令（receiver.SendBandCommand）</item>
    ///       <item>设置 RF/LO 频率（按频段显示名解析 MHz）</item>
    ///       <item>RF/LO :OUTP 1 → 等待 settleDelayMs → 读 peakDbm</item>
    ///       <item>计算 Gain = peakDbm - rfPowerDbm，判定 Pass/Fail</item>
    ///       <item>TestReportManager.Record(一条 TestResult)</item>
    ///     </list>
    ///   </item>
    ///   <item>该模式结束后 RF/LO :OUTP 0（避免下一模式首频点读到上模式尾迹）</item>
    /// </list>
    /// </summary>
    public class GainTest : ITestItem
    {
        // 模式联动频谱仪参数（与 ReceiverControlPanel 保持一致；如修改需同步两处）
        private const double SaSpanModeHz = 50_000_000.0;
        private const double SaCenterFreqMode12Hz = 70_000_000.0;
        private const double SaCenterFreqMode3Hz = 750_000_000.0;

        // 模式联动信号源功率
        private const double RfPowerMode13Dbm = -20.0;
        private const double RfPowerMode2Dbm = -10.0;
        private const double LoPowerModeDbm = 3.0;

        // 线性最大增益判据（按模式）：
        //   Mode1: 20 ± 2   → [18.0, 22.0]
        //   Mode2: 10 ± 1   → [ 9.0, 11.0]
        //   Mode3: 20 ± 1.5 → [18.5, 21.5]
        // UI 单行"线性最大增益"的阈值列展示 Mode1；运行中按当前模式各自判定。
        private const double Mode1Center = 20.0, Mode1Tol = 2.0;   // [18.0, 22.0]
        private const double Mode2Center = 10.0, Mode2Tol = 1.0;   // [ 9.0, 11.0]
        private const double Mode3Center = 20.0, Mode3Tol = 1.5;   // [18.5, 21.5]

        /// <summary>取某模式下增益的判据上下限（dB）。不在表内的模式抛异常。</summary>
        private static (double Lower, double Upper) GetGainLimits(ReceiverMode mode)
        {
            switch (mode)
            {
                case ReceiverMode.Mode1: return (Mode1Center - Mode1Tol, Mode1Center + Mode1Tol);
                case ReceiverMode.Mode2: return (Mode2Center - Mode2Tol, Mode2Center + Mode2Tol);
                case ReceiverMode.Mode3: return (Mode3Center - Mode3Tol, Mode3Center + Mode3Tol);
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, "未配置该模式的增益判据。");
            }
        }

        // 兼容路径（M6 单点模式）字段
        private readonly SignalGenerator _rfSingle;
        private readonly SignalGeneratorLO _loSingle;
        private readonly SpectrumAnalyzer _saSingle;
        private readonly double _rfFreqHzSingle;
        private readonly double _rfPowerDbmSingle;
        private readonly double _loFreqHzSingle;
        private readonly double _loPowerDbmSingle;
        private readonly double _ifFreqHzSingle;
        private readonly int _settleDelayMsSingle;
        private readonly ReceiverMode _singleMode;   // M6 单点路径使用的模式（用于取 GainLimits）

        // M6.x 路径（receiver + 模式×频点循环）字段
        private readonly ReceiverSerialPort _receiver;
        private readonly SignalGenerator _rfLoop;
        private readonly SignalGeneratorLO _loLoop;
        private readonly SpectrumAnalyzer _saLoop;
        private readonly int _channel;
        // 注意：ifFreqHz / rfPowerDbm / loPowerDbm 不再作为字段保存——
        // 它们由当前 mode 决定（Mode1/2 IF=70MHz+RF=-20dBm；Mode2 RF=-10dBm；
        // Mode3 IF=750MHz+RF=-20dBm；LO 三个模式均 3dBm）。
        // 在 ExecuteModeBandSweepAsync 内按 mode 实时取，避免整组循环用错参数。
        private readonly IReadOnlyList<ReceiverMode> _modes;
        private readonly int _settleDelayMsLoop;

        // 兼容标志
        private readonly bool _isLegacySinglePoint;

        /// <summary>等待信号稳定的延时（毫秒）。M6 阶段固定 200ms。</summary>
        private const int DefaultSettleDelayMs = 200;

        // M6.x 时序常量（每频点总时长 ~1s；模式切换 2s；读 peak 后 200ms 余量给 SA 扫下一帧）
        private const int RfLoStableMs = 200;        // RF/LO 频率切换 + PLL 锁相稳定
        private const int BandStableMs = 500;        // 频段命令后等 0.5s 再读 peak（推测 9 + Q2）
        private const int ModeStableMs = 2000;       // 模式切换后稳定（按 M6.x 用户要求 2s）
        private const int PostReadDelayMs = 200;     // 读 peak 后等频谱仪扫到下一帧（Q5=b）

        /// <summary>频谱仪参考电平（dBm）。推测 2：峰 -10 dBm 占满 0-10 dBm 量程。</summary>
        private const double SaReferenceLevelDbm = 10.0;

        // ============================================================
        // 构造（M6 兼容）
        // ============================================================

        /// <summary>
        /// M6 阶段单点测试构造（兼容保留）。
        /// <para>不接 receiver、不循环，按 M6 旧语义只跑一次。</para>
        /// </summary>
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

            _rfSingle = rfGenerator;
            _loSingle = loGenerator;
            _saSingle = spectrumAnalyzer;
            _rfFreqHzSingle = rfFreqHz;
            _rfPowerDbmSingle = rfPowerDbm;
            _loFreqHzSingle = loFreqHz;
            _loPowerDbmSingle = loPowerDbm;
            _ifFreqHzSingle = ifFreqHz;
            _settleDelayMsSingle = DefaultSettleDelayMs;
            _singleMode = ReceiverMode.Mode1;   // 显式未传模式，按 Mode1 取判据（兼容旧调用）
            _isLegacySinglePoint = true;
        }

        /// <summary>
        /// M6 阶段单点测试构造重载（mode → LO 自动计算）。
        /// </summary>
        public GainTest(
            SignalGenerator rfGenerator,
            SignalGeneratorLO loGenerator,
            SpectrumAnalyzer spectrumAnalyzer,
            double rfFreqHz,
            double rfPowerDbm,
            int mode,
            double loPowerDbm,
            double ifFreqHz)
        {
            if (rfGenerator == null) throw new ArgumentNullException(nameof(rfGenerator));
            if (loGenerator == null) throw new ArgumentNullException(nameof(loGenerator));
            if (spectrumAnalyzer == null) throw new ArgumentNullException(nameof(spectrumAnalyzer));
            if (rfFreqHz <= 0) throw new ArgumentOutOfRangeException(nameof(rfFreqHz));
            if (ifFreqHz <= 0) throw new ArgumentOutOfRangeException(nameof(ifFreqHz));

            _rfSingle = rfGenerator;
            _loSingle = loGenerator;
            _saSingle = spectrumAnalyzer;
            _rfFreqHzSingle = rfFreqHz;
            _rfPowerDbmSingle = rfPowerDbm;
            _loFreqHzSingle = LoFrequencyRule.Calculate(rfFreqHz, mode);
            _loPowerDbmSingle = loPowerDbm;
            _ifFreqHzSingle = ifFreqHz;
            _settleDelayMsSingle = DefaultSettleDelayMs;
            _singleMode = (ReceiverMode)mode;
            _isLegacySinglePoint = true;
        }

        // ============================================================
        // 构造（M6.x 全功能）
        // ============================================================

        /// <summary>
        /// M6.x 全功能构造：按"模式 → 频点"两层循环扫测。
        /// <para>中频、RF 功率、LO 功率均由当前 mode 决定（见类顶部常量），不再作为参数传入。</para>
        /// </summary>
        /// <param name="receiver">接收机串口（必须已 Open）。</param>
        /// <param name="rfGenerator">射频信号源（必须已 Connect）。</param>
        /// <param name="loGenerator">本振信号源（必须已 Connect）。</param>
        /// <param name="spectrumAnalyzer">频谱仪（必须已 Connect）。</param>
        /// <param name="channel">通道号；当前实现阶段固定传 1（留扩展点）。</param>
        /// <param name="modes">要遍历的工作模式列表；默认 [Mode1, Mode2, Mode3]。</param>
        /// <param name="settleDelayMs">每个频点的稳定等待时间（毫秒）；默认 200ms。</param>
        public GainTest(
            ReceiverSerialPort receiver,
            SignalGenerator rfGenerator,
            SignalGeneratorLO loGenerator,
            SpectrumAnalyzer spectrumAnalyzer,
            int channel,
            IReadOnlyList<ReceiverMode> modes = null,
            int settleDelayMs = DefaultSettleDelayMs)
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
            _rfLoop = rfGenerator;
            _loLoop = loGenerator;
            _saLoop = spectrumAnalyzer;
            _channel = channel;
            _modes = modes;
            _settleDelayMsLoop = settleDelayMs;
            _isLegacySinglePoint = false;
        }

        public string TestName => "线性最大增益";
        public string Unit => "dB";

        // UI 单行展示用：固定返回 Mode1 的判据区间。
        // 真实判定在 ExecuteSinglePointAsync / ExecuteModeBandSweepAsync 中按当前 mode 查询。
        private static readonly (double Lower, double Upper) DisplayLimits = GetGainLimits(ReceiverMode.Mode1);

        /// <summary>判据下限（dB），展示 Mode1 区间。null 表示不设下限。</summary>
        public double? LowerLimit => DisplayLimits.Lower;

        /// <summary>判据上限（dB），展示 Mode1 区间。null 表示不设上限。</summary>
        public double? UpperLimit => DisplayLimits.Upper;

        public async Task<TestResult> ExecuteAsync(IProgress<string> progress, CancellationToken ct)
        {
            if (progress == null) throw new ArgumentNullException(nameof(progress));
            ct.ThrowIfCancellationRequested();

            if (_isLegacySinglePoint)
            {
                return await ExecuteSinglePointAsync(progress, ct).ConfigureAwait(false);
            }
            return await ExecuteModeBandSweepAsync(progress, ct).ConfigureAwait(false);
        }

        // ============================================================
        // M6 兼容：单点
        // ============================================================

        private async Task<TestResult> ExecuteSinglePointAsync(IProgress<string> progress, CancellationToken ct)
        {
            progress.Report("[GainTest] 配置 RF 信号源…");
            Logger.Log("[GainTest] 配置 RF 信号源…");
            _rfSingle.SetFrequency(_rfFreqHzSingle);
            _rfSingle.SetPower(_rfPowerDbmSingle);
            _rfSingle.SetOutputState(true);

            progress.Report("[GainTest] 配置 LO 信号源…");
            Logger.Log("[GainTest] 配置 LO 信号源…");
            _loSingle.SetFrequency(_loFreqHzSingle);
            _loSingle.SetPower(_loPowerDbmSingle);
            _loSingle.SetOutputState(true);

            progress.Report("[GainTest] 配置频谱仪…");
            Logger.Log("[GainTest] 配置频谱仪…");
            _saSingle.SetCenterFrequency(_ifFreqHzSingle);
            _saSingle.SetSpan(10_000);
            _saSingle.SetResolutionBandwidth(1.0);

            progress.Report($"[GainTest] 等待信号稳定 {_settleDelayMsSingle}ms…");
            await Task.Delay(_settleDelayMsSingle, ct).ConfigureAwait(false);

            progress.Report("[GainTest] 读取频谱仪峰值…");
            double peakDbm = _saSingle.ReadPeakAmplitude();
            double gain = peakDbm - _rfPowerDbmSingle;

            var limits = GetGainLimits(_singleMode);
            string msg = string.Format(
                CultureInfo.InvariantCulture,
                "[GainTest] mode={0} peak={1:F2} dBm, gain={2:F2} dB (limit=[{3:F2},{4:F2}])",
                _singleMode, peakDbm, gain, limits.Lower, limits.Upper);
            progress.Report(msg);
            Logger.Log(msg);

            bool pass = gain >= limits.Lower && gain <= limits.Upper;

            TestResult result = new TestResult
            {
                TestTime = DateTime.Now,
                TestItem = TestName,
                Value = gain,
                Unit = Unit,
                Judgment = pass ? "Pass" : "Fail",
                Remarks = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}, RF={1:F3}MHz@{2}dBm, IF={3:F3}MHz, IFpeak={4:F2}dBm",
                    _singleMode,
                    _rfFreqHzSingle / 1e6, _rfPowerDbmSingle, _ifFreqHzSingle / 1e6, peakDbm)
            };
            TestReportManager.Instance.Record(result);
            return result;
        }

        // ============================================================
        // M6.x：模式×频点扫测
        // ============================================================

        private async Task<TestResult> ExecuteModeBandSweepAsync(IProgress<string> progress, CancellationToken ct)
        {
            if (_receiver == null || !_receiver.IsOpen)
            {
                throw new InvalidOperationException("GainTest（M6.x 全功能）需要接收机串口已连接，请在『接收机控制』页打开串口。");
            }
            if (!_rfLoop.IsConnected || !_loLoop.IsConnected || !_saLoop.IsConnected)
            {
                throw new InvalidOperationException("GainTest（M6.x 全功能）需要三台仪表（RF/LO/SA）已连接。");
            }

            // 频谱仪入口：清残留状态 + 切连续扫描（Q1=不需要设 RBW/VBW）
            try { _saLoop.Reset(); } catch { /* best-effort */ }
            _saLoop.SetContinuousScan(true);

            // M6.x 业务要求：测试开始时打开 RF/LO 输出，整个 60 点循环期间不再切，
            // 整个 GainTest 结束（异常或正常 return 都包含）时再关闭。
            try
            {
                _rfLoop.SetOutputState(true);
                _loLoop.SetOutputState(true);
                Logger.Log("[GainTest] RF/LO 输出已开启（整个 60 点循环期间保持）。");
            }
            catch (Exception ex)
            {
                Logger.Log("[GainTest] RF/LO :OUTP 1 失败：" + ex.Message);
            }

            TestResult lastResult = null;
            int total = 0;
            foreach (var _ in _modes) total += ModeBandTable.GetBands(_).Count;
            int done = 0;

            try
            {
                foreach (var mode in _modes)
                {
                    ct.ThrowIfCancellationRequested();
                    progress.Report($"[GainTest] === 进入 {mode} ===");
                    Logger.Log($"[GainTest] === 进入 {mode} ===");

                    // M6.x：模式相关参数在循环内按 mode 实时取，避免整组循环共用一组参数。
                    // 与 ReceiverControlPanel.cs#L57-64 的常量保持一致；如修改需同步两处。
                    double ifFreqHz = mode == ReceiverMode.Mode3 ? SaCenterFreqMode3Hz : SaCenterFreqMode12Hz;
                    double rfPowerDbm = mode == ReceiverMode.Mode2 ? RfPowerMode2Dbm : RfPowerMode13Dbm;
                    double loPowerDbm = LoPowerModeDbm;     // 三个模式 LO 均为 3 dBm

                    // A) 模式切换：先通道命令再模式命令（按 M6.x 用户要求 Q1，照搬
                    //    ReceiverControlPanel.OnModeSetClicked 的现成调用）。
                    //    SendChannelThenModeCommand 内部自动在两条命令之间 Sleep(InterSequenceIntervalMs=100ms)。
                    _receiver.SendChannelThenModeCommand(_channel, mode);

                    // B) 频谱仪：中心 = 当前 mode IF + Span 50MHz + 参考电平 10 dBm
                    _saLoop.SetFrequencySpan(ifFreqHz, SaSpanModeHz);
                    _saLoop.SetReferenceLevel(SaReferenceLevelDbm);

                    // C) RF/LO 功率
                    _rfLoop.SetPower(rfPowerDbm);
                    _loLoop.SetPower(loPowerDbm);

                    // D) 模式切换稳定（按 M6.x 用户要求 1.5s）
                    progress.Report($"[GainTest] {mode} 模式切换稳定 {ModeStableMs}ms…");
                    await Task.Delay(ModeStableMs, ct).ConfigureAwait(false);

                    // 3) 遍历该模式下的所有频点
                    IReadOnlyList<ModeBandTable.BandInfo> bands = ModeBandTable.GetBands(mode);
                    foreach (var band in bands)
                    {
                        ct.ThrowIfCancellationRequested();

                        // 1) 设置 RF/LO 频率（按频段名解析 MHz）
                        //    推测 9 修正时序：RF/LO 设频在频段命令之前——
                        //    避免"切到新频段瞬间 LO 还是上频段的"读到错误 IF。
                        if (!band.TryGetRfFreqMHz(out double rfFreqMHz))
                        {
                            string err = $"无法从频段名 '{band.DisplayName}' 解析 RF 频率，跳过该频点。";
                            progress.Report("[GainTest] " + err);
                            Logger.Log("[GainTest] " + err);
                            continue;
                        }
                        double rfFreqHz = rfFreqMHz * 1_000_000.0;
                        double loFreqHz = LoFrequencyRule.Calculate(rfFreqHz, (int)mode);

                        // 2) RF/LO 设频（不再重复 :OUTP 1：业务要求"开始时打开后保持开启"）
                        _rfLoop.SetFrequency(rfFreqHz);
                        _loLoop.SetFrequency(loFreqHz);

                        // 3) 等待 RF/LO 频率切换 + PLL 锁相稳定
                        progress.Report($"[GainTest] {mode} {band.DisplayName} 等 RF/LO 稳定 {RfLoStableMs}ms…");
                        await Task.Delay(RfLoStableMs, ct).ConfigureAwait(false);

                        // 4) 频段命令
                        _receiver.SendBandCommand(mode, band.BandIndex);

                        // 推测 9 + Q2：读 peak 前等 0.5s（频段切换 + 接收机内部稳定）
                        progress.Report($"[GainTest] {mode} {band.DisplayName} 等频段稳定 {BandStableMs}ms…");
                        await Task.Delay(BandStableMs, ct).ConfigureAwait(false);

                        // 5) 读 peak（业务要求：失败时不再 :OUTP 0，保持开启等 finally 统一关）
                        double peakDbm;
                        try
                        {
                            peakDbm = _saLoop.ReadPeakAmplitude();
                        }
                        catch (Exception ex)
                        {
                            string err = $"{mode} {band.DisplayName} 读 peak 失败：{ex.Message}";
                            progress.Report("[GainTest] " + err);
                            Logger.Log("[GainTest] " + err);
                            done++;
                            continue;
                        }
                        double gain = peakDbm - rfPowerDbm;

                        // 按当前模式取判据
                        var modeLimits = GetGainLimits(mode);
                        bool pass = gain >= modeLimits.Lower && gain <= modeLimits.Upper;

                        TestResult result = new TestResult
                        {
                            TestTime = DateTime.Now,
                            Channel = _channel,
                            Mode = mode.ToString(),
                            BandIndex = band.BandIndex,
                            BandDisplayName = band.DisplayName,
                            TestItem = TestName,
                            Value = gain,
                            Unit = Unit,
                            Judgment = pass ? "Pass" : "Fail",
                            Remarks = string.Format(
                                CultureInfo.InvariantCulture,
                                "RF={0:F3}MHz@{1}dBm, LO={2:F3}MHz, IF={3:F3}MHz, IFpeak={4:F2}dBm",
                                rfFreqMHz, rfPowerDbm,
                                loFreqHz / 1e6, ifFreqHz / 1e6, peakDbm)
                        };
                        TestReportManager.Instance.Record(result);
                        lastResult = result;

                        progress.Report(string.Format(
                            CultureInfo.InvariantCulture,
                            "[GainTest] {0} {1} ({2}/{3}): peak={4:F2} dBm, gain={5:F2} dB → {6}",
                            mode, band.DisplayName, ++done, total, peakDbm, gain, result.Judgment));
                        Logger.Log(string.Format(
                            CultureInfo.InvariantCulture,
                            "[GainTest] {0} {1}: peak={2:F2} dBm, gain={3:F2} dB → {4}",
                            mode, band.DisplayName, peakDbm, gain, result.Judgment));

                        // 6) 读 peak 后余量：等频谱仪扫到下一帧（Q5=b）
                        //    业务要求：读完当前频点后给 SA 时间"扫到下一频点的稳定值"，
                        //    避免下一频点的首次读 peak 拿到上频点的尾迹。
                        await Task.Delay(PostReadDelayMs, ct).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                // M6.x 业务要求：测试完成后再关闭信号输出。
                // 正常 return 与异常都走到这里，保证 :OUTP 0 一定执行。
                try { _rfLoop.SetOutputState(false); } catch { /* best-effort */ }
                try { _loLoop.SetOutputState(false); } catch { /* best-effort */ }
                Logger.Log("[GainTest] RF/LO 输出已关闭（finally）。");
            }

            return lastResult;
        }
    }
}
