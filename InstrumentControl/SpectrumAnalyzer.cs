using System;
using System.Globalization;

namespace A205AutoTestSystem.InstrumentControl
{
    /// <summary>
    /// 频谱仪控制类。面向 Keysight / R&amp;S FSV 等支持 SCPI 的频谱分析仪。
    /// 默认 SCPI 命令参考通用约定：
    /// <list type="bullet">
    /// <item><c>FREQ:CENT &lt;Hz&gt;</c> 中心频率</item>
    /// <item><c>FREQ:SPAN &lt;Hz&gt;</c> 频宽</item>
    /// <item><c>BAND:RES &lt;Hz&gt;</c> 分辨率带宽（RBW）</item>
    /// <item><c>CALC:MARK:MAX</c> 标记峰值（搜索）</item>
    /// <item><c>CALC:MARK:Y?</c> 读取标记 Y 值（dBm）</item>
    /// </list>
    /// 真实仪表型号接入后，命令不一致时在本类替换。
    /// </summary>
    public class SpectrumAnalyzer : VisaBaseInstrument
    {
        public SpectrumAnalyzer(string address) : base(address) { }

        public void SetCenterFrequency(double freqHz)
        {
            SendCommand(string.Format(CultureInfo.InvariantCulture, "FREQ:CENT {0}", freqHz));
        }

        public void SetSpan(double spanHz)
        {
            SendCommand(string.Format(CultureInfo.InvariantCulture, "FREQ:SPAN {0}", spanHz));
        }

        public void SetRbw(double rbwHz)
        {
            SendCommand(string.Format(CultureInfo.InvariantCulture, "BAND:RES {0}", rbwHz));
        }

        /// <summary>在当前 span 内搜索峰值，并将 Marker 移到该峰值处。</summary>
        public void SearchPeak()
        {
            SendCommand("CALC:MARK:MAX");
        }

        /// <summary>读取 Marker 当前位置的 Y 值（dBm）。</summary>
        public double ReadMarkerAmplitude()
        {
            string resp = QueryString("CALC:MARK:Y?");
            return double.Parse(resp, CultureInfo.InvariantCulture);
        }

        /// <summary>便捷方法：搜索峰值后读取幅值（dBm）。</summary>
        public double ReadPeakAmplitude()
        {
            SearchPeak();
            return ReadMarkerAmplitude();
        }
    }
}