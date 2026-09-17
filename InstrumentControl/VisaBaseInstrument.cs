using System;
using System.Threading;
using Ivi.Visa;
using A205AutoTestSystem.Common;

namespace A205AutoTestSystem.InstrumentControl
{
    /// <summary>
    /// VISA 传输抽象接口。把"打开资源 / 关闭资源 / 写 SCPI / 读响应"四件事
    /// 与具体 VISA 实现解耦，<see cref="VisaBaseInstrument"/> 只依赖本接口，
    /// 不直接接触 <c>NationalInstruments.Visa</c> 的具体类型。
    /// <para>生产实现：<c>NationalInstrumentsVisaTransport</c>（见同名文件）。</para>
    /// <para>测试实现：<c>FakeVisaTransport</c>（见 <c>Tests/Instrument.Tests</c>）。</para>
    /// </summary>
    public interface IVisaTransport : IDisposable
    {
        /// <summary>资源地址（如 "GPIB0::15::INSTR" / "TCPIP0::192.168.1.10::INSTR"）。</summary>
        string Address { get; }

        /// <summary>当前是否已打开。</summary>
        bool IsOpen { get; }

        /// <summary>单次 I/O 操作超时（毫秒）。</summary>
        int TimeoutMilliseconds { get; set; }

        /// <summary>打开资源。</summary>
        void Open();

        /// <summary>写入 SCPI 命令（不读取响应）。</summary>
        void Write(string data);

        /// <summary>读取响应字符串（去除末尾 \n）。</summary>
        string ReadString();
    }

    /// <summary>
    /// VISA 仪表基类。封装通用 VISA 操作（资源打开、SCPI 发送/读取、超时、重试、事件），
    /// 供 <see cref="SignalGenerator"/> / <see cref="SignalGeneratorLO"/> / <see cref="SpectrumAnalyzer"/> 等使用。
    /// <para>M4 阶段实现要点：</para>
    /// <list type="bullet">
    /// <item>通过 <see cref="IVisaTransport"/> 抽象接口与底层 VISA 驱动解耦，
    /// 默认实现使用 <c>NationalInstruments.Visa 25.0</c>。</item>
    /// <item>默认超时 5000ms，可通过 <see cref="TimeoutMs"/> 覆盖。</item>
    /// <item>重试机制：默认 3 次，指数退避（100ms / 200ms / 400ms）。</item>
    /// <item>对外暴露 <see cref="ConnectionChanged"/> 与 <see cref="InstrumentError"/> 事件。</item>
    /// </list>
    /// </summary>
    public abstract class VisaBaseInstrument : IDisposable
    {
        private IVisaTransport _transport;
        private bool _disposed;

        // NI VISA 25.0 中 Ivi.Visa.VisaException 不暴露 ErrorCode 属性，使用 HResult。
        // 下列常量来自 VISA 规范：
        //   VI_ERROR_TMO          = 0xBFFF0015 = -1073807339 (超时)
        //   VI_ERROR_SYSTEM_ERROR = 0xBFFF000D = -1073807347 (系统错误)
        //   VI_ERROR_IO           = 0xBFFF000E = -1073807346 (IO 错误)
        //   VI_ERROR_CONN_LOST    = 0xBFFF001E = -1073807330 (连接丢失)
        private const int VisaErrorTimeout = -1073807339;
        private const int VisaErrorSystemError = -1073807347;
        private const int VisaErrorIo = -1073807346;
        private const int VisaErrorConnectionLost = -1073807330;

        /// <summary>VISA 资源地址（如 "GPIB0::15::INSTR" / "TCPIP0::192.168.1.10::INSTR"）。</summary>
        public string Address { get; set; } = string.Empty;

        /// <summary>当前是否已连接。</summary>
        public bool IsConnected { get; protected set; }

        /// <summary>单次 I/O 操作超时（毫秒）。默认 5000ms。</summary>
        public int TimeoutMs { get; set; } = 5000;

