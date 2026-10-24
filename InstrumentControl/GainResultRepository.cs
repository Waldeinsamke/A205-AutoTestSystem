using System;
using System.Collections.Generic;
using System.Globalization;
using A205AutoTestSystem.Common;

namespace A205AutoTestSystem.InstrumentControl
{
    /// <summary>
    /// 线性最大增益结果仓库（进程内）。
    /// <para>用途：供「通道间增益不一致性」这类**纯计算型**测试项取用
    /// 线性最大增益的实测数据，避免为了算一个派生指标而重复跑一遍硬件。</para>
    /// <para>键的构成：<c>通道号 × 模式名 × 频段显示名</c>。</para>
    /// <list type="bullet">
    ///   <item>按通道分键，因此切通道不清空——切回通道1 无需重跑线性最大增益。</item>
    ///   <item>切温度档 / 点「加载测试」时清空，避免跨温度档或跨批次用到旧数据。</item>
    /// </list>
    /// <para>线程安全：所有公共方法用 <see cref="_lock"/> 串行化
    /// （测试项在后台线程写、UI 线程读）。</para>
    /// </summary>
    public static class GainResultRepository
    {
        private static readonly object _lock = new object();

        // 键格式："通道号|模式名|频段显示名"
        private static readonly Dictionary<string, double> _gains =
            new Dictionary<string, double>(StringComparer.Ordinal);

        /// <summary>
        /// 记录一条线性最大增益实测值。
        /// </summary>
        /// <param name="channel">通道号（1~8）。</param>
        /// <param name="mode">工作模式名（如 "Mode1"）。</param>
        /// <param name="bandDisplayName">频段显示名（如 "频段108"）。</param>
        /// <param name="gain">增益实测值（dB）。</param>
        public static void Record(int channel, string mode, string bandDisplayName, double gain)
        {
            if (string.IsNullOrEmpty(mode) || string.IsNullOrEmpty(bandDisplayName)) return;
            if (double.IsNaN(gain) || double.IsInfinity(gain)) return;

            string key = BuildKey(channel, mode, bandDisplayName);
            lock (_lock)
            {
                _gains[key] = gain;
            }
            Logger.Log(string.Format(
                CultureInfo.InvariantCulture,
                "[GainRepo] 记录增益：ch{0} {1} {2} = {3:F2} dB",
                channel, mode, bandDisplayName, gain));
        }

        /// <summary>
        /// 读取某通道某模式某频段的增益实测值。
        /// </summary>
        /// <returns>有数据返回 true 并输出增益值；无数据返回 false。</returns>
        public static bool TryGet(int channel, string mode, string bandDisplayName, out double gain)
        {
            gain = 0;
            if (string.IsNullOrEmpty(mode) || string.IsNullOrEmpty(bandDisplayName)) return false;

            string key = BuildKey(channel, mode, bandDisplayName);
            lock (_lock)
            {
                return _gains.TryGetValue(key, out gain);
            }
        }

        /// <summary>
        /// 当前已记录的增益数据条数（用于诊断"缺哪些数据"）。
        /// </summary>
        public static int Count
        {
            get
            {
                lock (_lock)
                {
                    return _gains.Count;
                }
            }
        }

        /// <summary>
        /// 清空仓库。切温度档 / 点「加载测试」时调用。
        /// </summary>
        public static void Clear()
        {
            lock (_lock)
            {
                if (_gains.Count == 0) return;
                int n = _gains.Count;
                _gains.Clear();
                Logger.Log("[GainRepo] 已清空增益仓库（" + n + " 条）。");
            }
        }

        private static string BuildKey(int channel, string mode, string bandDisplayName)
        {
            return channel.ToString(CultureInfo.InvariantCulture) + "|"
                 + mode.Trim() + "|"
                 + bandDisplayName.Trim();
        }
    }
}
