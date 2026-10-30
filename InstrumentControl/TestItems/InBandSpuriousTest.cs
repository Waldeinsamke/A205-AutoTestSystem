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
    /// 带内虚假信号测试（仅 Mode3）。
    /// <para>物理含义：接收机在 Mode3 下工作于 800~3500 MHz，IF = 750 MHz。
    /// 在 RF 信号源输出端外接低通滤波器（LPF）后，让 RF 信号源在 800~3500 MHz 内扫频，
    /// 接收机内部因非线性 / 本振泄漏产生的杂散落在 IF 带内（590~910 MHz），
    /// 用频谱仪最大保持累积后读到的最高点电平，即为该频点的带内虚假信号值。</para>
    /// <para><b>与其它测试项的关键差异：</b>每测一个频点都要人工更换 RF 输出端的低通滤波器，
    /// 因此本测试项通过 <see cref="FrequencySelector"/> 回调把"选下一个频点"的决定权交给 UI，
    /// 由 UI 弹窗等待操作者点按钮（见 <c>UI/InBandSpuriousFreqForm.cs</c>）。</para>
    /// <para><b>线程约束：</b>本类的所有 <c>await</c> 都<b>不能</b>加 <c>ConfigureAwait(false)</c>——
    /// 其他测试项用 false 是因为纯仪表 IO 无 UI 交互；本类需要回到 UI 线程操作选频点窗体，
    /// 一旦切到线程池线程，弹窗操作会抛跨线程异常。</para>
    /// <para>流程（每个频点）：</para>
    /// <list type="number">
    ///   <item>准备阶段（与"线性最大增益"完全一致）：RF/LO 设频 → 频段命令 → 开输出 → 读 IF peak
    ///         （IF peak 仅作"信号已正常出现"的留痕，不参与判定）</item>
    ///   <item>RF 切扫频：:FREQ:MODE SWE → :FREQ:STAR 800 MHz → :FREQ:STOP 3500 MHz →
    ///         :SOUR:SWE:FREQ:STEP:LIN 10 MHz → :POW -20 dBm → :INIT:CONT 1（LO 完全不动）</item>
    ///   <item>频谱仪：:FREQ:STAR 590 MHz → :FREQ:STOP 910 MHz → RLEV -20 dBm → RBW/VBW 100 kHz →
    ///         :TRACE1:MODE MAXH</item>
    ///   <item>等 5 s → :CALC:MARK1:MAX:PEAK + :MAX:PEAK:SEARCH → 读 Y?（= 带内虚假信号值）
    ///         并读 X?（= 峰值所在频率，写进 Remarks 便于定位是杂散还是真信号）</item>
    ///   <item>收尾：关 MAXH → :INIT:CONT 0 → :FREQ:MODE CW → 关 RF/LO 输出 → 关 marker</item>
    /// </list>
    /// <para>阈值（用户给定）：≤ -50 dBm 合格（越低越好，故为上限判据）。</para>
    /// </summary>
    public class InBandSpuriousTest : ITestItem
    {
        // ============================================================
        // 常量
        // ============================================================

        /// <summary>Mode3 IF 中心频率（Hz）。与 GainTest / HarmonicRejectionTest 一致。</summary>
        private const double SaCenterFreqMode3Hz = 750_000_000.0;

        /// <summary>准备阶段频谱仪Span（Hz）。与"线性最大增益"一致的 50 MHz。</summary>
        private const double SaSpanPrepareHz = 50_000_000.0;

        /// <summary>
        /// 准备阶段频谱仪参考电平（dBm）。与"线性最大增益"一致的 10 dBm——
        /// 此时 IF 真信号约 0 dBm（RF -20 dBm + 增益 20 dB），必须留足屏顶余量才能正确读到 peak。
        /// </summary>
        private const double SaReferenceLevelPrepareDbm = 10.0;

        /// <summary>
        /// 测量阶段频谱仪参考电平（dBm）。虚假信号很弱，把屏顶压到 -10 dBm
        /// 让 marker 在更密的刻度下定位真峰（与 IfRejectionTest 测弱信号同思路）。
        /// </summary>
        private const double SaReferenceLevelMeasureDbm = -10.0;

        private const double SaRbwKHz = 100.0;
        private const double SaVbwKHz = 100.0;

        /// <summary>测量阶段频谱仪起始频率（Hz）。590 MHz。</summary>
        private const double SaStartFreqHz = 590_000_000.0;

        /// <summary>测量阶段频谱仪终止频率（Hz）。910 MHz（覆盖 Mode3 IF 750 MHz 附近带内）。</summary>
        private const double SaStopFreqHz = 910_000_000.0;

        /// <summary>RF 扫频起始频率（Hz）。800 MHz。</summary>
        private const double RfSweepStartHz = 800_000_000.0;

        /// <summary>RF 扫频终止频率（Hz）。3500 MHz。</summary>
        private const double RfSweepStopHz = 3_500_000_000.0;

        /// <summary>RF 扫频步进（Hz）。10 MHz。</summary>
        private const double RfSweepStepHz = 10_000_000.0;

        /// <summary>RF 输出功率（dBm）。扫频段与准备段统一 -20 dBm。</summary>
        private const double RfPowerDbm = -20.0;

        /// <summary>LO 输出功率（dBm）。与 GainTest 同款，三个模式均为 +3 dBm。</summary>
        private const double LoPowerDbm = 3.0;

        // 时序（沿用 GainTest / HarmonicRejectionTest 的常量口径）
        private const int ModeStableMs = 1000;      // 模式切换后稳定
        private const int RfLoStableMs = 200;      // RF/LO 设频后 PLL 锁相稳定
        private const int BandStableMs = 500;      // 频段命令后接收机内部稳定
        private const int SignalOnStableMs = 300;  // 开输出后等 IF 真信号建立
        private const int MaxHoldWaitMs = 10000;    // 最大保持累积时长（用户给定 10s）
        private const int PostFreqDelayMs = 300;   // 频点末余量：让 SA 扫到下一帧

        /// <summary>带内虚假信号合格门限（dBm，上限）。用户给定 ≤ -50 dBm。</summary>
        private const double UpperLimitDbm = -50.0;

        // ============================================================
        // 测试频点（用户给定 9 个；其中 1200 / 2400 在 Mode3 频段表中不存在，
        // 已按用户决策向上取到表内真实频段 1300 / 2500）
        // ============================================================

        /// <summary>
        /// Mode3 测试频点（MHz）：900 / 1300 / 1500 / 1800 / 2100 / 2500 / 2700 / 3000 / 3300。
        /// <para>用户原始清单为 900 / 1200 / 1500 / 1800 / 2100 / 2400 / 2700 / 3000 / 3300，
        /// 但 <see cref="ModeBandTable"/> 的 Mode3 频段不含 1200 与 2400，
        /// 按用户决策向上取最接近的真实频段 1300（bandIndex 7）与 2500（bandIndex 16）。</para>
        /// </summary>
        public static readonly IReadOnlyList<double> DefaultTestFreqsMHz = new[]
        {
            900.0, 1300.0, 1500.0, 1800.0, 2100.0, 2500.0, 2700.0, 3000.0, 3300.0
        };

        // ============================================================
        // 字段
        // ============================================================

        private readonly ReceiverSerialPort _receiver;
        private readonly SignalGenerator _rf;
        private readonly SignalGeneratorLO _lo;
        private readonly SpectrumAnalyzer _sa;
        private readonly int _channel;

        /// <summary>
        /// 频点选择器：由 UI（<c>AutoTestPanel</c>）注入，返回操作者手动选中的频点（MHz）。
        /// <para>为 null 时（无 UI 场景，如单元测试）自动按 <see cref="DefaultTestFreqsMHz"/> 顺序遍历，
        /// 不等待人工干预。</para>
        /// </summary>
        public Func<Task<double>> FrequencySelector { get; set; }

        /// <summary>
        /// 单频点测完后的回调（频点 MHz, 该频点结果），供 UI 更新按钮状态 / 结果列表。
        /// 为 null 时不通知。
        /// </summary>
        public Action<double, TestResult> FrequencyCompleted { get; set; }

        // ============================================================
        // 构造
        // ============================================================

        public InBandSpuriousTest(
            ReceiverSerialPort receiver,
            SignalGenerator rfGenerator,
            SignalGeneratorLO loGenerator,
            SpectrumAnalyzer spectrumAnalyzer,
            int channel)
        {
            if (receiver == null) throw new ArgumentNullException(nameof(receiver));
            if (rfGenerator == null) throw new ArgumentNullException(nameof(rfGenerator));
            if (loGenerator == null) throw new ArgumentNullException(nameof(loGenerator));
            if (spectrumAnalyzer == null) throw new ArgumentNullException(nameof(spectrumAnalyzer));
            if (channel < 1 || channel > 8)
                throw new ArgumentOutOfRangeException(nameof(channel), channel, "通道号必须在 1~8 范围内。");

            _receiver = receiver;
            _rf = rfGenerator;
            _lo = loGenerator;
            _sa = spectrumAnalyzer;
            _channel = channel;
        }

        public string TestName => "带内虚假信号";

        /// <summary>单位：dBm。IF 输出端590~910 MHz 内最高点的绝对电平。</summary>
        public string Unit => "dBm";

        /// <summary>下限：null（本项为上限判据）。UI 显示 [-∞, -50.00]。</summary>
        public double? LowerLimit => null;

        /// <summary>上限：-50 dBm。</summary>
        public double? UpperLimit => UpperLimitDbm;

        // ============================================================
        // 执行
        // ============================================================

        public async Task<TestResult> ExecuteAsync(IProgress<string> progress, CancellationToken ct)
        {
            if (progress == null) throw new ArgumentNullException(nameof(progress));
            ct.ThrowIfCancellationRequested();

            if (!_rf.IsConnected || !_lo.IsConnected || !_sa.IsConnected)
            {
                throw new InvalidOperationException("带内虚假信号测试需要三台仪表（RF/LO/SA）已连接。");
            }

            // 温度切换命令：测试流程发往串口的第一条命令。
            // 必须在任何通道 / 模式 / 频段命令之前——温度档位先落到硬件上，后续命令才有意义。
            TemperatureContext.SendTo(_receiver, TestName, progress);

            // 频谱仪入口：清残留状态 + 切连续扫描（与 GainTest 同款）
            try { _sa.Reset(); } catch { /* best-effort */ }
            _sa.SetContinuousScan(true);

            TestResult lastResult = null;
            int total = DefaultTestFreqsMHz.Count;

            try
            {
                // 本测试仅 Mode3：先通道后模式
                _receiver.SendChannelThenModeCommand(_channel, ReceiverMode.Mode3);

                // 准备阶段频谱仪参数（与"线性最大增益"一致：中心 = IF 750 MHz、Span 50 MHz）
                _sa.SetCenterFrequency(SaCenterFreqMode3Hz);
                _sa.SetSpan(SaSpanPrepareHz);
                _sa.SetReferenceLevel(SaReferenceLevelPrepareDbm);

                // RF/LO 功率
                _rf.SetPower(RfPowerDbm);
                _lo.SetPower(LoPowerDbm);

                progress.Report($"[InBandSpuriousTest] Mode3 模式切换稳定 {ModeStableMs}ms…");
                await Task.Delay(ModeStableMs, ct);

                for (int i = 0; i < total; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    // 取本轮频点：有人工选频点窗体就等操作者点；否则按默认顺序自动遍历
                    double freqMHz = FrequencySelector != null
                        ? await FrequencySelector()
                        : DefaultTestFreqsMHz[i];

                    TestResult r = await ExecuteOneFreqAsync(freqMHz, i + 1, total, progress, ct);
                    if (r != null)
                    {
                        lastResult = r;
                        FrequencyCompleted?.Invoke(freqMHz, r);
                    }
                }
            }
            finally
            {
                // 兜底：退出扫频 + 切回 CW + 关输出 + 关 MAXH + 关 marker
                CleanupInstruments();
            }

            return lastResult;
        }

        // ============================================================
        // 单频点测量
        // ============================================================

        private async Task<TestResult> ExecuteOneFreqAsync(
            double freqMHz,
            int done,
            int total,
            IProgress<string> progress,
            CancellationToken ct)
        {
            // 反查 Mode3 频段（按显示名 "频段XXXX"）
            string displayName = "频段" + ((int)Math.Round(freqMHz)).ToString(CultureInfo.InvariantCulture);
            ModeBandTable.BandInfo band = null;
            foreach (var b in ModeBandTable.GetBands(ReceiverMode.Mode3))
            {
                if (b.DisplayName == displayName)
                {
                    band = b;
                    break;
                }
            }
            if (band == null)
            {
                string err = $"Mode3 {freqMHz} MHz：未在 ModeBandTable 中找到对应频段（{displayName}）。";
                progress.Report("[InBandSpuriousTest] " + err);
                Logger.Log("[InBandSpuriousTest] " + err);
                return FailResult(freqMHz, err);
            }

            double rfFreqHz = freqMHz * 1_000_000.0;
            double loFreqHz = LoFrequencyRule.Calculate(rfFreqHz, (int)ReceiverMode.Mode3);

            // -------- 阶段一：准备（与"线性最大增益"完全一致，让信号完整出现）--------

            // 设频在频段命令之前（沿用 GainTest / HarmonicRejectionTest 的时序）
            _rf.SetFrequency(rfFreqHz);
            _lo.SetFrequency(loFreqHz);
            await Task.Delay(RfLoStableMs, ct);

            _receiver.SendBandCommand(ReceiverMode.Mode3, band.BandIndex);
            await Task.Delay(BandStableMs, ct);

            _sa.SetAllMarkOFF();

            _rf.SetPower(RfPowerDbm);
            _lo.SetPower(LoPowerDbm);

            try
            {
                _rf.SetOutputState(true);
                _lo.SetOutputState(true);
            }
            catch (Exception ex)
            {
                Logger.Log($"[InBandSpuriousTest] {band.DisplayName} 开 RF/LO 输出失败：{ex.Message}");
            }

            await Task.Delay(SignalOnStableMs, ct);

            // 读 IF peak：仅用于确认"信号已正常出现"，写入 Remarks 供排查，不参与判定
            double refPeakDbm = double.NaN;
            try
            {
                refPeakDbm = _sa.ReadPeakAmplitude();
                progress.Report(string.Format(
                    CultureInfo.InvariantCulture,
                    "[InBandSpuriousTest] {0} ({1}/{2}) IF 基准 peak={3:F2}dBm",
                    band.DisplayName, done, total, refPeakDbm));
            }
            catch (Exception ex)
            {
                Logger.Log($"[InBandSpuriousTest] {band.DisplayName} 读 IF 基准 peak 失败：{ex.Message}");
            }

            // -------- 阶段二：RF 扫频 + 频谱仪最大保持 --------
            // 注意：本阶段 LO 完全不动（只有 RF 扫）。

            // RF 切扫频：800~3500 MHz、步进 10 MHz、幅度 -20 dBm
            _rf.SetFrequencyModeSweep();
            _rf.SetSweepStartFrequency(RfSweepStartHz);
            _rf.SetSweepStopFrequency(RfSweepStopHz);
            // Keysight N5173B 必须先设 STAR / STOP 才能接受 STEP:LIN
            _rf.SetSweepStepLinear(RfSweepStepHz);
            _rf.SetPower(RfPowerDbm);
            _rf.SetContinuousSweep(true);

            // 频谱仪：590~910 MHz、弱信号参考电平、窄 RBW/VBW、最大保持
            _sa.SetReferenceLevel(SaReferenceLevelMeasureDbm);
            _sa.SetResolutionBandwidth(SaRbwKHz);
            _sa.SetVideoBandwidth(SaVbwKHz);
            _sa.SetStartFrequency(SaStartFreqHz);
            _sa.SetStopFrequency(SaStopFreqHz);
            _sa.SetMaxHold(true);

            progress.Report(string.Format(
                CultureInfo.InvariantCulture,
                "[InBandSpuriousTest] {0} ({1}/{2}) RF 扫 800~3500MHz/10MHz，SA 最大保持 {3}ms…",
                band.DisplayName, done, total, MaxHoldWaitMs));
            await Task.Delay(MaxHoldWaitMs, ct);

            // ① 先搜主信号峰（RF 扫过当前频点时 IF 750 MHz 出现的期望信号，它比杂散强得多）
            try
            {
                _sa.SendCommand(":CALC:MARK1:STAT ON");
                _sa.SendCommand(":CALC:MARK1:MAX:PEAK");
                _sa.SendCommand(":CALC:MARK1:MAX:PEAK:SEARCH");
            }
            catch (Exception ex)
            {
                string err = $"{band.DisplayName} 设峰值搜索失败：{ex.Message}";
                progress.Report("[InBandSpuriousTest] " + err);
                Logger.Log("[InBandSpuriousTest] " + err);
                CleanupOneFreq();
                return FailResult(freqMHz, err);
            }

            double mainSignalDbm = ReadMarkerYDbm(band.DisplayName);
            double mainSignalFreqHz = ReadMarkerXHz(band.DisplayName);

            // ② 跳到下一个峰 —— 这才是"除主信号之外"的带内最高点，即本测试的实测值
            double spuriousDbm = double.NaN;
            double peakFreqHz = double.NaN;
            try
            {
                _sa.MoveMarkerToNextPeak();
                spuriousDbm = _sa.ReadMarkerAmplitude();
                peakFreqHz = _sa.ReadMarkerFrequency();
            }
            catch (Exception ex)
            {
                string err = $"{band.DisplayName} 读带内次高峰失败：{ex.Message}";
                progress.Report("[InBandSpuriousTest] " + err);
                Logger.Log("[InBandSpuriousTest] " + err);
                CleanupOneFreq();
                return FailResult(freqMHz, err);
            }

            CleanupOneFreq();

            // -------- 判定与记录 --------
            bool pass = !double.IsNaN(spuriousDbm) && spuriousDbm <= UpperLimitDbm;

            string remarks = string.Format(
                CultureInfo.InvariantCulture,
                "RF={0:F0}MHz@{1}dBm, LO={2:F0}MHz, IF基准peak={3}, RF扫800~3500MHz/10MHz, " +
                "SA扫{4:F0}~{5:F0}MHz MAXH{6}ms, 主信号={7:F2}dBm@{8}, 次高峰(虚假)={9:F2}dBm@{10}, 门限≤{11}dBm{12}",
                freqMHz, RfPowerDbm,
                loFreqHz / 1e6,
                double.IsNaN(refPeakDbm) ? "N/A" : refPeakDbm.ToString("F2", CultureInfo.InvariantCulture) + "dBm",
                SaStartFreqHz / 1e6, SaStopFreqHz / 1e6, MaxHoldWaitMs / 1000.0,
                double.IsNaN(mainSignalDbm) ? 0.0 : mainSignalDbm,
                double.IsNaN(mainSignalFreqHz) ? 0.0 : mainSignalFreqHz / 1e6,
                double.IsNaN(spuriousDbm) ? 0.0 : spuriousDbm,
                double.IsNaN(peakFreqHz) ? 0.0 : peakFreqHz / 1e6,
                UpperLimitDbm,
                pass ? "合格" : "不合格");

            TestResult result = new TestResult
            {
                TestTime = DateTime.Now,
                Channel = _channel,
                Mode = ReceiverMode.Mode3.ToString(),
                BandIndex = band.BandIndex,
                BandDisplayName = band.DisplayName,
                TestItem = TestName,
                Value = spuriousDbm,
                Unit = Unit,
                Judgment = double.IsNaN(spuriousDbm) ? "Fail" : (pass ? "Pass" : "Fail"),
                Remarks = remarks,
            };
            TestReportManager.Instance.Record(result);

            progress.Report(string.Format(
                CultureInfo.InvariantCulture,
                "[InBandSpuriousTest] {0} ({1}/{2}): 主信号={3:F2}dBm@{4:F0}MHz, 带内虚假={5:F2}dBm@{6:F0}MHz (≤{7}dBm) → {8}",
                band.DisplayName, done, total,
                double.IsNaN(mainSignalDbm) ? 0.0 : mainSignalDbm,
                double.IsNaN(mainSignalFreqHz) ? 0.0 : mainSignalFreqHz / 1e6,
                double.IsNaN(spuriousDbm) ? 0.0 : spuriousDbm,
                double.IsNaN(peakFreqHz) ? 0.0 : peakFreqHz / 1e6,
                UpperLimitDbm, result.Judgment));
            Logger.Log(string.Format(
                CultureInfo.InvariantCulture,
                "[InBandSpuriousTest] {0}: 主信号={1:F2}dBm@{2:F0}MHz, 带内虚假={3:F2}dBm@{4:F0}MHz → {5}",
                band.DisplayName,
                double.IsNaN(mainSignalDbm) ? 0.0 : mainSignalDbm,
                double.IsNaN(mainSignalFreqHz) ? 0.0 : mainSignalFreqHz / 1e6,
                double.IsNaN(spuriousDbm) ? 0.0 : spuriousDbm,
                double.IsNaN(peakFreqHz) ? 0.0 : peakFreqHz / 1e6,
                result.Judgment));

            await Task.Delay(PostFreqDelayMs, ct);

            return result;
        }

        // ============================================================
        // 仪表收尾
        // ============================================================

        /// <summary>
        /// 单频点收尾：把仪表恢复到"可测下一个频点"的干净状态。
        /// <para><b>注意</b>：<c>ExitSweep()</c> 内部会发 <c>:INIT:CONT 0</c>（停扫描触发）
        /// 与 <c>:FREQ:MODE CW</c>（频率模式切回 CW）两条命令，缺一不可：
        /// 只停扫描的话频率模式仍是 SWE，下一频点只发 :FREQ 也会继续按扫频走，
        /// 读出来的 IF 峰值是错的。</para>
        /// </summary>
        private void CleanupOneFreq()
        {
            try { _sa.SetMaxHold(false); } catch { /* best-effort */ }
            try { _rf.ExitSweep(); } catch { /* best-effort */ }
            try { _rf.SetOutputState(false); } catch { /* best-effort */ }
            try { _lo.SetOutputState(false); } catch { /* best-effort */ }
            try { _sa.SetAllMarkOFF(); } catch { /* best-effort */ }
        }

        /// <summary>异常路径兜底（与 <see cref="CleanupOneFreq"/> 同款，best-effort 全部吞掉）。</summary>
        private void CleanupInstruments()
        {
            CleanupOneFreq();
            Logger.Log("[InBandSpuriousTest] 仪表收尾完成（MAXH 已关、RF 已切回 CW、RF/LO 输出已关）。");
        }

        // ============================================================
        // Marker 读取
        // ============================================================

        private double ReadMarkerYDbm(string bandDisplayName)
        {
            try
            {
                string resp = _sa.QueryString(":CALC:MARK1:Y?");
                if (double.TryParse(resp, NumberStyles.Float, CultureInfo.InvariantCulture, out double dbm))
                {
                    return dbm;
                }
                Logger.Log($"[InBandSpuriousTest] {bandDisplayName} 读 marker Y? 失败：'{resp}'");
            }
            catch (Exception ex)
            {
                Logger.Log($"[InBandSpuriousTest] {bandDisplayName} 读 marker Y? 异常：{ex.Message}");
            }
            return double.NaN;
        }

        private double ReadMarkerXHz(string bandDisplayName)
        {
            try
            {
                string resp = _sa.QueryString(":CALC:MARK1:X?");
                if (double.TryParse(resp, NumberStyles.Float, CultureInfo.InvariantCulture, out double hz))
                {
                    return hz;
                }
                Logger.Log($"[InBandSpuriousTest] {bandDisplayName} 读 marker X? 失败：'{resp}'");
            }
            catch (Exception ex)
            {
                Logger.Log($"[InBandSpuriousTest] {bandDisplayName} 读 marker X? 异常：{ex.Message}");
            }
            return double.NaN;
        }

        private TestResult FailResult(double freqMHz, string reason)
        {
            return new TestResult
            {
                TestTime = DateTime.Now,
                Channel = _channel,
                Mode = ReceiverMode.Mode3.ToString(),
                BandIndex = -1,
                BandDisplayName = "频段" + ((int)Math.Round(freqMHz)).ToString(CultureInfo.InvariantCulture),
                TestItem = TestName,
                Value = double.NaN,
                Unit = Unit,
                Judgment = "Fail",
                Remarks = string.Format(
                    CultureInfo.InvariantCulture,
                    "RF={0:F0}MHz, IF={1:F0}MHz, RF扫800~3500MHz/10MHz, SA扫{2:F0}~{3:F0}MHz, error: {4}",
                    freqMHz, SaCenterFreqMode3Hz / 1e6,
                    SaStartFreqHz / 1e6, SaStopFreqHz / 1e6, reason),
            };
        }
    }
}