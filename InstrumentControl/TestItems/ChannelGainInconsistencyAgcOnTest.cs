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
    /// 通道间增益不一致性（AGC 起控）。
    /// <para><b>复合测试项</b>：先实测本模式各频点的"带 32dB 衰减"的线性最大增益，
    /// 再据此计算通道间增益不一致性——与
    /// <see cref="ChannelGainInconsistencyTest"/>（AGC 不起控）共用同一套比对算法
    /// （<see cref="GainConsistencyCalculator"/>），但数据来源不同：
    /// 不起控读 <see cref="GainResultRepository"/> 里的现成数据，起控自己测。</para>
    /// <para><b>测试范围</b>：仅 Mode3，9 个频点：
    /// 900 / 1300 / 1500 / 1800 / 2100 / 2500 / 2700 / 3000 / 3300 MHz。</para>
    /// <para><b>每频点执行序列</b>：</para>
    /// <list type="number">
    ///   <item>RF/LO 设频（RF = 频点 MHz，LO 按 <see cref="LoFrequencyRule"/> 计算）→ 等稳定</item>
    ///   <item><b>发频段命令</b> → 等频段稳定</item>
    ///   <item><b>发衰减命令 32dB</b>（在频段命令之后）→ 等衰减稳定</item>
    ///   <item>读 peak → 原始增益 = peak − RF 功率 → <b>逐条记入报表</b></item>
    ///   <item>读完 mark 值后<b>观察 500ms</b>（便于在频谱仪屏幕上核对读数）→ <b>关闭 marker</b>（:CALC:MARK1:AOFF），避免带着开着的 mark 进入下一频点</item>
    /// </list>
    /// <para><b>判定口径</b>（与不起控一致）：</para>
    /// <list type="bullet">
    ///   <item>等效增益 = 原始带衰减值 + 32 dB（抵消接收机 32dB 输入衰减）</item>
    ///   <item>等效规格 = [18, 22] dB</item>
    ///   <item>频点行判定 = 该频点等效增益是否落在 [18,22]</item>
    ///   <item>汇总行：值 = 最差频点的<b>原始带衰减值</b>，判定 = 极差 ≤ 1 dB</item>
    /// </list>
    /// </summary>
    public class ChannelGainInconsistencyAgcOnTest : ITestItem
    {
        /// <summary>测试项目名称。</summary>
        public const string ItemName = "通道间增益不一致性（AGC起控）";

        /// <summary>接收机输入衰减档位（dB）。</summary>
        private const int AttenuationDb = 32;

        /// <summary>
        /// 等效增益规格（dB）。
        /// <para>产品手册对 AGC 起控态的规格未明确，暂按线性最大增益的 18~22 dB 取值；
        /// 比较时用"等效增益"（原始带衰减值 + 32）与之比较。</para>
        /// </summary>
        private const double EquivalentGainLower = 18.0;
        private const double EquivalentGainUpper = 22.0;

        // 频谱仪参考电平（dBm）。加 32dB 衰减后 IF 信号约 −32dBm，
        // 默认 10dBm 参考电平下信号会沉到屏底读不出来 → 读数期间切到 −10dBm。
        private const double SaReferenceLevelDbm = 10.0;
        private const double SaReferenceLevelAttenuatedDbm = -10.0;

        private const double SaSpanMode3Hz = 50_000_000.0;
        private const double SaCenterFreqMode3Hz = 750_000_000.0;
        private const double RfPowerMode3Dbm = -20.0;
        private const double LoPowerModeDbm = 3.0;

        // 各阶段等待时间（与 GainTest / AgcControlTest 保持同一量级）
        private const int ModeStableMs = 1000;       // 模式切换后稳定
        private const int RfLoStableMs = 200;       // RF/LO 频率切换 + PLL 锁相
        private const int BandStableMs = 500;       // 频段命令后稳定
        /// <summary>
        /// 衰减切换后稳定时间（ms）。
        /// <para>原取 1500ms（参照 AgcControlTest 的 AgcStepStableMs），实测 9 个频点读数稳定，
        /// 缩短到 1000ms 可省 9×0.5s = 4.5s。</para>
        /// </summary>
        private const int AttenStableMs = 1000;
        private const int PostReadDelayMs = 200;    // 读 peak 后给 SA 扫下一帧
        /// <summary>
        /// 读 mark 值后、关闭 marker 前的观察延时（ms）。
        /// <para>仅用于正常读数路径：留出时间在频谱仪屏幕上观察 marker 读数，
        /// 便于人工核对。异常/失败路径不加此延时。</para>
        /// </summary>
        private const int MarkObserveMs = 500;

        /// <summary>
        /// 本指标测试的频点（MHz）。仅 Mode3。
        /// </summary>
        private static readonly int[] _testFreqsMHz = { 900, 1300, 1500, 1800, 2100, 2500, 2700, 3000, 3300 };

        private readonly ReceiverSerialPort _receiver;
        private readonly SignalGenerator _rf;
        private readonly SignalGeneratorLO _lo;
        private readonly SpectrumAnalyzer _sa;
        private readonly int _channel;

        /// <summary>
        /// 构造。
        /// </summary>
        public ChannelGainInconsistencyAgcOnTest(
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

        public string TestName => ItemName;

        /// <summary>单位 dB。</summary>
        public string Unit => "dB";

        /// <summary>判据下限。实际判据是"极差 ≤ 1 dB"，极差恒 ≥ 0，故下限取 null。</summary>
        public double? LowerLimit => null;

        /// <summary>
        /// 判据上限：极差上限 1.0 dB。
        /// <para>注意：报表 Value 是"最差频点的原始带衰减值"，不是极差；
        /// Pass/Fail 由极差决定，因此 DGV 阈值列显示的 [−∞, 1.00] 是真实判据。</para>
        /// </summary>
        public double? UpperLimit => GainConsistencyCalculator.RangeLimitDb;

        public async Task<TestResult> ExecuteAsync(IProgress<string> progress, CancellationToken ct)
        {
            if (progress == null) throw new ArgumentNullException(nameof(progress));
            ct.ThrowIfCancellationRequested();

            if (!_receiver.IsOpen)
                throw new InvalidOperationException("通道间增益不一致性（AGC起控）需要接收机串口已连接。");
            if (!_rf.IsConnected || !_lo.IsConnected || !_sa.IsConnected)
                throw new InvalidOperationException("通道间增益不一致性（AGC起控）需要三台仪表（RF/LO/SA）已连接。");

            // 温度切换命令：测试流程发往串口的第一条命令。
            // 必须在任何通道 / 模式 / 频段命令之前——温度档位先落到硬件上，后续命令才有意义。
            TemperatureContext.SendTo(_receiver, TestName, progress);

            // 频谱仪入口：清残留状态 + 切连续扫描
            try { _sa.Reset(); } catch { /* best-effort */ }
            _sa.SetContinuousScan(true);

            // RF/LO 输出整轮保持开启，finally 统一关闭
            try
            {
                _rf.SetOutputState(true);
                _lo.SetOutputState(true);
                Logger.Log("[GainInconsistencyAgcOn] RF/LO 输出已开启。");
            }
            catch (Exception ex)
            {
                Logger.Log("[GainInconsistencyAgcOn] RF/LO :OUTP 1 失败：" + ex.Message);
            }

            var bands = new List<string>(_testFreqsMHz.Length);
            var rawGains = new List<double>(_testFreqsMHz.Length);   // 原始带衰减值
            var equivalentGains = new List<double>(_testFreqsMHz.Length); // 等效增益（+32）

            try
            {
                // ---- 模式切换（只需 Mode3 一次）----
                progress.Report($"[{ItemName}] === 进入 Mode3 ===");
                Logger.Log($"[{ItemName}] === 进入 Mode3 ===");

                _receiver.SendChannelThenModeCommand(_channel, ReceiverMode.Mode3);
                _sa.SetFrequencySpan(SaCenterFreqMode3Hz, SaSpanMode3Hz);
                _rf.SetPower(RfPowerMode3Dbm);
                _lo.SetPower(LoPowerModeDbm);

                progress.Report($"[{ItemName}] Mode3 模式切换稳定 {ModeStableMs}ms…");
                await Task.Delay(ModeStableMs, ct).ConfigureAwait(false);

                // ---- 9 个频点循环 ----
                int done = 0;
                foreach (int mhz in _testFreqsMHz)
                {
                    ct.ThrowIfCancellationRequested();

                    string bandName = "频段" + mhz.ToString(CultureInfo.InvariantCulture);
                    var band = FindBand(ReceiverMode.Mode3, bandName);
                    if (band == null)
                    {
                        string err = $"{bandName}：未在 ModeBandTable 中找到对应频段，跳过。";
                        progress.Report("[" + ItemName + "] " + err);
                        Logger.Log("[GainInconsistencyAgcOn] " + err);
                        continue;
                    }

                    double rawGain = await MeasureOneFreqAsync(band, progress, ct).ConfigureAwait(false);
                    done++;
                    if (double.IsNaN(rawGain))
                    {
                        // 该频点读数失败：不参与比对（末尾会因数据不全而整项 Fail）
                        progress.Report($"[{ItemName}] {bandName} 读数失败，不参与比对。");
                        continue;
                    }

                    double equivalentGain = rawGain + AttenuationDb;
                    bool pointPass = GainConsistencyCalculator.IsInRange(
                        equivalentGain, EquivalentGainLower, EquivalentGainUpper);

                    // 先入列表再拼备注——否则"各频点增益"明细会漏掉当前频点。
                    bands.Add(band.DisplayName);
                    rawGains.Add(rawGain);
                    equivalentGains.Add(equivalentGain);

                    string remarks = string.Format(
                        CultureInfo.InvariantCulture,
                        "带衰减{0}dB实测增益={1:F2}dB, 等效增益={2:F2}dB (规格[{3:F0},{4:F0}] {5}), 各频点等效增益: {6}",
                        AttenuationDb, rawGain, equivalentGain,
                        EquivalentGainLower, EquivalentGainUpper,
                        pointPass ? "合格" : "不合格",
                        GainConsistencyCalculator.FormatGainDetails(bands, equivalentGains));

                    var r = new TestResult
                    {
                        TestTime = DateTime.Now,
                        Channel = _channel,
                        Mode = ReceiverMode.Mode3.ToString(),
                        BandIndex = band.BandIndex,
                        BandDisplayName = band.DisplayName,
                        TestItem = TestName,
                        Value = rawGain,
                        Unit = Unit,
                        Judgment = pointPass ? "Pass" : "Fail",
                        Remarks = remarks
                    };
                    TestReportManager.Instance.Record(r);

                    progress.Report(string.Format(
                        CultureInfo.InvariantCulture,
                        "[{0}] {1} ({2}/{3}): 带衰减增益={4:F2}dB, 等效增益={5:F2}dB → {6}",
                        ItemName, band.DisplayName, done, _testFreqsMHz.Length,
                        rawGain, equivalentGain, r.Judgment));
                    Logger.Log(string.Format(
                        CultureInfo.InvariantCulture,
                        "[GainInconsistencyAgcOn] {0}: 带衰减增益={1:F2}dB, 等效增益={2:F2}dB → {3}",
                        band.DisplayName, rawGain, equivalentGain, r.Judgment));
                }
            }
            finally
            {
                // 衰减复位到 0dB：虽然重新上电会重置，但显式归零避免污染后续测试项。
                try { _receiver.SendAttenuationCommand(_channel, 0); } catch { /* best-effort */ }
                // 参考电平复位
                try { _sa.SetReferenceLevel(SaReferenceLevelDbm); } catch { /* best-effort */ }
                // marker 兜底关闭：正常路径每个频点读完就已关闭，此处防止异常中断时残留 marker。
                try { _sa.SetAllMarkOFF(); } catch { /* best-effort */ }
                try { _rf.SetOutputState(false); } catch { /* best-effort */ }
                try { _lo.SetOutputState(false); } catch { /* best-effort */ }
                Logger.Log("[GainInconsistencyAgcOn] 衰减已复位 0dB，参考电平已复位，marker 已关闭，RF/LO 输出已关闭（finally）。");
            }

            // ---- 汇总：与不起控同一套比对算法 ----
            return Summarize(bands, rawGains, equivalentGains);
        }

        /// <summary>
        /// 汇总比对，产出最后一条结果（值 = 最差频点的原始带衰减值，判定 = 极差 ≤ 1 dB）。
        /// </summary>
        private TestResult Summarize(
            List<string> bands,
            List<double> rawGains,
            List<double> equivalentGains)
        {
            if (equivalentGains.Count == 0)
            {
                string failRemark = "所有频点读数失败，无法计算（请检查仪表连接与接收机状态）。";
                Logger.Log("[GainInconsistencyAgcOn] " + failRemark);
                var failResult = new TestResult
                {
                    TestTime = DateTime.Now,
                    Channel = _channel,
                    Mode = ReceiverMode.Mode3.ToString(),
                    BandIndex = null,
                    BandDisplayName = string.Empty,
                    TestItem = TestName,
                    Value = double.NaN,
                    Unit = Unit,
                    Judgment = "Fail",
                    Remarks = failRemark
                };
                TestReportManager.Instance.Record(failResult);
                return failResult;
            }

            var comparison = GainConsistencyCalculator.Compare(
                bands, equivalentGains, EquivalentGainLower, EquivalentGainUpper);

            int worstIndex = comparison.WorstIndex;
            string remark = string.Format(
                CultureInfo.InvariantCulture,
                "最差值={0:F2}dB@{1}(带衰减实测, 距等效边界{2:F2}dB), 等效增益={3:F2}dB (规格[{4:F0},{5:F0}]), 极差={6:F2}dB(判据≤{7:F1}dB), 各频点等效增益: {8}",
                rawGains[worstIndex], comparison.WorstBand, comparison.WorstDistance,
                equivalentGains[worstIndex], EquivalentGainLower, EquivalentGainUpper,
                comparison.Range, GainConsistencyCalculator.RangeLimitDb,
                GainConsistencyCalculator.FormatGainDetails(bands, equivalentGains));

            Logger.Log("[GainInconsistencyAgcOn] 汇总 " + remark);

            var summary = new TestResult
            {
                TestTime = DateTime.Now,
                Channel = _channel,
                Mode = ReceiverMode.Mode3.ToString(),
                BandIndex = FindBand(ReceiverMode.Mode3, comparison.WorstBand)?.BandIndex,
                BandDisplayName = comparison.WorstBand,
                TestItem = TestName,
                // 报表 Value = 最差频点的**原始带衰减值**（不是等效增益），
                // 与 9 条频点行同口径。等效增益写在备注里。
                Value = rawGains[worstIndex],
                Unit = Unit,
                Judgment = comparison.RangePass ? "Pass" : "Fail",
                Remarks = remark
            };
            TestReportManager.Instance.Record(summary);
            return summary;
        }

        /// <summary>
        /// 实测单个频点：设频 → 频段命令 → 加 32dB 衰减 → 读 peak。
        /// <para><b>衰减命令在频段命令之后发</b>（按需求约定）。</para>
        /// </summary>
        /// <returns>原始带衰减增益（dB）；读数失败返回 NaN。</returns>
        private async Task<double> MeasureOneFreqAsync(
            ModeBandTable.BandInfo band,
            IProgress<string> progress,
            CancellationToken ct)
        {
            double rfFreqMHz = 0;
            if (!band.TryGetRfFreqMHz(out rfFreqMHz))
            {
                string err = $"无法从频段名 '{band.DisplayName}' 解析 RF 频率。";
                progress.Report("[" + ItemName + "] " + err);
                Logger.Log("[GainInconsistencyAgcOn] " + err);
                return double.NaN;
            }
            double rfFreqHz = rfFreqMHz * 1_000_000.0;
            double loFreqHz = LoFrequencyRule.Calculate(rfFreqHz, (int)ReceiverMode.Mode3);

            try
            {
                // 1) RF/LO 设频（频段命令之前）
                _rf.SetFrequency(rfFreqHz);
                _lo.SetFrequency(loFreqHz);
                await Task.Delay(RfLoStableMs, ct).ConfigureAwait(false);

                // 2) 频段命令
                _receiver.SendBandCommand(ReceiverMode.Mode3, band.BandIndex);
                await Task.Delay(BandStableMs, ct).ConfigureAwait(false);

                // 3) 衰减命令（32dB）——在频段命令之后
                //    先归零再设 32dB：避免上一频点的 32dB 残留影响本频点读数。
                _receiver.SendAttenuationCommand(_channel, 0);
                await Task.Delay(AttenStableMs, ct).ConfigureAwait(false);
                _receiver.SendAttenuationCommand(_channel, AttenuationDb);
                await Task.Delay(AttenStableMs, ct).ConfigureAwait(false);

                // 4) 加了衰减后才切参考电平，让 −32dBm 的 IF 信号浮到屏中部
                _sa.SetReferenceLevel(SaReferenceLevelAttenuatedDbm);
                await Task.Delay(AttenStableMs, ct).ConfigureAwait(false);

                // 5) 读 peak
                double peakDbm = _sa.ReadPeakAmplitude();

                // 6) 观察延时：读 mark 值后先停留一下，便于在频谱仪屏幕上核对 marker 读数
                Logger.Log($"[GainInconsistencyAgcOn] {band.DisplayName}: 观察 mark 读数 {MarkObserveMs}ms…");
                await Task.Delay(MarkObserveMs, ct).ConfigureAwait(false);

                // 7) 读完 mark 值后关闭 marker，避免带着开着的 mark 进入下一频点。
                //    best-effort：关 mark 失败只记日志，不影响本频点增益判定。
                try { _sa.SetAllMarkOFF(); } catch { /* best-effort */ }

                // 8) 读完后把参考电平切回 10dBm（不影响本频点读数，供下一频点用）
                _sa.SetReferenceLevel(SaReferenceLevelDbm);

                double gain = peakDbm - RfPowerMode3Dbm;

                // 记录 peak 原始读数——便于与频谱仪屏幕读数直接核对
                // （报表「测试值」是 gain，不是 peak）。
                Logger.Log(string.Format(
                    CultureInfo.InvariantCulture,
                    "[GainInconsistencyAgcOn] {0}: peak={1:F2}dBm, RF={2:F0}dBm, 带衰减增益={3:F2}dB",
                    band.DisplayName, peakDbm, RfPowerMode3Dbm, gain));

                await Task.Delay(PostReadDelayMs, ct).ConfigureAwait(false);
                return gain;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                string err = $"{band.DisplayName} 读 peak 失败：{ex.Message}";
                progress.Report("[" + ItemName + "] " + err);
                Logger.Log("[GainInconsistencyAgcOn] " + err);
                // 读数失败时也要把参考电平切回去、关闭 marker，避免污染下一频点
                try { _sa.SetReferenceLevel(SaReferenceLevelDbm); } catch { /* best-effort */ }
                try { _sa.SetAllMarkOFF(); } catch { /* best-effort */ }
                return double.NaN;
            }
        }

        /// <summary>
        /// 按频段显示名查找 BandInfo。找不到返回 null。
        /// </summary>
        private static ModeBandTable.BandInfo FindBand(ReceiverMode mode, string bandDisplayName)
        {
            foreach (var b in ModeBandTable.GetBands(mode))
            {
                if (string.Equals(b.DisplayName, bandDisplayName, StringComparison.Ordinal))
                    return b;
            }
            return null;
        }
    }
}
