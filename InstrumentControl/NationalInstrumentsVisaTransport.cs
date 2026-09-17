using System;
using Ivi.Visa;
using NationalInstruments.Visa;

namespace A205AutoTestSystem.InstrumentControl
{
    /// <summary>
    /// 基于 <c>NationalInstruments.Visa 25.0</c> 的 <see cref="IVisaTransport"/> 实现。
    /// <para>本文件在主工程编译时包含；测试工程通过 <c>Compile Include</c> 不包含本文件，
    /// 避免测试用例产生对 NI VISA 驱动的依赖。</para>
    /// </summary>
    public sealed class NationalInstrumentsVisaTransport : IVisaTransport
    {
        private MessageBasedSession _session;
        private bool _disposed;

        public NationalInstrumentsVisaTransport(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                throw new ArgumentException("VISA 资源地址不能为空。", nameof(address));
            }
            Address = address;
        }

        public string Address { get; }

        public bool IsOpen => !_disposed && _session != null;

        public int TimeoutMilliseconds
        {
            get => _session?.TimeoutMilliseconds ?? 0;
            set
            {
                if (_session != null)
                {
                    _session.TimeoutMilliseconds = value;
                }
            }
        }

        public void Open()
        {
            if (_session != null)
            {
                return;
            }

            using (var rm = new ResourceManager())
            {
                _session = (MessageBasedSession)rm.Open(Address, AccessModes.None, TimeoutMilliseconds);
            }

            if (_session == null)
            {
                throw new InvalidOperationException(
                    $"无法打开 VISA 资源：{Address}（Open 返回 null）");
            }

            // 多数仪表以换行符结尾响应，开启 TerminationCharacter = '\n' 提升兼容性
            try
            {
                _session.TerminationCharacter = 0x0A;
                _session.TerminationCharacterEnabled = true;
            }
            catch
            {
                // 某些接口类型可能不支持终止字符；忽略
            }
        }

        public void Write(string data)
        {
            if (_session == null)
            {
                throw new InvalidOperationException("VISA 会话未打开。");
            }
            _session.RawIO.Write(data);
        }

        public string ReadString()
        {
            if (_session == null)
            {
                throw new InvalidOperationException("VISA 会话未打开。");
            }
            return _session.RawIO.ReadString();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            try
            {
                _session?.Dispose();
            }
            catch
            {
                // 释放阶段吞掉异常
            }
            finally
            {
                _session = null;
            }
            GC.SuppressFinalize(this);
        }
    }
}