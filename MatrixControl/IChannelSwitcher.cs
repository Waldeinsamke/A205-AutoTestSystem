using System;

namespace A205AutoTestSystem.MatrixControl
{
    /// <summary>
    /// 工装矩阵通道切换抽象接口。
    /// <para>矩阵协议尚未到位（见 spec.md R1 风险），通过该接口隔离协议依赖；
    /// 上游业务（接收机通道联动）只需面向接口编程。</para>
    /// </summary>
    public interface IChannelSwitcher
    {
        /// <summary>当前是否已连接到工装矩阵。</summary>
        bool IsConnected { get; }

        /// <summary>当前连接的串口名。</summary>
        string PortName { get; }

        /// <summary>建立串口连接。</summary>
        void Connect(string portName, int baudRate = 9600);

        /// <summary>断开串口连接。</summary>
        void Disconnect();

        /// <summary>切换到指定通道（1..N，具体范围由协议决定）。</summary>
        void SwitchChannel(int channel);

        /// <summary>连接状态变化事件。</summary>
        event Action<bool> ConnectionChanged;
    }
}