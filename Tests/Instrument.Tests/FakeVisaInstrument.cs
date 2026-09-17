using System;
using System.Collections.Generic;
using Ivi.Visa;
using A205AutoTestSystem.InstrumentControl;

namespace A205AutoTestSystem.Tests.InstrumentTests
{
    /// <summary>
    /// 测试用伪 VISA 传输。实现 <see cref="IVisaTransport"/>，不实际打开任何 VISA 资源。
    /// <list type="bullet">
    /// <item>维护"命令期望 → 响应"映射字典，可针对每个 SCPI 命令预设返回值。</item>
    /// <item>记录调用历史（<see cref="SendHistory"/>），便于断言"发出了什么命令"。</item>
    /// <item>支持在指定调用次数后注入 <see cref="VisaException"/>（瞬态或非瞬态），
    ///     验证 <c>VisaBaseInstrument.ExecuteWithRetry</c> 的行为。</item>
    /// </list>
    /// </summary>
    public sealed class FakeVisaInstrument : IVisaTransport
    {
        // 期望 → 响应映射；键为 SCPI 命令（已去除首尾空白与换行），值为响应字符串。
        private readonly Dictionary<string, string> _responses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // 调用序号 → 抛异常；null 表示不抛
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

        /// <summary>已发送的命令历史（按调用顺序）。</summary>
        public IReadOnlyList<string> SendHistory => _sendHistory;

        /// <summary>已执行的 Open() 次数。</summary>
        public int OpenCount { get; private set; }

        /// <summary>预设命令响应。</summary>
        public void SetResponse(string scpiCommand, string response)
        {
            string key = NormalizeKey(scpiCommand);
            _responses[key] = response ?? string.Empty;
        }

        /// <summary>让第 <paramref name="callIndex"/> 次调用（Open/Write/ReadString）抛出异常。</summary>
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
            if (!_isOpen)
            {
                throw new InvalidOperationException("FakeVisaInstrument 未 Open。");
            }
            MaybeThrow();
            _sendHistory.Add(data ?? string.Empty);
        }

        public string ReadString()
        {
            if (!_isOpen)
            {
                throw new InvalidOperationException("FakeVisaInstrument 未 Open。");
            }
            MaybeThrow();

            // 找最后一次 Write 命令对应的响应
            string lastWrite = _sendHistory.Count > 0 ? _sendHistory[_sendHistory.Count - 1] : null;
            if (lastWrite != null)
            {
                string key = NormalizeKey(lastWrite);
                if (_responses.TryGetValue(key, out string resp))
                {
                    return resp;
                }
            }
            // 默认响应（避免 null）
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
            return scpi.Trim().TrimEnd('\r', '\n').TrimEnd(';');
        }
    }

    /// <summary>便捷工厂：构造指定 HResult 的 VisaException。</summary>
    public static class FakeVisaException
    {
        public static VisaException WithHResult(int hresult, string message = null)
        {
            // VisaException 只暴露 (string, Exception) 构造；HResult 通过初始化异常属性不可设，
            // 但 .NET 的 Exception.HResult setter 实际是 protected。反射打开它。
            var ex = new VisaException(message ?? $"Fake VisaException hresult=0x{hresult:X8}");
            typeof(Exception)
                .GetProperty("HResult", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
                ?.SetValue(ex, hresult);
            return ex;
        }
    }
}