using System;
using System.Collections.Generic;
using System.IO.Ports;

namespace A205AutoTestSystem.Common
{
    /// <summary>
    /// 串口列表扫描器：定期读取系统当前存在的串口，并与上一次快照做差分。
    /// <para><b>设计约束（纯逻辑，不依赖 WinForms）</b>：端口来源通过构造参数注入，
    /// 便于在没有真实硬件的环境下验证差分与排序行为。</para>
    /// <para><b>为什么用轮询而不是 WMI 事件</b>：<c>SerialPort.GetPortNames()</c> 只是读注册表
    /// <c>HARDWARE\DEVICEMAP\SERIALCOMM</c>，不触碰已打开的串口，1s 一次开销可忽略；
    /// WMI 方案需要额外引用 <c>System.Management</c>，且 <c>Win32_PnPEntity</c> 的 Name 形如
    /// <c>"USB-SERIAL CH340 (COM3)"</c> 需自行解析，订阅失败还是静默的。</para>
    /// </summary>
    public sealed class SerialPortWatcher
    {
        /// <summary>端口列表来源。默认取系统真实串口；测试时注入假列表。</summary>
        private readonly Func<string[]> _portProvider;

        /// <summary>最近一次扫描到的端口集合（已排序）。</summary>
        private List<string> _currentPorts = new List<string>();

        /// <summary>
        /// 构造扫描器。
        /// </summary>
        /// <param name="portProvider">
        /// 端口列表来源。传 null 时使用 <see cref="SerialPort.GetPortNames"/>。
        /// </param>
        public SerialPortWatcher(Func<string[]> portProvider = null)
        {
            _portProvider = portProvider ?? (() => SerialPort.GetPortNames());
        }

        /// <summary>
        /// 最近一次扫描到的端口列表（已按自然序排序）。
        /// <para>用于判断"正在使用的某个口是否已从系统中消失"。</para>
        /// </summary>
        public IReadOnlyList<string> CurrentPorts => _currentPorts;

        /// <summary>
        /// 判断指定端口是否存在于最近一次扫描结果中。
        /// </summary>
        public bool ContainsPort(string portName)
        {
            return !string.IsNullOrEmpty(portName) && _currentPorts.Contains(portName);
        }

        /// <summary>
        /// 扫描一次系统串口列表，并与上一次快照比较。
        /// </summary>
        /// <param name="added">本次新出现的端口。</param>
        /// <param name="removed">本次消失的端口。</param>
        /// <returns>列表发生变化时返回 true；无变化时返回 false（调用方据此跳过 UI 刷新）。</returns>
        public bool Scan(out IReadOnlyList<string> added, out IReadOnlyList<string> removed)
        {
            List<string> current = ReadSortedPorts();

            List<string> addedList = new List<string>();
            List<string> removedList = new List<string>();

            for (int i = 0; i < current.Count; i++)
            {
                if (!_currentPorts.Contains(current[i]))
                {
                    addedList.Add(current[i]);
                }
            }

            for (int i = 0; i < _currentPorts.Count; i++)
            {
                if (!current.Contains(_currentPorts[i]))
                {
                    removedList.Add(_currentPorts[i]);
                }
            }

            _currentPorts = current;

            added = addedList;
            removed = removedList;
            return addedList.Count > 0 || removedList.Count > 0;
        }

        /// <summary>
        /// 读取当前端口列表并按自然序排序（COM2 排在 COM10 之前）。
        /// </summary>
        private List<string> ReadSortedPorts()
        {
            string[] raw;
            try
            {
                raw = _portProvider();
            }
            catch
            {
                // 端口枚举理论上不会失败；真失败时保持上一份快照不变，
                // 避免把"读不到"误判成"设备全被拔出"而误断开正在使用的串口。
                return new List<string>(_currentPorts);
            }

            List<string> list = new List<string>();
            if (raw != null)
            {
                for (int i = 0; i < raw.Length; i++)
                {
                    string port = raw[i];
                    if (!string.IsNullOrEmpty(port) && !list.Contains(port))
                    {
                        list.Add(port);
                    }
                }
            }

            list.Sort(ComparePortNames);
            return list;
        }

        /// <summary>
        /// 端口名自然序比较：COM2 &lt; COM10。
        /// <para>非 COM 形式的名称（如未来可能出现的 USB 名称）按前缀字母序、
        /// 前缀相同时按数字大小排列。</para>
        /// </summary>
        internal static int ComparePortNames(string x, string y)
        {
            SplitPrefixAndNumber(x, out string prefixX, out long numberX);
            SplitPrefixAndNumber(y, out string prefixY, out long numberY);

            int prefixCompare = string.Compare(prefixX, prefixY, StringComparison.OrdinalIgnoreCase);
            if (prefixCompare != 0)
            {
                return prefixCompare;
            }

            int numberCompare = numberX.CompareTo(numberY);
            if (numberCompare != 0)
            {
                return numberCompare;
            }

            return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 把端口名拆成"字母前缀 + 数字后缀"。如 COM12 → ("COM", 12)；无数字后缀时数字为 0。
        /// </summary>
        private static void SplitPrefixAndNumber(string port, out string prefix, out long number)
        {
            int end = port.Length;
            while (end > 0 && char.IsDigit(port[end - 1]))
            {
                end--;
            }

            prefix = port.Substring(0, end);
            string digits = port.Substring(end);

            long parsed;
            if (!long.TryParse(digits, out parsed))
            {
                parsed = 0;
            }

            number = parsed;
        }
    }
}