        /// <summary>瞬态错误的最大重试次数。默认 3 次（首次 + 2 次重试）。</summary>
        public int MaxRetry { get; set; } = 3;

        /// <summary>连接状态变化事件。参数：true=连接成功，false=断开。</summary>
        public event Action<bool> ConnectionChanged;

        /// <summary>仪表错误事件（非重试可恢复的异常）。</summary>
        public event Action<Exception> InstrumentError;

        /// <summary>
        /// 当前传输对象。子类（生产或测试）可通过 <see cref="SetTransport"/> 注入。
        /// </summary>
        protected IVisaTransport Transport => _transport;

        protected VisaBaseInstrument(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                throw new ArgumentException("VISA 资源地址不能为空。", nameof(address));
            }
            Address = address;
        }

        /// <summary>
        /// 注入传输实现。仅在调用 <see cref="Connect"/> 之前生效。
        /// <para>测试场景：在测试用例中注入 <c>FakeVisaTransport</c> 验证命令与重试逻辑。</para>
        /// </summary>
        protected void SetTransport(IVisaTransport transport)
        {
            if (IsConnected)
            {
                throw new InvalidOperationException("已连接，无法替换传输实现。");
            }
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        /// <summary>建立 VISA 连接。</summary>
        public virtual void Connect()
        {
            if (IsConnected)
            {
                return;
            }

            try
            {
                if (_transport == null)
                {
                    _transport = CreateTransport();
                    if (_transport == null)
                    {
                        throw new InvalidOperationException(
                            $"VISA 传输未配置。请在调用 Connect() 之前先调用 SetTransport() 注入实现，" +
                            $"或在应用启动时设置 {nameof(VisaBaseInstrument)}.{nameof(DefaultTransportFactory)}。");
                    }
                }
                _transport.TimeoutMilliseconds = TimeoutMs;
                _transport.Open();

                IsConnected = true;
                Logger.Log($"[VISA:{GetType().Name}] connected at {Address}");
                ConnectionChanged?.Invoke(true);
            }
            catch (VisaException vex)
            {
                Logger.Log($"[VISA:{GetType().Name}] Connect failed: {vex.Message} (HResult=0x{vex.HResult:X8})");
                SafeDisposeTransport();
                IsConnected = false;
                InstrumentError?.Invoke(vex);
                throw;
            }
            catch (Exception ex)
            {
                Logger.Log($"[VISA:{GetType().Name}] Connect failed: {ex.Message}");
                SafeDisposeTransport();
                IsConnected = false;
                InstrumentError?.Invoke(ex);
                throw;
            }
        }

        /// <summary>
        /// 默认传输工厂。生产环境应在应用启动时设置：
        /// <c>VisaBaseInstrument.DefaultTransportFactory = addr => new NationalInstrumentsVisaTransport(addr);</c>
        /// <para>测试环境无需设置，直接通过 <see cref="SetTransport"/> 注入伪传输。</para>
        /// </summary>
        public static Func<string, IVisaTransport> DefaultTransportFactory { get; set; }

        /// <summary>
        /// 创建默认传输实例。
        /// <list type="bullet">
        /// <item>若 <see cref="DefaultTransportFactory"/> 已设置，则委托工厂创建。</item>
        /// <item>若未设置则返回 null；调用 <see cref="Connect"/> 时会要求用户改用 <see cref="SetTransport"/> 注入。</item>
        /// </list>
        /// </summary>
        protected virtual IVisaTransport CreateTransport()
        {
            var factory = DefaultTransportFactory;
            return factory != null ? factory(Address) : null;
        }

        /// <summary>断开连接。</summary>
        public virtual void Disconnect()
        {
            if (!IsConnected && _transport == null)
            {
                return;
            }

            SafeDisposeTransport();
            IsConnected = false;
            Logger.Log($"[VISA:{GetType().Name}] disconnected from {Address}");
            ConnectionChanged?.Invoke(false);
        }

        /// <summary>发送 SCPI 命令（不读取返回值）。自动追加换行符以便多数仪表正确解析。</summary>
        public virtual void SendCommand(string scpi)
        {
            if (scpi == null)
            {
                throw new ArgumentNullException(nameof(scpi));
            }
            EnsureConnected();

            // SCPI 命令以 \n 结尾是惯例；保持原内容由调用方控制，这里只在无终止符时追加
            string payload = scpi.EndsWith("\n") ? scpi : scpi + "\n";

            ExecuteWithRetry(() =>
            {
                LogRawSend(payload);
                _transport.Write(payload);
                return true;
            });
        }

        /// <summary>发送 SCPI 查询命令并读取字符串返回值（去除末尾 \r / \n）。</summary>
        public virtual string QueryString(string scpi)
        {
            if (scpi == null)
            {
                throw new ArgumentNullException(nameof(scpi));
            }
            EnsureConnected();

            string payload = scpi.EndsWith("\n") ? scpi : scpi + "\n";

            string response = ExecuteWithRetry(() =>
            {
                LogRawSend(payload);
                _transport.Write(payload);
                string resp = _transport.ReadString();
                LogRawRecv(resp);
                return resp;
            });

            return response == null ? string.Empty : response.TrimEnd('\r', '\n');
        }

        /// <summary>执行带重试的操作。仅对瞬态 VISA 错误（超时 / 系统错误 / IO / 连接丢失）触发重试。</summary>
        protected T ExecuteWithRetry<T>(Func<T> action)
        {
            if (action == null            )
            {
                throw new ArgumentNullException(nameof(action));
            }

            int attempt = 0;
            Exception lastEx = null;

            while (attempt < MaxRetry)
            {
                attempt++;
                try
                {
                    return action();
                }
                catch (VisaException vex) when (IsTransientError(vex))
                {
                    lastEx = vex;
                    if (attempt >= MaxRetry)
                    {
                        break;
                    }
                    int delayMs = (int)(100 * Math.Pow(2, attempt - 1));
                    Logger.Log(
                        $"[VISA:{GetType().Name}] retry {attempt}/{MaxRetry} after {delayMs}ms: " +
                        $"{vex.Message} (HResult=0x{vex.HResult:X8})");
                    Thread.Sleep(delayMs);
                }
                catch (Exception ex)
                {
                    // 非瞬态错误直接抛出，并触发观测事件
                    InstrumentError?.Invoke(ex);
                    throw;
                }
            }

            InstrumentError?.Invoke(lastEx);
            throw lastEx;
        }

        private static bool IsTransientError(VisaException ex)
        {
            return ex.HResult == VisaErrorTimeout
                || ex.HResult == VisaErrorSystemError
                || ex.HResult == VisaErrorIo
                || ex.HResult == VisaErrorConnectionLost;
        }

        /// <summary>记录发送的 SCPI（仅在调试日志可见）。</summary>
        protected void LogRawSend(string scpi)
        {
            if (!string.IsNullOrEmpty(scpi))
            {
                Logger.Log($">> [{GetType().Name}] {scpi.TrimEnd('\n', '\r')}");
            }
        }

        /// <summary>记录接收的 SCPI 响应。</summary>
        protected void LogRawRecv(string response)
        {
            if (response != null)
            {
                Logger.Log($"<< [{GetType().Name}] {response.TrimEnd('\n', '\r')}");
            }
        }

        protected void EnsureConnected()
        {
            if (!IsConnected || _transport == null)
            {
                throw new InvalidOperationException(
                    $"VISA 仪表未连接：{GetType().Name} ({Address})");
            }
        }

        private void SafeDisposeTransport()
        {
            if (_transport == null)
            {
                return;
            }
            try
            {
                _transport.Dispose();
            }
            catch
            {
                // 释放阶段的异常忽略
            }
            finally
            {
                _transport = null;
            }
        }

        public virtual void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            try
            {
                Disconnect();
            }
            catch
            {
                // Dispose 阶段吞掉异常
            }
            GC.SuppressFinalize(this);
        }
    }
}