using System;
using System.Globalization;

namespace A205AutoTestSystem.InstrumentControl
{
    /// <summary>
    /// LO 本振信号源控制类。独立类，预留 LO 仪表特有的命令差异（如双工混频模式、PLL 设置）。
    /// <para>M4 阶段：与 <see cref="SignalGenerator"/> 共享同一组 SCPI 命令；
    /// 协议到位 / 型号明确后再扩展 LO 专用方法。</para>
    /// <para>SCPI 命令定义来自 <c>rules/SCPI仪表控制命令与流程总结.md</c> 第 2.2 节。</para>
    /// </summary>
    public class SignalGeneratorLO : VisaBaseInstrument
    {
        public SignalGeneratorLO(string address) : base(address) { }

        /// <summary>设置 LO 频率（Hz）。按文档命令格式，转换为 MHz 字符串发出。</summary>
        public void SetFrequency(double freqHz)
        {
            double freqMHz = freqHz / 1e6;
            SendCommand(string.Format(CultureInfo.InvariantCulture, ":FREQ {0} MHz", freqMHz));
        }

        /// <summary>设置 LO 输出功率（dBm）。</summary>
        public void SetPower(double powerDbm)
        {
            SendCommand(string.Format(CultureInfo.InvariantCulture, ":POW {0} dBm", powerDbm));
        }

        /// <summary>设置 LO 输出开关。</summary>
        public void SetOutputState(bool on)
        {
            SendCommand(string.Format(CultureInfo.InvariantCulture, ":OUTP {0}", on ? 1 : 0));
        }

        /// <summary>读取状态字节。</summary>
        public string GetStatus()
        {
            return QueryString("*STB?");
        }

        // ====================================================================
        // 以下方法来自 rules/SCPI仪表控制命令与流程总结.md 第 2.2 节
        // ====================================================================

        /// <summary>设置调制类型（AM/FM/PM 等）。</summary>
        public void SetModulationType(string modulationType)
        {
            SendCommand(string.Format(CultureInfo.InvariantCulture, ":MOD:TYPE {0}", modulationType));
        }

        /// <summary>启用/禁用调制。</summary>
        public void EnableModulation(bool enable)
        {
            SendCommand(string.Format(CultureInfo.InvariantCulture, ":MOD:STAT {0}", enable ? 1 : 0));
        }

        /// <summary>启用/禁用脉冲调制。</summary>
        public void SetPulseModulation(bool enable)
        {
            SendCommand(string.Format(CultureInfo.InvariantCulture, ":PULM:STAT {0}", enable ? 1 : 0));
        }

        /// <summary>切换到扫频模式。</summary>
        public void SetFrequencyModeSweep()
        {
            SendCommand(":FREQ:MODE SWE");
        }

        /// <summary>设置扫频起始频率（Hz）。</summary>
        public void SetSweepStartFrequency(double frequencyHz)
        {
            SendCommand(string.Format(CultureInfo.InvariantCulture, ":FREQ:STAR {0}", frequencyHz));
        }

        /// <summary>设置扫频终止频率（Hz）。</summary>
        public void SetSweepStopFrequency(double frequencyHz)
        {
            SendCommand(string.Format(CultureInfo.InvariantCulture, ":FREQ:STOP {0}", frequencyHz));
        }

        /// <summary>设置扫频点数。</summary>
        public void SetSweepPoints(int points)
        {
            SendCommand(string.Format(CultureInfo.InvariantCulture, ":SWE:POIN {0}", points));
        }

        /// <summary>启用/禁用连续扫频。</summary>
        public void SetContinuousSweep(bool enable)
        {
            SendCommand(string.Format(CultureInfo.InvariantCulture, ":INIT:CONT {0}", enable ? 1 : 0));
        }
    }
}