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
            // 默认基础配置：9600 / 8 / N / 1
            _serialPort = new SerialPort
            {
                BaudRate = 9600,
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
        public virtual void Open(string portName, int baudRate = 9600)
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

            _serialPort.Write(data, 0, data.Length);
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
                Logger.Log($"[Serial] Receive error: {ex.Message}");
            }
        }

        private void OnSerialPortErrorReceived(object sender, SerialErrorReceivedEventArgs e)
        {
            Logger.Log($"[Serial] Error: {e.EventType}");
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