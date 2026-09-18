using System;
using System.Globalization;
using A205AutoTestSystem.Common;

namespace A205AutoTestSystem.InstrumentControl
{
    /// <summary>
    /// RF 射频信号源控制类。面向 Keysight / R&amp;S 等支持 SCPI 的通用信号源。
    /// SCPI 命令定义来自 <c>rules/SCPI仪表控制命令与流程总结.md</c> 第 2.2 节：
    /// <list type="bullet">
    /// <item><c>:FREQ &lt;MHz&gt;</c> 设置频率（Hz 入参，内部转 MHz 打印）</item>
    /// <item><c>:POW &lt;dBm&gt;</c> 设置功率</item>
    /// <item><c>:OUTP 1|0</c> 射频输出开关</item>
    /// <item><c>:MOD:TYPE / :MOD:STAT / :PULM:STAT</c> 调制相关</item>
    /// <item><c>:FREQ:MODE SWE / :FREQ:STAR / :FREQ:STOP / :SWE:POIN / :INIT:CONT</c> 扫频相关</item>
    /// </list>
    /// </summary>
    public class SignalGenerator : VisaBaseInstrument
    {
        public SignalGenerator(string address) : base(address) { }

        /// <summary>设置载波频率（Hz）。按文档命令格式，转换为 MHz 字符串发出。</summary>
        public void SetFrequency(double freqHz)
        {
            double freqMHz = freqHz / 1e6;
            SendCommand(string.Format(CultureInfo.InvariantCulture, ":FREQ {0} MHz", freqMHz));
        }

        /// <summary>设置输出功率（dBm）。</summary>
        public void SetPower(double powerDbm)
        {
            SendCommand(string.Format(CultureInfo.InvariantCulture, ":POW {0} dBm", powerDbm));
        }

        /// <summary>设置射频输出开关。</summary>
        public void SetOutputState(bool on)
        {
            SendCommand(string.Format(CultureInfo.InvariantCulture, ":OUTP {0}", on ? 1 : 0));
        }

        /// <summary>读取状态字节。</summary>
        public string GetStatus()
        {
            return QueryString("*STB?");
        }

        /// <summary>
        /// 便捷方法：一次性配置频率 + 功率 + 输出开启。
        /// 常用于自动化测试用例的"准备阶段"。
        /// </summary>
        public void ConfigureAndEnable(double freqHz, double powerDbm)
        {
            SetFrequency(freqHz);
            SetPower(powerDbm);
            SetOutputState(true);
            Logger.Log(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "[SignalGenerator] configured {0} Hz @ {1} dBm, output ON",
                    freqHz,
                    powerDbm));
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