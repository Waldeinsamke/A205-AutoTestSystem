using System;

namespace A205AutoTestSystem.InstrumentControl
{
    /// <summary>
    /// 本振信号频率计算规则（LO 跟随 RF 动态计算）。
    /// <para>工作模式与 RF→LO 偏移：</para>
    /// <list type="bullet">
    /// <item>Mode1 / Mode2：<c>LO = RF + 3670 MHz</c></item>
    /// <item>Mode3：<c>LO = RF + 4350 MHz</c></item>
    /// </list>
    /// <para>示例：</para>
    /// <list type="bullet">
    /// <item>Mode1，RF=108 MHz → LO=3778 MHz</item>
    /// <item>Mode3，RF=800 MHz → LO=5150 MHz</item>
    /// </list>
    /// </summary>
    public static class LoFrequencyRule
    {
        /// <summary>Mode1 / Mode2 的 RF→LO 偏移（Hz）。</summary>
        public const double OffsetMode12Hz = 3_670_000_000.0;

        /// <summary>Mode3 的 RF→LO 偏移（Hz）。</summary>
        public const double OffsetMode3Hz = 4_350_000_000.0;

        /// <summary>Mode1 的整数值（用于 <see cref="Calculate(double,int)"/>）。</summary>
        public const int Mode1 = 1;

        /// <summary>Mode2 的整数值。</summary>
        public const int Mode2 = 2;

        /// <summary>Mode3 的整数值。</summary>
        public const int Mode3 = 3;

        /// <summary>
        /// 根据 RF 频率（Hz）和工作模式计算 LO 频率（Hz）。
        /// </summary>
        /// <param name="rfFreqHz">射频信号频率（Hz）。</param>
        /// <param name="mode">工作模式，1 / 2 / 3。</param>
        /// <returns>本振信号频率（Hz）。</returns>
        /// <exception cref="ArgumentOutOfRangeException">传入未定义偏移规则的工作模式。</exception>
        public static double Calculate(double rfFreqHz, int mode)
        {
            switch (mode)
            {
                case Mode1:
                case Mode2:
                    return rfFreqHz + OffsetMode12Hz;
                case Mode3:
                    return rfFreqHz + OffsetMode3Hz;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(mode), mode, "未定义 LO 偏移规则的工作模式（应为 1/2/3）。");
            }
        }
    }
}