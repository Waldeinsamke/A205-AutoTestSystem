using System;
using System.Globalization;
using System.Threading;
using A205AutoTestSystem.Common;

namespace A205AutoTestSystem.InstrumentControl
{
    /// <summary>
    /// 频谱仪控制类。面向 Keysight / R&amp;S FSV / FSW 等支持 SCPI 的频谱分析仪。
    /// SCPI 命令定义来自 <c>rules/SCPI仪表控制命令与流程总结.md</c> 第 2.3 节：
    /// <list type="bullet">
    /// <item><c>:FREQ:CENT / :FREQ:SPAN</c> 中心频率/跨度（Hz 入参，内部转 MHz 打印）</item>
    /// <item><c>:BWID:RES / :BWID:VID</c> 分辨率带宽/视频带宽（kHz 单位）</item>
    /// <item><c>:CALC:MARK1:STAT / :CALC:MARK1:MAX:PEAK / :CALC:MARK1:Y?</c> Marker 峰值搜索与读值</item>
    /// <item><c>:INITiate:HARMonics / :FETCh:HARMonics:AMPLitude:ALL?</c> 谐波测试</item>
    /// </list>
    /// <para>说明：本类按用户要求<strong>不启用</strong> <c>EnableDelay</c>，不强制每条 Write 后 Sleep(100)。</para>
    /// </summary>
    public class SpectrumAnalyzer : VisaBaseInstrument
    {
        public SpectrumAnalyzer(string address) : base(address) { }

        /// <summary>设置中心频率（Hz）。按文档命令格式，转换为 MHz 字符串发出。</summary>
        public void SetCenterFrequency(double freqHz)
        {
            double freqMHz = freqHz / 1e6;
            SendCommand(string.Format(CultureInfo.InvariantCulture, ":FREQ:CENT {0} MHz", freqMHz));
        }

        /// <summary>设置频率跨度（Hz）。按文档命令格式，转换为 MHz 字符串发出。</summary>
        public void SetSpan(double spanHz)
        {
            double spanMHz = spanHz / 1e6;
            SendCommand(string.Format(CultureInfo.InvariantCulture, ":FREQ:SPAN {0} MHz", spanMHz));
        }

        /// <summary>设置起始频率（Hz）。按文档命令格式，转换为 MHz 字符串发出。
        /// 对应 SCPI 命令：<c>:FREQ:STAR</c>。用于带外抑制扫描
        /// （中频带外抑制测试需要避开 IF 中心 ±4 MHz 的"中频带"，分两段设 START/STOP）。</summary>
        public void SetStartFrequency(double freqHz)
        {
            double freqMHz = freqHz / 1e6;
            SendCommand(string.Format(CultureInfo.InvariantCulture, ":FREQ:STAR {0} MHz", freqMHz));
        }

        /// <summary>设置终止频率（Hz）。按文档命令格式，转换为 MHz 字符串发出。
        /// 对应 SCPI 命令：<c>:FREQ:STOP</c>。</summary>
        public void SetStopFrequency(double freqHz)
        {
            double freqMHz = freqHz / 1e6;
            SendCommand(string.Format(CultureInfo.InvariantCulture, ":FREQ:STOP {0} MHz", freqMHz));
        }

        /// <summary>设置分辨率带宽 RBW（kHz）。</summary>
        public void SetResolutionBandwidth(double bandwidthKHz)
        {
            SendCommand(string.Format(CultureInfo.InvariantCulture, ":BWID:RES {0} kHz", bandwidthKHz));
        }

        /// <summary>
        /// 兼容旧接口的便捷包装：接收 Hz 入参，按文档命令 <c>:BWID:RES kHz</c> 发出。
        /// <para>测试代码与早期调用方仍可使用 <c>SetRbw(1000)</c>，效果等价于 <c>SetResolutionBandwidth(1.0)</c>。</para>
        /// </summary>
        public void SetRbw(double bandwidthHz)
        {
            SetResolutionBandwidth(bandwidthHz / 1000.0);
        }

        /// <summary>
        /// 便捷方法：搜索峰值后读取幅值（dBm）。
        /// 按文档 2.3 节"峰值搜索并返回功率"流程：STAT ON → MAX:PEAK → MAX:PEAK:SEARCH → Y?
        /// </summary>
        public double ReadPeakAmplitude()
        {
            return MeasureMarkerPeak();
        }

