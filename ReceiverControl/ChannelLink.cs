using System;
using System.Threading;
using System.Threading.Tasks;
using A205AutoTestSystem.Common;
using A205AutoTestSystem.MatrixControl;

namespace A205AutoTestSystem.ReceiverControl
{
    /// <summary>
    /// 接收机通道选择与工装矩阵通道切换的联动逻辑。
    /// 上层（UI / AutoTestEngine）通过本类一次调用同时下发接收机通道命令与工装矩阵切换。
    /// <para>顺序约定（参见 <c>spec.md</c> §4.1.4）：先切换工装矩阵（射频信号路径），
    /// 等待切换稳定后，再下发接收机通道命令。</para>
    /// </summary>
    public class ChannelLink
    {
        private readonly ReceiverSerialPort _receiver;
        private readonly IChannelSwitcher _matrix;

        /// <summary>
        /// 矩阵切换与接收机通道命令之间的等待间隔（毫秒）。
        /// 默认 100ms，对应 <c>spec.md</c> R4 风险应对策略。
        /// 协议到位后此值可能需要调整，参见 <c>protocol-todo.md</c> 第 6 项。
        /// </summary>
        public int InterChannelDelayMs { get; set; } = 100;

        /// <summary>
        /// 创建联动器。<paramref name="receiver"/> 与 <paramref name="matrix"/> 均不允许为 null。
        /// </summary>
        public ChannelLink(ReceiverSerialPort receiver, IChannelSwitcher matrix)
        {
            _receiver = receiver ?? throw new ArgumentNullException(nameof(receiver));
            _matrix = matrix ?? throw new ArgumentNullException(nameof(matrix));
        }

        /// <summary>
        /// 同步版通道切换。顺序：矩阵先切 → 等待 → 接收机通道命令。
        /// </summary>
        public void SwitchTo(int channel)
        {
            ValidateChannel(channel);

            Logger.Log($"[ChannelLink] SwitchTo ch={channel}: matrix first");
            _matrix.SwitchChannel(channel);

            if (InterChannelDelayMs > 0)
            {
                Thread.Sleep(InterChannelDelayMs);
            }

            Logger.Log($"[ChannelLink] SwitchTo ch={channel}: receiver command");
            _receiver.SendChannelCommand(channel);
        }

        /// <summary>
        /// 异步版通道切换。顺序与同步版一致，调用方可传入 <see cref="CancellationToken"/>。
        /// </summary>
        public async Task SwitchToAsync(int channel, CancellationToken ct = default)
        {
            ValidateChannel(channel);

            Logger.Log($"[ChannelLink] SwitchToAsync ch={channel}: matrix first");
            _matrix.SwitchChannel(channel);

            if (InterChannelDelayMs > 0)
            {
                await Task.Delay(InterChannelDelayMs, ct).ConfigureAwait(false);
            }

            Logger.Log($"[ChannelLink] SwitchToAsync ch={channel}: receiver command");
            _receiver.SendChannelCommand(channel);
        }

        private static void ValidateChannel(int channel)
        {
            if (channel < 1 || channel > 8)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(channel),
                    channel,
                    "A205 接收机通道号必须在 1..8 范围内。");
            }
        }
    }
}