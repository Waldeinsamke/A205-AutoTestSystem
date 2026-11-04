using System;
using System.IO.Ports;

namespace A205AutoTestSystem.Common
{
    /// <summary>
    /// 串口通信基类。封装 <see cref="SerialPort"/> 的常用操作，
    /// 供接收机和工装矩阵串口类继承。M1 阶段仅提供最小骨架，
    /// 具体协议层命令收发由子类在 M2 / M3 阶段补齐。
    /// </summary>
    public abstract class SerialPortBase : IDisposable
    {
        private readonly SerialPort _serialPort;
        private bool _disposed;

        /// <summary>
        /// 串口接收到数据时触发（字节数组形式）。
        /// </summary>
        public event Action<byte[]> DataReceived;

        /// <summary>
        /// 串口掉线时触发，参数为掉线原因描述。
        /// <para>与只写日志的旧行为不同，这里把掉线<b>通知给上层</b>：
        /// .NET Framework 的 <see cref="SerialPort.ErrorReceived"/> 只上报缓冲类错误
        /// （<c>RXOver / Overrun / RXParity / Frame / TXFull</c>），
        /// 设备被拔出时<b>不会</b>经该事件上报，上层只能靠"长时间收不到数据"来推断。</para>
        /// <para>本基类在收发失败且串口已不可用时触发本事件，
        /// 上层据此把对应节点置为故障态（提示人工介入）；
        /// 本基类不负责恢复连接，恢复策略由上层决定。</para>
        /// </summary>
        public event Action<string> PortError;

        /// <summary>
        /// 当前串口是否处于打开状态。
        /// </summary>
        public bool IsOpen => !_disposed && _serialPort != null && _serialPort.IsOpen;

        /// <summary>
        /// 当前串口名（如 "COM1"）。
        /// </summary>
        public string PortName => _serialPort?.PortName;

        /// <summary>
        /// 当前波特率。
        /// </summary>
        public int BaudRate => _serialPort?.BaudRate ?? 0;

        protected SerialPortBase()
        {
            // 默认基础配置：115200 / 8 / N / 1
            _serialPort = new SerialPort
            {
                BaudRate = 115200,
                DataBits = 8,
                Parity = Parity.None,
                StopBits = StopBits.One,
                ReadTimeout = 500,
                WriteTimeout = 500
            };
            _serialPort.DataReceived += OnSerialPortDataReceived;
            _serialPort.ErrorReceived += OnSerialPortErrorReceived;
        }

        /// <summary>
        /// 打开指定串口。
        /// </summary>
        public virtual void Open(string portName, int baudRate = 115200)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(SerialPortBase));
            }

            if (_serialPort.IsOpen)
            {
                _serialPort.Close();
            }

            _serialPort.PortName = portName;
            _serialPort.BaudRate = baudRate;
            _serialPort.Open();
            Logger.Log($"[Serial] Open {portName} @ {baudRate}");
        }

        /// <summary>
        /// 关闭串口。
        /// </summary>
        public virtual void Close()
        {
            if (_serialPort != null && _serialPort.IsOpen)
            {
                _serialPort.Close();
                Logger.Log($"[Serial] Close {PortName}");
            }
        }

        /// <summary>
        /// 发送字节数组。
        /// </summary>
        public virtual void SendBytes(byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                return;
            }

            if (!_serialPort.IsOpen)
            {
                throw new InvalidOperationException("Serial port is not open.");
            }

            try
            {
                _serialPort.Write(data, 0, data.Length);
            }
            catch (Exception ex)
            {
                // 写失败且串口已不可用 → 判定为掉线并通知上层；
                // 异常仍向上抛出，保持"发送失败必须让调用方知道"的原有语义。
                RaisePortErrorIfClosed("发送", ex);
                throw;
            }
            Logger.LogSend(data);
        }

        /// <summary>
        /// 发送十六进制字符串。允许形如 "0D 11 01 04 00 00 0A" 或 "0D11010400000A"。
        /// </summary>
        public virtual void SendHex(string hex)
        {
            if (string.IsNullOrEmpty(hex))
            {
                return;
            }

            byte[] bytes = ParseHexString(hex);
            SendBytes(bytes);
        }

        /// <summary>
        /// 解析十六进制字符串为字节数组。
        /// </summary>
        protected static byte[] ParseHexString(string hex)
        {
            string cleaned = hex.Replace(" ", string.Empty).Replace("-", string.Empty);
            if (cleaned.Length % 2 != 0)
            {
                throw new FormatException("Hex string length must be even.");
            }

            byte[] result = new byte[cleaned.Length / 2];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = Convert.ToByte(cleaned.Substring(i * 2, 2), 16);
            }
            return result;
        }

        private void OnSerialPortDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                int bytesToRead = _serialPort.BytesToRead;
                if (bytesToRead <= 0)
                {
                    return;
                }

                byte[] buffer = new byte[bytesToRead];
                _serialPort.Read(buffer, 0, bytesToRead);
                Logger.LogRecv(buffer);
                DataReceived?.Invoke(buffer);
            }
            catch (Exception ex)
            {
                RaisePortErrorIfClosed("接收", ex);
            }
        }

        private void OnSerialPortErrorReceived(object sender, SerialErrorReceivedEventArgs e)
        {
            Logger.Log($"[Serial] Error: {e.EventType}");
            // 注意：此处不据此判定掉线。.NET Framework 的 SerialError 只有缓冲类错误
            // （RXOver / Overrun / RXParity / Frame / TXFull），与设备是否还在线无关；
            // 设备被拔出时反而不经此事件上报。掉线判定统一放在收发失败处（见 RaisePortErrorIfClosed）。
        }

        /// <summary>
        /// 收发失败且串口已不可用时判定为掉线并通知上层。
        /// <para>判据是"操作失败 <b>且</b> 串口已不是打开状态"：单纯的读超时或单次写异常
        /// 并不能说明设备掉线（线缆接触抖动也会导致），只有串口本身已失效才作掉线处理，
        /// 避免把偶发通信错误误报成需要人工介入的故障。</para>
        /// </summary>
        /// <param name="operation">失败的操作描述（用于日志与上层提示）。</param>
        /// <param name="ex">捕获到的异常。</param>
        private void RaisePortErrorIfClosed(string operation, Exception ex)
        {
            Logger.Log($"[Serial] {operation} failed: {ex.Message}");

            if (IsOpen)
            {
                // 串口仍处于打开状态 → 只是本次通信失败（超时 / 抖动），不判定掉线
                return;
            }

            PortError?.Invoke($"{operation}失败，串口已不可用: {ex.Message}");
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
                if (_serialPort != null)
                {
                    _serialPort.DataReceived -= OnSerialPortDataReceived;
                    _serialPort.ErrorReceived -= OnSerialPortErrorReceived;
                    if (_serialPort.IsOpen)
                    {
                        _serialPort.Close();
                    }
                    _serialPort.Dispose();
                }
            }
            catch
            {
                // 忽略释放阶段的异常
            }
            GC.SuppressFinalize(this);
        }
    }
}