        // ====================================================================
        // 频率与带宽
        // ====================================================================

        /// <summary>分辨率带宽设为自动。</summary>
        public void SetResolutionBandwidthAuto()
        {
            SendCommand(":BWID:RES:AUTO ON");
        }

        /// <summary>设置视频带宽 VBW（kHz）。</summary>
        public void SetVideoBandwidth(double bandwidthKHz)
        {
            SendCommand(string.Format(CultureInfo.InvariantCulture, ":BWID:VID {0} kHz", bandwidthKHz));
        }

        /// <summary>视频带宽设为自动。</summary>
        public void SetVideoBandwidthAuto()
        {
            SendCommand(":BWID:VID:AUTO ON");
        }

        /// <summary>同时设置中心频率与跨度（Hz）。</summary>
        public void SetFrequencySpan(double centerFrequencyHz, double spanHz)
        {
            SetCenterFrequency(centerFrequencyHz);
            SetSpan(spanHz);
        }

        // ====================================================================
        // 功率测量
        // ====================================================================

        /// <summary>
        /// Marker 峰值搜索并返回功率（dBm）。
        /// 文档 2.3 节：STAT ON → MAX:PEAK → MAX:PEAK:SEARCH → Y?
        /// </summary>
        public double MeasureMarkerPeak()
        {
            SendCommand(":CALC:MARK1:STAT ON");
            SendCommand(":CALC:MARK1:MAX:PEAK");
            SendCommand(":CALC:MARK1:MAX:PEAK:SEARCH");
            return ParseMarkerY(":CALC:MARK1:Y?");
        }

        /// <summary>
        /// 测量中频功率（dBm）。文档 2.3 节：FREQ:CENT 70 MHz（硬编码） +
        /// SWE:TIME:AUTO ON + STAT ON + MAX:PEAK + MAX:PEAK:SEARCH + Y?
        /// </summary>
        public double MeasurePower(double frequency, double bandwidth)
        {
            // 注：文档此方法硬编码中心频率为 70 MHz（项目 IF），frequency/bandwidth 参数保留以满足调用方约定。
            _ = frequency;
            _ = bandwidth;
            SendCommand(":FREQ:CENT 70 MHz");
            SendCommand(":SWE:TIME:AUTO ON");
            SendCommand(":CALC:MARK1:STAT ON");
            SendCommand(":CALC:MARK1:MAX:PEAK");
            SendCommand(":CALC:MARK1:MAX:PEAK:SEARCH");
            return ParseMarkerY(":CALC:MARK1:Y?");
        }

        /// <summary>直接读取当前 Marker 值（dBm）。文档：STAT ON → Y?</summary>
        public double ReadCurrentMarkerValue()
        {
            SendCommand(":CALC:MARK1:STAT ON");
            return ParseMarkerY(":CALC:MARK1:Y?");
        }

        /// <summary>直接读取 Marker1 的 Y 值（dBm）。文档：Y?</summary>
        public double ReadMarkerValue()
        {
            return ParseMarkerY(":CALC:MARK1:Y?");
        }

        /// <summary>
        /// 频谱仪复位（SCPI 标准 *RST + *CLS）。
        /// <para>*RST 复位到出厂默认状态；*CLS 清状态字节/错误队列。
        /// 调用间隔 100ms 让仪表完成内部处理。</para>
        /// </summary>
        public void Reset()
        {
            SendCommand("*RST");
            Thread.Sleep(100);
            SendCommand("*CLS");
            Thread.Sleep(100);
            Logger.Log($"[{DateTime.Now:HH:mm:ss.fff}] 频谱仪已复位");
        }

        // ====================================================================
        // Marker
        // ====================================================================

        /// <summary>设置 DELTA Marker 并读取差值（dB）。文档：DELTA + X + Y?</summary>
        public double ReadDeltaValue(double markerFreq)
        {
            SendCommand(":CALC:MARK1:MODE DELTA");
            SendCommand(string.Format(CultureInfo.InvariantCulture, ":CALC:MARK1:X {0} MHz", markerFreq / 1e6));
            return ParseMarkerY(":CALC:MARK1:Y?");
        }

        /// <summary>切换到 DELTA 模式并读取差值（dB）。文档：DELTA + Y?</summary>
        public double SetDeltaValue()
        {
            SendCommand(":CALC:MARK1:MODE DELTA");
            return ParseMarkerY(":CALC:MARK1:Y?");
        }

