using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace A205AutoTestSystem.InstrumentControl.TestItems
{
    /// <summary>
    /// 通道间增益不一致性的共用比对算法。
    /// <para>被「通道间增益不一致性（AGC不起控）」与「通道间增益不一致性（AGC起控）」
    /// 两个测试项共用，保证两者的判定口径完全一致。</para>
    /// <para><b>算法</b>：</para>
    /// <list type="number">
    ///   <item><b>输出值 = 最差值</b>：距等效增益规格边界最近的那个值。
    ///     例（规格 [18,22]）：18.5 离边界 0.5、21.3 离边界 0.7 → 18.5 更差。</item>
    ///   <item><b>Pass/Fail = 极差</b>：<c>max − min</c>，≤ 1 dB 判 Pass。
    ///     例：18.5/18.6/18.9 → 最差 18.5，极差 0.4 → Pass；
    ///     20.5/19.5/21.5 → 最差 21.5，极差 2.0 → Fail。</item>
    /// </list>
    /// <para>距离统一用 <c>min(|v−lower|, |v−upper|)</c> 计算，
    /// 这样"最靠近边界的值 = 最差值"在规格区间内外都成立。</para>
    /// </summary>
    public static class GainConsistencyCalculator
    {
        /// <summary>极差判据上限（dB）。max−min ≤ 1 dB 判 Pass。</summary>
        public const double RangeLimitDb = 1.0;

        /// <summary>一组增益值的比对结果。</summary>
        public sealed class Comparison
        {
            /// <summary>参与比对的频点（频段显示名），与 <see cref="Gains"/> 一一对应。</summary>
            public IReadOnlyList<string> Bands { get; }

            /// <summary>参与比对的原始增益值（dB），与 <see cref="Bands"/> 一一对应。</summary>
            public IReadOnlyList<double> Gains { get; }

            /// <summary>最差值的索引（指向 <see cref="Bands"/> / <see cref="Gains"/>）。</summary>
            public int WorstIndex { get; }

            /// <summary>最差频段的显示名。</summary>
            public string WorstBand => Bands[WorstIndex];

            /// <summary>最差值（原始单位，dB）。</summary>
            public double WorstValue => Gains[WorstIndex];

            /// <summary>最差值到规格边界的距离（dB）。</summary>
            public double WorstDistance { get; }

            /// <summary>各频点增益的极差（dB）。</summary>
            public double Range { get; }

            /// <summary>极差判定结果：Range ≤ 1 dB → Pass。</summary>
            public bool RangePass => Range <= RangeLimitDb;

            public Comparison(IReadOnlyList<string> bands, IReadOnlyList<double> gains,
                              int worstIndex, double worstDistance, double range)
            {
                Bands = bands;
                Gains = gains;
                WorstIndex = worstIndex;
                WorstDistance = worstDistance;
                Range = range;
            }
        }

        /// <summary>
        /// 比对一组增益值，求最差值与极差。
        /// </summary>
        /// <param name="bands">频段显示名列表（与 gains 一一对应）。</param>
        /// <param name="gains">待比对的等效增益值（dB）。</param>
        /// <param name="lower">等效增益规格下限（dB）。</param>
        /// <param name="upper">等效增益规格上限（dB）。</param>
        /// <exception cref="ArgumentException">两个列表长度不一致或为空时抛出。</exception>
        public static Comparison Compare(
            IReadOnlyList<string> bands,
            IReadOnlyList<double> gains,
            double lower,
            double upper)
        {
            if (bands == null) throw new ArgumentNullException(nameof(bands));
            if (gains == null) throw new ArgumentNullException(nameof(gains));
            if (bands.Count != gains.Count)
                throw new ArgumentException("频段列表与增益列表长度必须一致。");
            if (gains.Count == 0)
                throw new ArgumentException("参与比对的增益值不能为空。");

            int worstIndex = 0;
            double worstDistance = DistanceToRange(gains[0], lower, upper);
            for (int i = 1; i < gains.Count; i++)
            {
                double d = DistanceToRange(gains[i], lower, upper);
                if (d < worstDistance)
                {
                    worstDistance = d;
                    worstIndex = i;
                }
            }

            double range = gains.Max() - gains.Min();
            return new Comparison(bands, gains, worstIndex, worstDistance, range);
        }

        /// <summary>
        /// 判断单个增益值是否落在规格区间内（用于频点行的判定列）。
        /// </summary>
        public static bool IsInRange(double value, double lower, double upper)
        {
            return value >= lower && value <= upper;
        }

        /// <summary>
        /// 生成各频点增益明细文本（写入备注）。
        /// </summary>
        public static string FormatGainDetails(IReadOnlyList<string> bands, IReadOnlyList<double> gains)
        {
            var parts = new List<string>(bands.Count);
            for (int i = 0; i < bands.Count; i++)
            {
                parts.Add(bands[i] + "=" + gains[i].ToString("F2", CultureInfo.InvariantCulture));
            }
            return string.Join("、", parts.ToArray());
        }

        /// <summary>
        /// 增益值到规格区间的最近边界距离（dB）。
        /// <para>统一用 <c>min(|v−lower|, |v−upper|)</c> 计算：
        /// 区间内的值取到较近边界的距离，区间外的值取到最近边界的距离。</para>
        /// </summary>
        public static double DistanceToRange(double value, double lower, double upper)
        {
            double d1 = Math.Abs(value - lower);
            double d2 = Math.Abs(value - upper);
            return d1 < d2 ? d1 : d2;
        }
    }
}
