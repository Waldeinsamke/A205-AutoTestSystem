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
    /// 通道间增益不一致性（AGC 不起控）。
    /// <para><b>纯计算型测试项</b>：不碰任何硬件，全部结果由
    /// <see cref="GainResultRepository"/> 中已记录的线性最大增益实测值派生。</para>
    /// <para><b>前置条件</b>：当前选中通道的线性最大增益必须已测完
    /// （本指标清单内的频点都要有数据）。缺数据时该模式记 Fail 并在备注里
    /// 说明缺哪个频点，不影响其它模式。</para>
    /// <para><b>算法</b>（每个模式独立算，共 3 条结果，与
    /// <see cref="GainConsistencyCalculator"/> 共用同一套判定逻辑）：</para>
    /// <list type="number">
    ///   <item>取本模式清单内各频点的线性最大增益实测值。</item>
    ///   <item><b>输出值 = 最差值</b>：距该模式增益规格边界最近的那个值。
    ///     Mode1 规格 [18,22] / Mode2 [9,11] / Mode3 [18.5,21.5]。
    ///     例：18.5 与 21.3 相比，18.5 离边界 0.5、21.3 离边界 0.7 → 18.5 更差。</item>
    ///   <item><b>Pass/Fail = 极差</b>：本模式清单内 <c>max − min</c>，
    ///     ≤ 1 dB 判 Pass，否则 Fail。
    ///     例：18.5/18.6/18.9 → 最差值 18.5，极差 0.4 → Pass；
    ///     20.5/19.5/21.5 → 最差值 21.5，极差 2.0 → Fail。</item>
    /// </list>
    /// </summary>
    public class ChannelGainInconsistencyTest : ITestItem
    {
        /// <summary>测试项目名称。</summary>
        public const string ItemName = "通道间增益不一致性（AGC不起控）";

        /// <summary>极差判据上限（dB）。max−min ≤ 1 dB 判 Pass。</summary>
        private const double RangeLimitDb = GainConsistencyCalculator.RangeLimitDb;

        /// <summary>
        /// 本指标参与计算的频点清单（MHz）。
        /// <para>Mode1 的 220 对应频段表里的「频段220」（BandIndex 7 / BN 0x07）；
        /// Mode3 清单为 900/1300/1500/1800/2100/2500/2700/3000/3300
        /// （产品手册原文写的 1200 / 2400 在 Mode3 频段表里不存在，
        /// 已与需求方确认分别改为 1300 / 2500）。</para>
        /// </summary>
        private static readonly Dictionary<ReceiverMode, int[]> _keyPoints =
            new Dictionary<ReceiverMode, int[]>
            {
                { ReceiverMode.Mode1, new[] { 108, 220, 400 } },
                { ReceiverMode.Mode2, new[] { 960, 1224, 1600 } },
                { ReceiverMode.Mode3, new[] { 900, 1300, 1500, 1800, 2100, 2500, 2700, 3000, 3300 } },
            };

        private readonly int _channel;
        private readonly IReadOnlyList<ReceiverMode> _modes;

        /// <summary>
        /// 构造。
        /// </summary>
        /// <param name="channel">当前选中通道号（1~8）。</param>
        /// <param name="modes">要计算的模式列表；默认 [Mode1, Mode2, Mode3]。</param>
        public ChannelGainInconsistencyTest(int channel, IReadOnlyList<ReceiverMode> modes = null)
        {
            if (channel < 1 || channel > 8)
                throw new ArgumentOutOfRangeException(nameof(channel), channel, "通道号必须在 1~8 范围内。");

            _channel = channel;
            _modes = modes ?? new ReceiverMode[]
            {
                ReceiverMode.Mode1,
                ReceiverMode.Mode2,
                ReceiverMode.Mode3
            };
            if (_modes.Count == 0)
                throw new ArgumentException("必须至少传入一个工作模式。", nameof(modes));

            // 校验所有模式都有对应的频点清单——否则运行期会抛 KeyNotFoundException。
            foreach (ReceiverMode m in _modes)
            {
                if (!_keyPoints.ContainsKey(m))
                    throw new ArgumentOutOfRangeException(nameof(modes), m, "该模式没有配置参与计算的频点清单。");
            }
        }

        public string TestName => ItemName;

        /// <summary>单位 dB。</summary>
        public string Unit => "dB";

        /// <summary>
        /// 判据下限。实际判据是"极差 ≤ 1 dB"，极差恒 ≥ 0，故下限取 null。
        /// </summary>
        public double? LowerLimit => null;

        /// <summary>
        /// 判据上限：极差上限 1.0 dB。
        /// <para>注意：输出到报表的 Value 是"最差增益值"，不是极差；
        /// Pass/Fail 由极差决定，因此 DGV 阈值列显示的 [−∞, 1.00] 是真实判据。</para>
        /// </summary>
        public double? UpperLimit => RangeLimitDb;

        public Task<TestResult> ExecuteAsync(IProgress<string> progress, CancellationToken ct)
        {
            if (progress == null) throw new ArgumentNullException(nameof(progress));
            ct.ThrowIfCancellationRequested();

            var results = new List<TestResult>();
            foreach (ReceiverMode mode in _modes)
            {
                ct.ThrowIfCancellationRequested();
                progress.Report($"[{ItemName}] 计算 {mode}…");
                TestResult r = ComputeOneMode(mode);
                results.Add(r);

                // 逐模式即时写入报表（与 GainTest 的逐频点记录方式一致）：
                // 中途被取消时，已算出的模式结果不丢失。
                TestReportManager.Instance.Record(r);
            }

            if (results.Count == 0)
            {
                throw new InvalidOperationException("通道间增益不一致性没有可计算的模式。");
            }

            // 返回给 UI 状态列显示的结果：
            // 任一模式 Fail → 返回**第一个失败模式**的结果（带上它的频段与数值），
            // 避免"只显示最后一个模式"掩盖其它模式的失败；
            // 全部 Pass → 返回最后一个模式的结果。
            TestResult failed = null;
            foreach (TestResult r in results)
            {
                if (r.Judgment != "Pass") { failed = r; break; }
            }
            if (failed != null)
            {
                return Task.FromResult(new TestResult
                {
                    TestTime = failed.TestTime,
                    Channel = failed.Channel,
                    Mode = failed.Mode,
                    BandIndex = failed.BandIndex,
                    BandDisplayName = failed.BandDisplayName,
                    TestItem = TestName,
                    Value = failed.Value,
                    Unit = Unit,
                    Judgment = "Fail",
                    Remarks = BuildSummaryRemarks(results, failed)
                });
            }
            return Task.FromResult(results[results.Count - 1]);
        }

        /// <summary>
        /// 汇总各模式结果为一条备注（存在失败模式时使用）。
        /// </summary>
        private static string BuildSummaryRemarks(List<TestResult> results, TestResult failed)
        {
            var parts = new List<string>();
            foreach (TestResult r in results)
            {
                string band = string.IsNullOrEmpty(r.BandDisplayName) ? "—" : r.BandDisplayName;
                parts.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}:{1}({2})", r.Mode, band, r.Judgment));
            }
            string failedDetail = string.IsNullOrEmpty(failed.Remarks)
                ? string.Empty
                : "；失败详情：" + failed.Remarks;
            return "汇总：" + string.Join("、", parts.ToArray()) + failedDetail;
        }

        /// <summary>
        /// 计算单个模式的最差值与极差，生成一条 TestResult。
        /// </summary>
        private TestResult ComputeOneMode(ReceiverMode mode)
        {
            string modeName = mode.ToString();
            var points = _keyPoints[mode];

            // 1) 取本模式清单内各频点的增益实测值
            var gains = new List<double>();
            var bands = new List<string>();
            var missing = new List<string>();

            foreach (int mhz in points)
            {
                string bandName = "频段" + mhz.ToString(CultureInfo.InvariantCulture);
                double gain;
                if (GainResultRepository.TryGet(_channel, modeName, bandName, out gain))
                {
                    gains.Add(gain);
                    bands.Add(bandName);
                }
                else
                {
                    missing.Add(bandName);
                }
            }

            // 2) 缺数据：该模式记 Fail，备注说明缺哪个频点
            if (missing.Count > 0 || gains.Count == 0)
            {
                string missingText = missing.Count > 0
                    ? "缺少线性最大增益数据：" + string.Join("、", missing.ToArray())
                    : "缺少线性最大增益数据";
                string remark = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}，无法计算（请先在『自动测试』页运行该通道的『线性最大增益』）", missingText);
                Logger.Log("[GainInconsistency] " + modeName + " " + remark);

                return new TestResult
                {
                    TestTime = DateTime.Now,
                    Channel = _channel,
                    Mode = modeName,
                    BandIndex = null,
                    BandDisplayName = string.Empty,
                    TestItem = TestName,
                    Value = double.NaN,
                    Unit = Unit,
                    Judgment = "Fail",
                    Remarks = remark
                };
            }

            // 3) 输出值 = 最差值（距规格边界最近者），判定 = 极差 ≤ 1 dB
            var limits = GainTest.GetGainLimits(mode);
            var comparison = GainConsistencyCalculator.Compare(bands, gains, limits.Lower, limits.Upper);

            string remarks = string.Format(
                CultureInfo.InvariantCulture,
                "最差值={0:F2}dB@{1}(距边界{2:F2}dB), 极差={3:F2}dB(判据≤{4:F1}dB), 各频点增益: {5}",
                comparison.WorstValue, comparison.WorstBand, comparison.WorstDistance,
                comparison.Range, GainConsistencyCalculator.RangeLimitDb,
                GainConsistencyCalculator.FormatGainDetails(bands, gains));

            Logger.Log("[GainInconsistency] " + modeName + " " + remarks);

            return new TestResult
            {
                TestTime = DateTime.Now,
                Channel = _channel,
                Mode = modeName,
                BandIndex = FindBandIndex(mode, comparison.WorstBand),
                BandDisplayName = comparison.WorstBand,
                TestItem = TestName,
                Value = comparison.WorstValue,
                Unit = Unit,
                Judgment = comparison.RangePass ? "Pass" : "Fail",
                Remarks = remarks
            };
        }

        /// <summary>
        /// 按频段显示名反查 BandIndex（用于报表溯源）。查不到返回 null。
        /// </summary>
        private static int? FindBandIndex(ReceiverMode mode, string bandDisplayName)
        {
            foreach (var band in ModeBandTable.GetBands(mode))
            {
                if (string.Equals(band.DisplayName, bandDisplayName, StringComparison.Ordinal))
                    return band.BandIndex;
            }
            return null;
        }
    }
}