        /// <summary>
        /// 仅开启差值标记（:CALC:MARK1:MODE DELTA），不读 Y?、不设 X 频率。
        /// <para>供 UI 便捷按钮调用——调试时直接看频谱仪屏幕，
        /// 不需要程序端读差值。复位或关闭所有标记请使用 <see cref="SetAllMarkOFF"/>。</para>
        /// </summary>
        public void EnableDeltaMarker()
        {
            SendCommand(":CALC:MARK1:MODE DELTA");
        }

        /// <summary>
        /// 读取当前 Marker 的幅值（dBm）。<para>
        /// 与 <see cref="ReadMarkerValue"/> 等价，仅命名上强调"紧跟一次 marker 移动之后读幅值"，
        /// 供<c>MAX:NEXT</c> 逐峰搜索场景使用。
        /// </summary>
        public double ReadMarkerAmplitude()
        {
            return ParseMarkerY(":CALC:MARK1:Y?");
        }

        /// <summary>
        /// Marker 跳到"下一个峰"（Peak Next）。对应 SCPI 命令 <c>:CALC:MARK1:MAX:NEXT</c>。
        /// <para><b>用途（带内虚假信号测试）：</b>RF 扫频经过当前频点时，IF 输出端会出现主信号
        /// （Mode3 二中频 750 MHz），它比杂散强得多，会被 <c>MAX:PEAK</c> 优先搜到。
        /// 本方法让 marker 跳到下一个峰，从而拿到"除主信号之外"的最高峰。</para>
        /// <para><b>仪表行为提示：</b>本命令只在迹线已有峰值搜索结果时有意义；
        /// 调用前应先执行 <see cref="MeasureMarkerPeak"/>（MAX:PEAK + MAX:PEAK:SEARCH）定位到主峰。
        /// 跳到下一个峰后用 <see cref="ReadMarkerAmplitude"/> 读幅值、
        /// 用 <see cref="ReadMarkerFrequency"/> 读频率，二者必须配对读取。</para>
        /// </summary>
        public void MoveMarkerToNextPeak()
        {
            SendCommand(":CALC:MARK1:MAX:NEXT");
        }

