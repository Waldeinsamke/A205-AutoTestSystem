using System;
using System.Globalization;
using A205AutoTestSystem.Common;

namespace A205AutoTestSystem.InstrumentControl
{
    /// <summary>
    /// RF 射频信号源控制类。面向 Keysight / R&amp;S 等支持 SCPI 的通用信号源。
    /// 默认 SCPI 命令参考通用约定：
    /// <list type="bullet">
    /// <item><c>FREQ &lt;Hz&gt;</c> 设置频率</item>
    /// <item><c>POW &lt;dBm&gt;</c> 设置功率</item>
    /// <item><c>OUTP ON|OFF</c> 射频输出开关</item>
    /// <item><c>*STB?</c> 读取状态字节</item>
    /// </list>
    /// 真实仪表接入时如命令不一致，在本类替换为型号专用命令即可。
    /// </summary>
    public class SignalGenerator : VisaBaseInstrument
    {
        public SignalGenerator(string address) : base(address) { }

        /// <summary>设置载波频率（Hz）。</summary>
        public void SetFrequency(double freqHz)
        {
            SendCommand(string.Format(CultureInfo.InvariantCulture, "FREQ {0}", freqHz));
        }

        /// <summary>设置输出功率（dBm）。</summary>
        public void SetPower(double powerDbm)
        {
            SendCommand(string.Format(CultureInfo.InvariantCulture, "POW {0}", powerDbm));
        }

        /// <summary>设置射频输出开关。</summary>
        public void SetOutputState(bool on)
        {
            SendCommand(on ? "OUTP ON" : "OUTP OFF");
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
    }
}