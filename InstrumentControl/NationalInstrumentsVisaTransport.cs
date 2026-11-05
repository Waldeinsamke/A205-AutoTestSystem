using Ivi.Visa;
using NationalInstruments.Visa;
using System;

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
        /// <summary>
        /// 待生效的超时值。无论 session 是否已打开都缓存，
        /// session 已存在则在 setter 里立即回写；session 尚未打开则留待 Open() 消费。
        /// </summary>
        private int _pendingTimeoutMs = 5000;

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

        /// <summary>
        /// 单次 I/O 操作超时（毫秒）。
        /// <para>setter 无条件缓存 + session 已存在时立即回写；
        /// 若 session 尚未打开，缓存值会被下一次 <see cref="Open"/> 消费。</para>
        /// <para>getter 在 session 存在时读 session 的实时值，否则返回缓存值。</para>
        /// </summary>
        public int TimeoutMilliseconds
        {
            get => _session?.TimeoutMilliseconds ?? _pendingTimeoutMs;
            set
            {
                _pendingTimeoutMs = value;
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
                // ResourceManager.Open 的 timeout 参数只管**连接建立**的超时；
                // NI VISA 驱动还需要单独设置 session.TimeoutMilliseconds 才生效于后续 I/O。
                // 这里传缓存值，避免原实现中 getter 在 session==null 时返回 0 导致
                // 会话以 0ms 打开的缺陷（见 VisaBaseInstrument.Connect 先赋值后 Open 的顺序）。
                _session = (MessageBasedSession)rm.Open(Address, AccessModes.None, _pendingTimeoutMs);
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

            // 双保险：ResourceManager.Open 的 timeout 参数只管**连接建立**的超时；
            // NI VISA 驱动对后续 I/O 还要单独 session.TimeoutMilliseconds 才生效。
            _session.TimeoutMilliseconds = _pendingTimeoutMs;
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