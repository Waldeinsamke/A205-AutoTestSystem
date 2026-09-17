using System;
using A205AutoTestSystem.Common;

namespace A205AutoTestSystem.MatrixControl
{
    /// <summary>
    /// 工装矩阵串口桩实现。在协议未到位时提供 noop 实现，
    /// 仅记录日志，避免上游业务因协议缺失而阻塞。
    /// </summary>
    public class MatrixSerialPort : IChannelSwitcher
    {
        private string _portName;

        /// <inheritdoc />
        public bool IsConnected => false;

        /// <inheritdoc />
        public string PortName => _portName;

        /// <inheritdoc />
        public event Action<bool> ConnectionChanged;

        /// <inheritdoc />
        public void Connect(string portName, int baudRate = 9600)
        {
            _portName = portName;
            Logger.Log($"[Matrix] protocol not ready, called Connect({portName}, {baudRate})");
            // 协议未到位：不真正打开串口
            ConnectionChanged?.Invoke(false);
        }

        /// <inheritdoc />
        public void Disconnect()
        {
            Logger.Log("[Matrix] protocol not ready, called Disconnect");
            _portName = null;
            ConnectionChanged?.Invoke(false);
        }

        /// <inheritdoc />
        public void SwitchChannel(int channel)
        {
            Logger.Log($"[Matrix] protocol not ready, called SwitchChannel({channel})");
        }
    }
}