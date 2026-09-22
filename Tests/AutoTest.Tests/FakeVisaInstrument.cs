using System;
using System.Collections.Generic;
using Ivi.Visa;
using A205AutoTestSystem.InstrumentControl;

namespace A205AutoTestSystem.Tests.AutoTestTests
{
    /// <summary>
    /// M6 测试工程的伪 VISA 传输实现。
    /// <para>与 <c>Tests/Instrument.Tests/FakeVisaInstrument.cs</c> 同源；
    /// 拷贝一份到本测试工程是为了避免跨工程引用。</para>
    /// <para>实现 <see cref="IVisaTransport"/>，支持：</para>
    /// <list type="bullet">
    /// <item>命令 → 响应映射（<see cref="SetResponse"/>）</item>
    /// <item>调用历史（<see cref="SendHistory"/>）</item>
    /// <item>第 N 次调用抛异常（<see cref="InjectError"/>）</item>
    /// </list>
    /// </summary>
    public sealed class FakeVisaInstrument : IVisaTransport
    {
        private readonly Dictionary<string, string> _responses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, VisaException> _errors = new Dictionary<int, VisaException>();
        private readonly List<string> _sendHistory = new List<string>();
        private bool _isOpen;
        private bool _disposed;
        private int _callIndex;

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
        public IReadOnlyList<string> SendHistory => _sendHistory;
        public int OpenCount { get; private set; }

        public void SetResponse(string scpiCommand, string response)
        {
            string key = NormalizeKey(scpiCommand);
            _responses[key] = response ?? string.Empty;
        }

        public void InjectError(int callIndex, VisaException ex)
        {
            _errors[callIndex] = ex ?? throw new ArgumentNullException(nameof(ex));
        }

        public void Open()
        {
            OpenCount++;
            MaybeThrow();
            _isOpen = true;
        }

        public void Write(string data)
        {
            if (!_isOpen) throw new InvalidOperationException("FakeVisaInstrument 未 Open。");
            MaybeThrow();
            _sendHistory.Add(data ?? string.Empty);
        }

        public string ReadString()
        {
            if (!_isOpen) throw new InvalidOperationException("FakeVisaInstrument 未 Open。");
            MaybeThrow();
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
            if (_disposed) return;
            _disposed = true;
            _isOpen = false;
        }

        private void MaybeThrow()
        {
            _callIndex++;
            if (_errors.TryGetValue(_callIndex, out var ex))
            {
                throw ex;
            }
        }

        private static string NormalizeKey(string scpi)
        {
            if (scpi == null) return string.Empty;
            // 去掉前导冒号，让测试 SetResponse 时既可写 "CALC:MARK1:Y?" 也可写 ":CALC:MARK1:Y?"
            // （与 Instrument.Tests 既有的无冒号 SendHistory 断言约定保持一致）
            return scpi.Trim().TrimStart(':').TrimEnd('\r', '\n').TrimEnd(';');
        }
    }

    /// <summary>便捷工厂：构造指定 HResult 的 VisaException。</summary>
    public static class FakeVisaException
    {
        public static VisaException WithHResult(int hresult, string message = null)
        {
            var ex = new VisaException(message ?? $"Fake VisaException hresult=0x{hresult:X8}");
            typeof(Exception)
                .GetProperty("HResult", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
                ?.SetValue(ex, hresult);
            return ex;
        }
    }
}