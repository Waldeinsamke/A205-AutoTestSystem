using System;
using System.Collections.Generic;
using Ivi.Visa;

namespace A205AutoTestSystem.InstrumentControl
{
    /// <summary>
    /// 主工程内嵌的伪 VISA 传输实现（M6 阶段演示用）。
    /// <para>与 <c>Tests/Instrument.Tests/FakeVisaInstrument.cs</c> 同源；为了避免主工程
    /// 引用测试工程，主工程单独维护一份简化版（仅支持演示场景）。</para>
    /// <para>特性：</para>
    /// <list type="bullet">
    /// <item>实现 <see cref="IVisaTransport"/>，不打开任何实际 VISA 资源。</item>
    /// <item>维护"SCPI 命令 → 响应"字典，可预设 <c>CALC:MARK:Y?</c> 等查询返回值。</item>
    /// <item>记录已发送的命令历史（<see cref="SendHistory"/>）。</item>
    /// </list>
    /// <para>真机阶段（M8）：<see cref="Program.Main"/> 把
    /// <c>VisaBaseInstrument.DefaultTransportFactory</c> 注册为
    /// <c>addr => new NationalInstrumentsVisaTransport(addr)</c>，本类不再被调用。</para>
    /// </summary>
    public sealed class FakeVisaInstrument : IVisaTransport
    {
        private readonly Dictionary<string, string> _responses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _sendHistory = new List<string>();
        private bool _isOpen;
        private bool _disposed;

        public FakeVisaInstrument(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                throw new ArgumentException("资源地址不能为空。", nameof(address));
            }
            Address = address;
        }

        public string Address { get; }

        public bool IsOpen => _isOpen;

        public int TimeoutMilliseconds { get; set; }

        /// <summary>已发送的命令历史（按调用顺序）。</summary>
        public IReadOnlyList<string> SendHistory => _sendHistory;

        public int OpenCount { get; private set; }

        /// <summary>预设 SCPI 命令的响应。</summary>
        public void SetResponse(string scpiCommand, string response)
        {
            string key = NormalizeKey(scpiCommand);
            _responses[key] = response ?? string.Empty;
        }

        public void Open()
        {
            OpenCount++;
            _isOpen = true;
        }

        public void Write(string data)
        {
            if (!_isOpen)
            {
                throw new InvalidOperationException("FakeVisaInstrument 未 Open。");
            }
            _sendHistory.Add(data ?? string.Empty);
        }

        public string ReadString()
        {
            if (!_isOpen)
            {
                throw new InvalidOperationException("FakeVisaInstrument 未 Open。");
            }
            string lastWrite = _sendHistory.Count > 0 ? _sendHistory[_sendHistory.Count - 1] : null;
            if (lastWrite != null)
            {
                string key = NormalizeKey(lastWrite);
                if (_responses.TryGetValue(key, out string resp))
                {
                    return resp;
                }
            }
            return string.Empty;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _isOpen = false;
        }

        private static string NormalizeKey(string scpi)
        {
            if (scpi == null) return string.Empty;
            return scpi.Trim().TrimEnd('\r', '\n').TrimEnd(';');
        }
    }
}