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
    /// 中频带外抑制测试。
    /// <para>物理含义：测量接收机对"中频中心左右两侧"邻近带外杂散/谐波信号的抑制度，
    /// 单位 dBm（按用户 Q3-B 决策：先按 dBm 把测试跑通，后续可以切换为 dBc）。</para>
    /// <para>测试方法（按产品手册 + 用户决策）：</para>
    /// <list type="bullet">
    ///   <item>Mode1 / Mode2：RF 不扫频（保持测试频点单点输入），频谱仪分两段扫描
    ///         (10~66 MHz + 74~3500 MHz，避开 70 MHz IF 中心 ±4 MHz 的"中频带")，
    ///         各自找最大点 Y?，取大作为本频点抑制值。</item>
    ///   <item>Mode3：RF 信号源分两段扫频（10~1660 MHz / 166 点+ 2340~2650 MHz / 32 点，
    ///         两段步进均为 10 MHz），频谱仪对应扫 1090~6000 MHz + 100~410 MHz 两段。
    ///         LO 不扫（按 Q4：仅 RF 扫）。各自找最大点 Y?，取大作为本频点抑制值。</item>
    /// </list>
    /// <para>频点（用户给定）：</para>
    /// <list type="bullet">
    ///   <item>Mode1：108 MHz、220 MHz、400 MHz</item>
    ///   <item>Mode2：960 MHz、1090 MHz、1224 MHz、1600 MHz</item>
    ///   <item>Mode3：2000 MHz</item>
    /// </list>
    /// <para>阈值（用户给定）：Mode1 / 2 ≥ 25 dBm；Mode3 ≥ 60 dBm。</para>
    /// </summary>
    public class OutOfBandRejectionTest : ITestItem
    {
        // ============================================================
        // 常量
        // ============================================================

        /// <summary>准备阶段频谱仪参考电平（dBm）。此时 IF 端会出现真信号
        /// （RF −20 dBm + 增益 20 dB ≈ 0 dBm），屏顶必须留足余量避免真信号被裁剪。</summary>
        private const double SaReferenceLevelDbm = 10.0;

        /// <summary>测试阶段频谱仪参考电平（dBm）。用于测量带外杂散/抑制量——信号很弱，
        /// 压低屏顶能让弱信号占据更多刻度，marker 定位更准、读数更精确。
        /// <para><b>设置时机（重要，不要改回"循环前设一次"）</b>：</para>
        /// <list type="bullet">
        ///   <item>模式准备阶段保持 <see cref="SaReferenceLevelDbm"/>（10 dBm），
        ///         因为频点前置阶段 RF 为 CW、IF 端有真信号，10 dBm 才能防止饱和削顶。</item>
        ///   <item>每个频点内、SA 测量窗口设置完成之后（即真信号已移出屏外），
        ///         才切到本值。Mode3 与 Mode1/2 各在各自分支内设一次（互斥路径，非重复）。</item>
        ///   <item>本频点测量全程（含 MAXH 累积期与各分段）不再改动 RLEV。</item>
        ///   <item>频点结束时恢复 <see cref="SaReferenceLevelDbm"/>，供下一个频点前置阶段使用。</item>
        /// </list></summary>
        private const double SaReferenceLevelTestDbm = -10.0;

        private const double SaRbwKHz = 100.0;
        private const double SaVbwKHz = 100.0;

        // 频谱仪等待扫稳定时间。
        // Mode3 RF 真的在扫频（10~1660 / 2340~2650 MHz），需 10s 让 MAXH 累积到稳态；
        // Mode1/2 RF 不扫频，只需要等待频谱仪完成一次 sweep + 短稳态，
        // 3500 MHz span + 100 kHz RBW 在 PXA 上 sweep 通常 ~50ms~几百 ms，给 2s 保险。
        private const int Mode3SweepWaitMs = 10_000;
        private const int Mode12SweepWaitMs = 2_000;

        private const int ModeStableMs = 1500;
        private const int BandStableMs = 500;
        private const int RfLoStableMs = 200;
        private const int TraceSettleMs = 500;

        /// <summary>读值后停留时间（ms）。让marker 读数在频谱仪屏幕上短暂驻留，便于人工观察确认。</summary>
        private const int PostReadDelayMs = 200;

        private const double SaCenterFreqMode12Hz = 70_000_000.0;
        private const double SaCenterFreqMode3Hz = 750_000_000.0;
        private const double StartRfPowerMode13Dbm = -20.0;
        private const double StartRfPowerMode2Dbm = -10.0;
        private const double LoPowerDbm = 3.0;

        // 阈值（用户给定）
        private const double Mode12LowerLimitDb = 25.0;
        private const double Mode3LowerLimitDb = 60.0;

        // ============================================================
        // Mode3 RF 扫频段（用户给定）
        // ============================================================
        private const double Mode3RfSweep1StartHz = 10_000_000.0;
        private const double Mode3RfSweep1StopHz = 1_660_000_000.0;
        private const double Mode3RfSweep2StartHz = 2_340_000_000.0;
        private const double Mode3RfSweep2StopHz = 2_650_000_000.0;

        /// <summary>Mode3 RF 扫频段 1（10~1660 MHz）的扫频点数。
    /// <para>改用点数（<c>:SWE:POIN</c>）而非步进（<c>:SOUR:SWE:FREQ:STEP:LIN</c>）：
    /// 实测步进方式会引起未知异常，改点数规避。</para>
    /// <para>166 点对应步进 (1660−10)/(166−1) = 10.0 MHz，与原 10 MHz 步进完全一致。</para></summary>
    private const int Mode3RfSweep1Points = 166;

    /// <summary>Mode3 RF 扫频段 2（2340~2650 MHz）的扫频点数。
    /// <para>32 点对应步进 (2650−2340)/(32−1) = 10.0 MHz，与原 10 MHz 步进完全一致。</para></summary>
    private const int Mode3RfSweep2Points = 32;

        // ============================================================
        // Mode1/2 频谱仪扫频段（避开 70 MHz IF 中心 ±4 MHz 的"中频带"）
        // ============================================================
        private const double Mode12FirstSweepStartHz = 10_000_000.0;
        private const double Mode12FirstSweepStopHz = 66_000_000.0;
        private const double Mode12SecondSweepStartHz = 74_000_000.0;
        private const double Mode12SecondSweepStopHz = 3_500_000_000.0;

        // ============================================================
        // Mode3 频谱仪扫频段
        // ============================================================
        private const double Mode3SaSweep1StartHz = 1_090_000_000.0;
        private const double Mode3SaSweep1StopHz = 6_000_000_000.0;
        private const double Mode3SaSweep2StartHz = 100_000_000.0;
        private const double Mode3SaSweep2StopHz = 410_000_000.0;

        /// <summary>UI 单行展示用占位值；实际判定在 ExecuteOneFreqAsync 内按 mode 取。</summary>
        public double? LowerLimit => null;
        public double? UpperLimit => null;

        // ============================================================
        // 频点配置
        // ============================================================
        public static readonly IReadOnlyDictionary<ReceiverMode, double[]> DefaultTestFreqsMHzPerMode =
            new Dictionary<ReceiverMode, double[]>
            {
                { ReceiverMode.Mode1, new[] { 108.0, 220.0, 400.0 } },
                { ReceiverMode.Mode2, new[] { 960.0, 1090.0, 1224.0, 1600.0 } },
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
        public OutOfBandRejectionTest(
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

        public string TestName => "中频带外抑制";
        public string Unit => "dBm";

        private static double GetLowerLimitByMode(ReceiverMode mode)
        {
            switch (mode)
            {
                case ReceiverMode.Mode1:
                case ReceiverMode.Mode2:
                    return Mode12LowerLimitDb;
                case ReceiverMode.Mode3:
                    return Mode3LowerLimitDb;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, "未知的工作模式。");
            }
        }

        public async Task<TestResult> ExecuteAsync(IProgress<string> progress, CancellationToken ct)
        {
            if (progress == null) throw new ArgumentNullException(nameof(progress));
            ct.ThrowIfCancellationRequested();

            if (!_rf.IsConnected || !_lo.IsConnected || !_sa.IsConnected)
            {
                throw new InvalidOperationException("OutOfBandRejectionTest 需要三台仪表（RF/LO/SA）已连接。");
            }

            // 频谱仪入口
            try { _sa.Reset(); } catch { /* best-effort */ }
            _sa.SetContinuousScan(true);

            // 测试开始时打开 RF/LO 输出；finally 关闭
            try
            {
                _rf.SetOutputState(true);
                _lo.SetOutputState(true);
                Logger.Log("[OutOfBandRejectionTest] RF/LO 输出已开启。");
            }
            catch (Exception ex)
            {
                Logger.Log("[OutOfBandRejectionTest] RF/LO :OUTP 1 失败：" + ex.Message);
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
                    progress.Report($"[OutOfBandRejectionTest] === 进入 {mode} ===");
                    Logger.Log($"[OutOfBandRejectionTest] === 进入 {mode} ===");

                    double ifFreqHz = (mode == ReceiverMode.Mode3) ? SaCenterFreqMode3Hz : SaCenterFreqMode12Hz;
                    double saCenterHz = ifFreqHz;
                    double rfStartPowerDbm = (mode == ReceiverMode.Mode2) ? StartRfPowerMode2Dbm : StartRfPowerMode13Dbm;

                    _receiver.SendChannelThenModeCommand(_channel, mode);

                    _sa.SetCenterFrequency(saCenterHz);
                    _sa.SetReferenceLevel(SaReferenceLevelDbm);
                    _sa.SetResolutionBandwidth(SaRbwKHz);
                    _sa.SetVideoBandwidth(SaVbwKHz);

                    progress.Report($"[OutOfBandRejectionTest] {mode} 模式切换稳定 {ModeStableMs}ms…");
                    await Task.Delay(ModeStableMs, ct).ConfigureAwait(false);

                    if (!_testFreqsMHzPerMode.TryGetValue(mode, out double[] freqs) || freqs == null || freqs.Length == 0)
                    {
                        string warn = $"未配置 {mode} 的测试频点，跳过。";
                        progress.Report("[OutOfBandRejectionTest] " + warn);
                        Logger.Log("[OutOfBandRejectionTest] " + warn);
                        continue;
                    }

                    // 参考电平由频点内部控制：频点前置阶段（真信号存在）用 10 dBm 防饱和，
                    // 设好SA 测量窗口后切到 −10 dBm 精测弱杂散。此处不做统一设置。
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
                // 兜底：退出 RF 扫频模式 + 关 RF/LO 输出
                try { _rf.SetContinuousSweep(false); } catch { /* best-effort */ }
                try { _rf.SetOutputState(false); } catch { /* best-effort */ }
                try { _lo.SetOutputState(false); } catch { /* best-effort */ }
                Logger.Log("[OutOfBandRejectionTest] RF/LO 输出已关闭（finally）。");
            }

            return lastResult;
        }

        // ============================================================
        // 单频点带外抑制
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
                progress.Report("[OutOfBandRejectionTest] " + err);
                Logger.Log("[OutOfBandRejectionTest] " + err);
                return null;
            }

            double rfFreqHz = freqMHz * 1_000_000.0;
            double loFreqHz = LoFrequencyRule.Calculate(rfFreqHz, (int)mode);

            // -------- 标准增益设置（让 IF 真信号稳定、混频器进入待测状态）--------

            _rf.SetFrequency(rfFreqHz);
            _lo.SetFrequency(loFreqHz);
            await Task.Delay(RfLoStableMs, ct).ConfigureAwait(false);

            _receiver.SendBandCommand(mode, band.BandIndex);
            await Task.Delay(BandStableMs, ct).ConfigureAwait(false);

            _sa.SetAllMarkOFF();

            _rf.SetPower(rfStartPowerDbm);
            await Task.Delay(RfLoStableMs, ct).ConfigureAwait(false);

            _receiver.SendAttenuationCommand(_channel, 0);
            await Task.Delay(TraceSettleMs, ct).ConfigureAwait(false);

            double maxSegmentDb;
            double maxSegmentStart;
            double maxSegmentStop;
            string sweepModeDesc;

            if (mode == ReceiverMode.Mode3)
            {
                // ---- Mode3：RF 两段扫频 ----

                // 第 1 段：RF 10~1660 MHz、SA 1090~6000 MHz
                // 进入段1：开启扫频
                _rf.SetFrequencyModeSweep();
                _rf.SetSweepStartFrequency(Mode3RfSweep1StartHz);
                _rf.SetSweepStopFrequency(Mode3RfSweep1StopHz);
                // 用点数设置扫频（实测 STEP:LIN 步进方式会引起未知异常，改 POIN 规避）
                _rf.SetSweepPoints(Mode3RfSweep1Points);
                _rf.SetContinuousSweep(true);
                // LO 不动（按 Q4：仅 RF 扫）。

                _sa.SetStartFrequency(Mode3SaSweep1StartHz);
                _sa.SetStopFrequency(Mode3SaSweep1StopHz);
                // 测量窗口已切走（真信号已不在屏上）→ 下压屏顶到 −10 dBm 精测弱杂散。
                // 本频点测量全程不再改 RLEV。
                _sa.SetReferenceLevel(SaReferenceLevelTestDbm);
                // Mode3 段 1：频谱仪开 MAXH 让 trace 在 RF 扫频周期内累积最大点
                _sa.SetMaxHold(true);
                await Task.Delay(Mode3SweepWaitMs, ct).ConfigureAwait(false);

                _sa.SendCommand(":CALC:MARK1:STAT ON");
                _sa.SendCommand(":CALC:MARK1:MAX:PEAK");
                _sa.SendCommand(":CALC:MARK1:MAX:PEAK:SEARCH");
                double v1 = ReadMarkerYDbm("Mode3 第 1 段", mode, band.DisplayName);
                await Task.Delay(PostReadDelayMs, ct).ConfigureAwait(false);

                // 离开段1：关闭 RF 扫频 + 关 MAXH（避免段 1 的历史最大峰污染段 2 读值）
                try { _rf.SetContinuousSweep(false); } catch { /* best-effort */ }
                _sa.SetMaxHold(false);

                // 第 2 段：RF 2340~2650 MHz、SA 100~410 MHz
                // 进入段2：重新开启扫频
                _rf.SetFrequencyModeSweep();
                _rf.SetSweepStartFrequency(Mode3RfSweep2StartHz);
                _rf.SetSweepStopFrequency(Mode3RfSweep2StopHz);
                _rf.SetSweepPoints(Mode3RfSweep2Points);
                _rf.SetContinuousSweep(true);

                _sa.SetStartFrequency(Mode3SaSweep2StartHz);
                _sa.SetStopFrequency(Mode3SaSweep2StopHz);
                // 第 2 段重新开 MAXH，让 trace 在新的扫频段上从头累积。
                _sa.SetMaxHold(true);
                await Task.Delay(Mode3SweepWaitMs, ct).ConfigureAwait(false);

                _sa.SendCommand(":CALC:MARK1:STAT ON");
                _sa.SendCommand(":CALC:MARK1:MAX:PEAK");
                _sa.SendCommand(":CALC:MARK1:MAX:PEAK:SEARCH");
                double v2 = ReadMarkerYDbm("Mode3 第 2 段", mode, band.DisplayName);
                await Task.Delay(PostReadDelayMs, ct).ConfigureAwait(false);

                // 离开段2：关闭 RF 扫频 + 关 MAXH（不论是否异常，MAXH 都得关掉）
                try { _rf.SetContinuousSweep(false); } catch { /* best-effort */ }
                try { _sa.SetMaxHold(false); } catch { /* best-effort */ }

                maxSegmentDb = Math.Max(v1, v2);
                maxSegmentStart = (v1 >= v2) ? Mode3SaSweep1StartHz : Mode3SaSweep2StartHz;
                maxSegmentStop = (v1 >= v2) ? Mode3SaSweep1StopHz : Mode3SaSweep2StopHz;
                sweepModeDesc = $"RF 扫 {Mode3RfSweep1StartHz / 1e6}~{Mode3RfSweep1StopHz / 1e6}MHz/" +
                                $"{Mode3RfSweep2StartHz / 1e6}~{Mode3RfSweep2StopHz / 1e6}MHz，SA 对应扫段";
            }
            else
            {
                // ---- Mode1 / Mode2：RF 单频点（不扫），频谱仪两段扫描 ----

                _sa.SetStartFrequency(Mode12FirstSweepStartHz);
                _sa.SetStopFrequency(Mode12FirstSweepStopHz);
                // 测量窗口已切走（真信号已不在屏上）→ 下压屏顶到 −10 dBm 精测弱杂散。
                // 本频点测量全程（含段 2）不再改 RLEV。
                _sa.SetReferenceLevel(SaReferenceLevelTestDbm);
                await Task.Delay(Mode12SweepWaitMs, ct).ConfigureAwait(false);

                _sa.SendCommand(":CALC:MARK1:STAT ON");
                _sa.SendCommand(":CALC:MARK1:MAX:PEAK");
                _sa.SendCommand(":CALC:MARK1:MAX:PEAK:SEARCH");
                double v1 = ReadMarkerYDbm("Mode1/2 第 1 段", mode, band.DisplayName);
                await Task.Delay(PostReadDelayMs, ct).ConfigureAwait(false);

                _sa.SetStartFrequency(Mode12SecondSweepStartHz);
                _sa.SetStopFrequency(Mode12SecondSweepStopHz);
                await Task.Delay(Mode12SweepWaitMs, ct).ConfigureAwait(false);

                _sa.SendCommand(":CALC:MARK1:MAX:PEAK");
                _sa.SendCommand(":CALC:MARK1:MAX:PEAK:SEARCH");
                double v2 = ReadMarkerYDbm("Mode1/2 第 2 段", mode, band.DisplayName);
                await Task.Delay(PostReadDelayMs, ct).ConfigureAwait(false);

                maxSegmentDb = Math.Max(v1, v2);
                maxSegmentStart = (v1 >= v2) ? Mode12FirstSweepStartHz : Mode12SecondSweepStartHz;
                maxSegmentStop = (v1 >= v2) ? Mode12FirstSweepStopHz : Mode12SecondSweepStopHz;
                sweepModeDesc = "RF 不扫频、SA 分两段扫描（避开 70 MHz ±4 MHz 中频带）";
            }

            double limit = GetLowerLimitByMode(mode);
            bool pass = !double.IsNaN(maxSegmentDb) && maxSegmentDb >= limit;
            string remarks = string.Format(
                CultureInfo.InvariantCulture,
                "{0} {1} ({2}/{3}), IF={4:F0}MHz, {5}, SA扫max段={6:F0}~{7:F0}MHz, max={8:F2}dBm (≥{9}dBm{10})",
                mode, band.DisplayName, done, total,
                ifFreqHz / 1e6,
                sweepModeDesc,
                maxSegmentStart / 1e6, maxSegmentStop / 1e6,
                maxSegmentDb, limit,
                pass ? "合格" : "不合格");

            TestResult result = new TestResult
            {
                TestTime = DateTime.Now,
                Channel = _channel,
                Mode = mode.ToString(),
                BandIndex = band.BandIndex,
                BandDisplayName = band.DisplayName,
                TestItem = TestName,
                Value = maxSegmentDb,
                Unit = Unit,
                Judgment = double.IsNaN(maxSegmentDb) ? "Fail" : (pass ? "Pass" : "Fail"),
                Remarks = remarks,
            };
            TestReportManager.Instance.Record(result);

            progress.Report(string.Format(
                CultureInfo.InvariantCulture,
                "[OutOfBandRejectionTest] {0} {1} ({2}/{3}): max={4:F2}dBm (≥{5}dBm) → {6}",
                mode, band.DisplayName, done, total, maxSegmentDb, limit, result.Judgment));
            Logger.Log(string.Format(
                CultureInfo.InvariantCulture,
                "[OutOfBandRejectionTest] {0} {1}: max={2:F2}dBm → {3}",
                mode, band.DisplayName, maxSegmentDb, result.Judgment));

            _sa.SetAllMarkOFF();

            // 频点结束：恢复屏顶到 10 dBm。下一个频点的前置阶段（RF 为 CW、IF 端有真信号）
            // 将在 10 dBm 屏顶下进行，避免真信号饱和被削平。
            _sa.SetReferenceLevel(SaReferenceLevelDbm);

            return result;
        }

        private double ReadMarkerYDbm(string stage, ReceiverMode mode, string bandDisplayName)
        {
            string resp;
            try
            {
                resp = _sa.QueryString(":CALC:MARK1:Y?");
            }
            catch (Exception ex)
            {
                Logger.Log($"[OutOfBandRejectionTest] {mode} {bandDisplayName} {stage} 读 marker Y 异常：{ex.Message}");
                return double.NaN;
            }

            if (!double.TryParse(resp, NumberStyles.Float, CultureInfo.InvariantCulture, out double dbm))
            {
                Logger.Log($"[OutOfBandRejectionTest] {mode} {bandDisplayName} {stage} 读 marker Y 失败：'{resp}'");
                return double.NaN;
            }
            return dbm;
        }
    }
}
