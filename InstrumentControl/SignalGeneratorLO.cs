using System;
using System.Globalization;

namespace A205AutoTestSystem.InstrumentControl
{
    /// <summary>
    /// LO 本振信号源控制类。独立类，预留 LO 仪表特有的命令差异（如双工混频模式、PLL 设置）。
    /// <para>M4 阶段：与 <see cref="SignalGenerator"/> 共享同一组 SCPI 命令；
    /// 协议到位 / 型号明确后再扩展 LO 专用方法。</para>
    /// </summary>
    public class SignalGeneratorLO : VisaBaseInstrument
    {
        public SignalGeneratorLO(string address) : base(address) { }

        /// <summary>设置 LO 频率（Hz）。</summary>
        public void SetFrequency(double freqHz)
        {
            SendCommand(string.Format(CultureInfo.InvariantCulture, "FREQ {0}", freqHz));
        }

        /// <summary>设置 LO 输出功率（dBm）。</summary>
        public void SetPower(double powerDbm)
        {
            SendCommand(string.Format(CultureInfo.InvariantCulture, "POW {0}", powerDbm));
        }

        /// <summary>设置 LO 输出开关。</summary>
        public void SetOutputState(bool on)
        {
            SendCommand(on ? "OUTP ON" : "OUTP OFF");
        }

        /// <summary>读取状态字节。</summary>
        public string GetStatus()
        {
            return QueryString("*STB?");
        }
    }
}