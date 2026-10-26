using System;
using System.Globalization;

namespace A205AutoTestSystem.InstrumentControl
{
    /// <summary>
    /// ODP3063 可编程直流电源控制类。
    /// <para><b>通道选择规则</b>：本仪表的电压/电流设置、测量回读等操作都作用于
    /// <b>当前选中通道</b>，必须先发 <c>INSTrument:NSELect {n}</c> 选定通道再发后续命令。
    /// 因此本类把"选通道"封装进每个操作方法内部，调用方无需关心当前选中的是哪一路。</para>
    /// <para><b>供电开关规则</b>：本仪表<b>无法单独开关某一个通道</b>，
    /// 只能三个通道一起开或一起关。开关走 <c>:OUTP:STAT {0|1}</c>（<b>不带通道号</b>，
    /// 该命令作用于全部通道），且不依赖当前选中通道，因此<b>不</b>在开关前插入 <c>NSELect</c>。</para>
    /// <para><b>远/本地</b>：<c>SYSTem:REMote</c> / <c>SYSTem:LOCal</c> 用于在远程控制与
    /// 本地面板操作之间切换。</para>
    /// </summary>
    public class ODP3063 : VisaBaseInstrument
    {
        /// <summary>
        /// 本系统给 A205 接收模块供电所用通道号（CH2）。
        /// <para><b>只影响电压/电流参数</b>——<c>SetSupplyVoltage</c> / <c>SetSupplyCurrent</c> /
        /// <c>MeasureSupplyVoltage</c> / <c>MeasureSupplyCurrent</c> 会先选中该通道再操作。
        /// 供电<b>开关</b>走 <see cref="SetSupplyOutput"/>，作用于全部三路，与本常量无关。</para>
        /// </summary>
        public const int SupplyChannel = 2;

        public ODP3063(string address) : base(address) { }

        // ============================================================
        // 通道选择
        // ============================================================

        /// <summary>
        /// 选择后续命令作用的通道。对应 SCPI 命令 <c>INSTrument:NSELect {n}</c>。
        /// <para>电压/电流设置与测量回读都依赖当前选中通道，故本类内部在需要时自动调用。</para>
        /// </summary>
        /// <param name="channel">通道号（1~3）。</param>
        public void SelectChannel(int channel)
        {
            if (channel < 1 || channel > 3)
                throw new ArgumentOutOfRangeException(nameof(channel), channel, "ODP3063 通道号必须在 1~3 范围内。");

            SendCommand(string.Format(CultureInfo.InvariantCulture, "INSTrument:NSELect {0}", channel));
        }

        // ============================================================
        // 输出参数设置（作用于当前选中通道）
        // ============================================================

        /// <summary>
        /// 设置选中通道的输出电压（V）。对应 SCPI 命令 <c>VOLT {voltage}</c>。
        /// <para>本方法会先选中 <see cref="SupplyChannel"/> 再下发电压设置命令。</para>
        /// </summary>
        public void SetSupplyVoltage(double voltage)
        {
            SelectChannel(SupplyChannel);
            SendCommand(string.Format(CultureInfo.InvariantCulture, "VOLT {0}", voltage));
        }

        /// <summary>
        /// 设置选中通道的限流值（A）。对应 SCPI 命令 <c>CURR {current}</c>。
        /// <para>本方法会先选中 <see cref="SupplyChannel"/> 再下发限流设置命令。</para>
        /// </summary>
        public void SetSupplyCurrent(double current)
        {
            SelectChannel(SupplyChannel);
            SendCommand(string.Format(CultureInfo.InvariantCulture, "CURR {0}", current));
        }

        // ============================================================
        // 实际值回读（作用于当前选中通道）
        // ============================================================

        /// <summary>
        /// 读取选中通道的实际输出电压（V）。对应 SCPI 命令 <c>MEAS:VOLT?</c>。
        /// <para>本方法会先选中 <see cref="SupplyChannel"/> 再回读。</para>
        /// </summary>
        public double MeasureSupplyVoltage()
        {
            SelectChannel(SupplyChannel);
            return QueryDouble("MEAS:VOLT?");
        }

        /// <summary>
        /// 读取选中通道的实际输出电流（A）。对应 SCPI 命令 <c>MEAS:CURR?</c>。
        /// <para>本方法会先选中 <see cref="SupplyChannel"/> 再回读。</para>
        /// </summary>
        public double MeasureSupplyCurrent()
        {
            SelectChannel(SupplyChannel);
            return QueryDouble("MEAS:CURR?");
        }

        // ============================================================
        // 供电开关（三通道一起开/关，不依赖当前选中通道）
        // ============================================================

        /// <summary>
        /// 开启/关闭供电。三个通道<b>同时</b>开关，无法单独控制某一路。
        /// <para>对应 SCPI 命令 <c>:OUTP:STAT {1|0}</c>（不带通道号——本命令作用于全部通道）。</para>
        /// <para><b>注意</b>：本方法<b>不</b>作用于 <see cref="SupplyChannel"/> 这一路专属通道，
        /// 而是一次开关全部三路；<see cref="SupplyChannel"/> 只决定电压/电流参数设到哪一路。</para>
        /// </summary>
        public void SetSupplyOutput(bool enable)
        {
            SendCommand(string.Format(CultureInfo.InvariantCulture, ":OUTP:STAT {0}", enable ? 1 : 0));
        }

        /// <summary>
        /// 查询供电输出状态（仪表原始返回字符串）。对应 SCPI 命令 <c>:OUTP:STAT?</c>。
        /// <para>该状态是<b>三通道统一</b>的输出状态，不代表某一路专属通道。</para>
        /// </summary>
        public string QuerySupplyOutputStatus()
        {
            return QueryString(":OUTP:STAT?");
        }

        // ============================================================
        // 远程 / 本地控制权
        // ============================================================

        /// <summary>
        /// 切换到远程控制模式。对应 SCPI 命令 <c>SYSTem:REMote</c>。
        /// <para>远程模式下前面板按键被锁定，参数只能经 SCPI 修改。</para>
        /// </summary>
        public void SetRemoteMode()
        {
            SendCommand("SYSTem:REMote");
        }

        /// <summary>
        /// 交还本地面板控制权。对应 SCPI 命令 <c>SYSTem:LOCal</c>。
        /// <para>退出远程模式后，操作者可在仪表前面板直接调电压/电流。</para>
        /// </summary>
        public void SetLocalMode()
        {
            SendCommand("SYSTem:LOCal");
        }

        // ============================================================
        // 内部
        // ============================================================

        /// <summary>
        /// 查询并解析数值型响应。解析失败时抛出带命令原文的异常，便于定位。
        /// </summary>
        private double QueryDouble(string scpi)
        {
            string resp = QueryString(scpi);
            string trimmed = resp == null ? string.Empty : resp.Trim();
            if (trimmed.Length == 0)
            {
                throw new InvalidOperationException(
                    $"ODP3063 命令 \"{scpi}\" 返回空响应。请检查仪表连接与状态。");
            }
            if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                throw new InvalidOperationException(
                    $"ODP3063 命令 \"{scpi}\" 的响应无法解析为数值：'{trimmed}'。");
            }
            return value;
        }
    }
}
