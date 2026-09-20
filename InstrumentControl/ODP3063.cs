using System;
using System.Globalization;

namespace A205AutoTestSystem.InstrumentControl
{
    /// <summary>
    /// ODP3063 可编程直流电源控制类。只操作第二路通道（CH2）。
    /// SCPI 命令定义来自 <c>rules/SCPI仪表控制命令与流程总结.md</c> 第 2.4 节：
    /// <list type="bullet">
    /// <item><c>:SOUR2:VOLT</c> 设置 CH2 输出电压（V）</item>
    /// <item><c>:SOUR2:CURR</c> 设置 CH2 输出电流（A）</item>
    /// <item><c>:OUTP2:STAT 1|0</c> CH2 输出开关</item>
    /// <item><c>:OUTP2:STAT?</c> 查询 CH2 输出状态</item>
    /// <item><c>:SOUR2:VOLT:PROT / :SOUR2:CURR:PROT</c> 过压/过流保护</item>
    /// </list>
    /// </summary>
    public class ODP3063 : VisaBaseInstrument
    {
        public ODP3063(string address) : base(address) { }

        /// <summary>设置 CH2 输出电压（V）。</summary>
        public void SetChannel2Voltage(double voltage)
        {
            SendCommand(string.Format(CultureInfo.InvariantCulture, ":SOUR2:VOLT {0}", voltage));
        }

        /// <summary>设置 CH2 输出电流（A）。</summary>
        public void SetChannel2Current(double current)
        {
            SendCommand(string.Format(CultureInfo.InvariantCulture, ":SOUR2:CURR {0}", current));
        }

        /// <summary>启用/禁用 CH2 输出。</summary>
        public void EnableChannel2Output(bool enable)
        {
            SendCommand(string.Format(CultureInfo.InvariantCulture, ":OUTP2:STAT {0}", enable ? 1 : 0));
        }

        /// <summary>查询 CH2 输出状态（仪表原始返回字符串）。</summary>
        public string QueryChannel2OutputStatus()
        {
            return QueryString(":OUTP2:STAT?");
        }

        /// <summary>设置 CH2 过压保护值（V）。</summary>
        public void SetChannel2OverVoltageProtection(double voltage)
        {
            SendCommand(string.Format(CultureInfo.InvariantCulture, ":SOUR2:VOLT:PROT {0}", voltage));
        }

        /// <summary>设置 CH2 过流保护值（A）。</summary>
        public void SetChannel2OverCurrentProtection(double current)
        {
            SendCommand(string.Format(CultureInfo.InvariantCulture, ":SOUR2:CURR:PROT {0}", current));
        }
    }
}