        /// <summary>
        /// 读取当前 Marker 的频率（Hz）。对应 SCPI 命令 <c>:CALC:MARK1:X?</c>。
        /// <para>与 <see cref="ReadMarkerAmplitude"/> 配对使用，用于确认"跳过的那个峰之后
        /// 落在哪个频率上"，便于判断读到的是杂散还是底噪。</para>
        /// </summary>
        public double ReadMarkerFrequency()
        {
            string resp = QueryString(":CALC:MARK1:X?");
            if (string.IsNullOrWhiteSpace(resp))
            {
                throw new InvalidOperationException(
                    "频谱仪 Marker X? 返回空响应（命令：:CALC:MARK1:X?）。");
            }
            if (!double.TryParse(resp, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                throw new InvalidOperationException(
                    $"频谱仪 Marker X? 响应无法解析为数值：'{resp}'。");
            }
            return value;
        }

        /// <summary>峰峰值搜索（Peak-to-Peak）。文档：MARK:PTP + MARK:Y?</summary>
        public double ReadPeakToPeak()
        {
            SendCommand(":CALC:MARK:PTP");
            return ParseMarkerY(":CALC:MARK:Y?");
        }

        /// <summary>关闭所有 Marker。</summary>
        public void SetAllMarkOFF()
        {
            SendCommand(":CALC:MARK1:AOFF");
        }

        // ====================================================================
        // 显示与触发
        // ====================================================================

        /// <summary>设置平均模式开关。文档：:TRAC:TYPE AVER / WRIT</summary>
        public void SetAverageMode(bool enable)
        {
            SendCommand(string.Format(CultureInfo.InvariantCulture, ":TRAC:TYPE {0}", enable ? "AVER" : "WRIT"));
        }

        /// <summary>设置最大保持模式开关。文档：TRACE1:MODE MAXH / WRIT</summary>
        public void SetMaxHold(bool enable)
        {
            SendCommand(enable ? ":TRACE1:MODE MAXH" : ":TRACE1:MODE WRIT");
        }

        /// <summary>设置最小保持模式开关。文档：TRACE1:MODE MINH / WRIT</summary>
        public void SetMinHold(bool enable)
        {
            SendCommand(enable ? ":TRACE1:MODE MINH" : ":TRACE1:MODE WRIT");
        }

        /// <summary>清除显示。</summary>
        public void ClearDisplay()
        {
            SendCommand(":DISP:WIND:TRAC:CLE");
        }

        /// <summary>设置参考电平（dBm）。</summary>
        public void SetReferenceLevel(double leveldBm)
        {
            SendCommand(string.Format(CultureInfo.InvariantCulture, ":DISP:WIND:TRAC:Y:RLEV {0} dBm", leveldBm));
        }

        /// <summary>
        /// 读取当前参考电平（dBm）。文档：:DISP:WIND:TRAC:Y:RLEV?
        /// <para>响应为空或非数值时抛 InvalidOperationException；调用方应捕获并仅记日志。</para>
        /// </summary>
        public double QueryReferenceLevel()
        {
            string resp = QueryString(":DISP:WIND:TRAC:Y:RLEV?");
            if (string.IsNullOrWhiteSpace(resp))
            {
                throw new InvalidOperationException(
                    "频谱仪参考电平查询返回空响应（命令：:DISP:WIND:TRAC:Y:RLEV?）。");
            }
            if (!double.TryParse(resp, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                throw new InvalidOperationException(
                    $"频谱仪参考电平查询响应无法解析为数值：'{resp}'。");
            }
            return value;
        }

        /// <summary>设置参考电平偏移（dB）。</summary>
        public void SetReferenceLevelOffset(double offsetdBm)
        {
            SendCommand(string.Format(CultureInfo.InvariantCulture, ":DISP:WIND:TRAC:Y:RLEV:OFFS {0}", offsetdBm));
        }

        /// <summary>设置扫描时间（秒）。</summary>
        public void SetSweepTime(double seconds)
        {
            SendCommand(string.Format(CultureInfo.InvariantCulture, ":SWE:TIME {0} s", seconds));
        }

        /// <summary>启用视频触发。文档：TRIG:SOUR VID + TRIG:LEV:AUTO ON</summary>
        public void EnableVideoTrigger()
        {
            SendCommand(":TRIG:SOUR VID");
            SendCommand(":TRIG:LEV:AUTO ON");
        }

        /// <summary>连续/单次扫描切换。文档：INIT:CONT ON / OFF</summary>
        public void SetContinuousScan(bool continuous)
        {
            SendCommand(continuous ? ":INIT:CONT ON" : ":INIT:CONT OFF");
        }

        /// <summary>启用峰值自动搜索。兼容旧调用方，内部转发到 <see cref="SetPeakAutoSearch(bool)"/>。</summary>
        public void EnablePeakAutoSearch()
        {
            SetPeakAutoSearch(true);
        }

        /// <summary>设置峰值自动搜索开关（:CALC:MARK:CPS ON / OFF）。</summary>
        public void SetPeakAutoSearch(bool enable)
        {
            SendCommand(string.Format(CultureInfo.InvariantCulture, ":CALC:MARK:CPS {0}", enable ? "ON" : "OFF"));
        }

        // ====================================================================
        // 谐波
        // ====================================================================

        /// <summary>进入谐波测试模式。</summary>
        public void InitializeHarmonicsTest()
        {
            SendCommand(":INITiate:HARMonics");
        }

        /// <summary>读取所有谐波幅度（返回逗号分隔字符串，调用方自行解析）。</summary>
        public string FetchHarmonicsAmplitude()
        {
            return QueryString(":FETCh:HARMonics:AMPLitude:ALL?");
        }

        // ====================================================================
        // Internal helpers
        // ====================================================================

        /// <summary>
        /// 解析 Marker Y? 响应为 double。响应为空或非数值时抛出明确异常，
        /// 避免 <c>double.Parse</c> 默认抛出难以定位的 FormatException。
        /// </summary>
        private double ParseMarkerY(string scpi)
        {
            string resp = QueryString(scpi);
            if (string.IsNullOrWhiteSpace(resp))
            {
                throw new InvalidOperationException(
                    $"频谱仪 Marker Y? 返回空响应（命令：{scpi.TrimEnd('\n')}）。" +
                    "请检查仪表连接与状态，或确认测试桩是否预设了对应命令的响应。");
            }
            if (!double.TryParse(resp, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                throw new InvalidOperationException(
                    $"频谱仪 Marker Y? 响应无法解析为数值：'{resp}'（命令：{scpi.TrimEnd('\n')}）。");
            }
            return value;
        }
    }
}