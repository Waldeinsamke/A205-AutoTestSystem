using System;

namespace A205AutoTestSystem.InstrumentControl
{
    /// <summary>
    /// 单条测试结果数据模型。字段对齐规划书 §6.1。
    /// </summary>
    public class TestResult
    {
        /// <summary>测试执行的时间戳。</summary>
        public DateTime TestTime { get; set; } = DateTime.Now;

        /// <summary>温度区间（常温 / 高温 / 低温）。</summary>
        public string TemperatureRange { get; set; } = string.Empty;

        /// <summary>通道号（1~8）。</summary>
        public int Channel { get; set; }

        /// <summary>工作模式（Mode1 / Mode2 / Mode3）。</summary>
        public string Mode { get; set; } = string.Empty;

        /// <summary>测试项目名称。</summary>
        public string TestItem { get; set; } = string.Empty;

        /// <summary>实际测量值。</summary>
        public double Value { get; set; }

        /// <summary>测量单位（dB / MHz 等）。</summary>
        public string Unit { get; set; } = string.Empty;

        /// <summary>判定结果（Pass / Fail）。</summary>
        public string Judgment { get; set; } = string.Empty;

        /// <summary>可选的附加信息。</summary>
        public string Remarks { get; set; } = string.Empty;
    }
}