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
            : this(rfGenerator, loGenerator, spectrumAnalyzer,
                   rfFreqHz, rfPowerDbm,
                   LoFrequencyRule.Calculate(rfFreqHz, mode),
                   loPowerDbm, ifFreqHz)
        {
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

        /// <summary>判据下限（dB）。null 表示不设下限。</summary>
        public double? LowerLimit { get; set; } = 10.0;

        /// <summary>判据上限（dB）。null 表示不设上限。</summary>
        public double? UpperLimit { get; set; } = 30.0;

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

            TestResult lastResult = null;
            int total = 0;
            foreach (var _ in _modes) total += ModeBandTable.GetBands(_).Count;
            int done = 0;

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

                // 1) 模式命令
                _receiver.SendModeCommand(mode);

                // 2) 模式联动：频谱仪中心 = 当前 mode 的 IF，功率按当前 mode
                _saLoop.SetFrequencySpan(ifFreqHz, SaSpanModeHz);
                _rfLoop.SetPower(rfPowerDbm);
                _loLoop.SetPower(loPowerDbm);

                // 3) 遍历该模式下的所有频点
                IReadOnlyList<ModeBandTable.BandInfo> bands = ModeBandTable.GetBands(mode);
                foreach (var band in bands)
                {
                    ct.ThrowIfCancellationRequested();

                    // a) 频段命令
                    _receiver.SendBandCommand(mode, band.BandIndex);

                    // b) 设置 RF 频率（按频段名解析 MHz）
                    if (!band.TryGetRffeatMHz(out double rfFreqMHz))
                    {
                        string err = $"无法从频段名 '{band.DisplayName}' 解析 RF 频率，跳过该频点。";
                        progress.Report("[GainTest] " + err);
                        Logger.Log("[GainTest] " + err);
                        continue;
                    }
                    double rfFreqHz = rfFreqMHz * 1_000_000.0;

                    // c) 设置 RF/LO 频率
                    _rfLoop.SetFrequency(rfFreqHz);
                    double loFreqHz = LoFrequencyRule.Calculate(rfFreqHz, (int)mode);
                    _loLoop.SetFrequency(loFreqHz);

                    // d) RF/LO 输出 ON
                    _rfLoop.SetOutputState(true);
                    _loLoop.SetOutputState(true);

                    // e) 等待稳定
                    progress.Report($"[GainTest] {mode} {band.DisplayName} 等待 {_settleDelayMsLoop}ms…");
                    await Task.Delay(_settleDelayMsLoop, ct).ConfigureAwait(false);

                    // f) 读 peak
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
                        _rfLoop.SetOutputState(false);
                        _loLoop.SetOutputState(false);
                        done++;
                        continue;
                    }
                    double gain = peakDbm - rfPowerDbm;

                    bool pass = (!LowerLimit.HasValue || gain >= LowerLimit.Value)
                             && (!UpperLimit.HasValue || gain <= UpperLimit.Value);

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
                }

                // 4) 该模式结束后 RF/LO :OUTP 0，避免下一模式首频点读到上模式尾迹
                try { _rfLoop.SetOutputState(false); } catch { /* best-effort */ }
                try { _loLoop.SetOutputState(false); } catch { /* best-effort */ }
            }

            return lastResult;
        }
    }
}
