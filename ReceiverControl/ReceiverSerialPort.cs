using System;
using System.Collections.Generic;
using System.Threading;
using A205AutoTestSystem.Common;

namespace A205AutoTestSystem.ReceiverControl
{
    /// <summary>
    /// 接收机串口业务封装。继承 <see cref="SerialPortBase"/>，
    /// 暴露业务方法（通道 / 模式 / 温度 / 频段 / 衰减），
    /// 并负责：
    /// <list type="bullet">
    /// <item>调用 <see cref="ReceiverCommandBuilder"/> 构造命令字节</item>
    /// <item>按可配置间隔串行发送多命令序列</item>
    /// <item>通过 <see cref="Logger"/> 留痕发送动作与原始字节</item>
    /// </list>
    /// M2 阶段完成。M5 / M6 / M8 阶段再叠加与工装矩阵 / 仪表的联动。
    /// </summary>
    public class ReceiverSerialPort : SerialPortBase
    {
        private readonly ReceiverCommandBuilder _builder = new ReceiverCommandBuilder();

        /// <summary>
        /// 同一命令序列内部命令之间的发送间隔（毫秒）。
        /// 默认 100ms，对应 spec.md R4 风险应对策略。
        /// </summary>
        public int SendIntervalMs { get; set; } = 100;

        /// <summary>
        /// 不同业务调用之间的发送间隔（毫秒）。默认 100ms。
        /// </summary>
        public int InterSequenceIntervalMs { get; set; } = 100;

        /// <summary>
        /// 发送指定工作模式下的通道选择命令（单条帧）。
        /// </summary>
        public void SendChannelCommand(int channel, ReceiverMode mode)
        {
            byte[] frame = _builder.BuildChannelCommand(channel, mode);
            SendBytes(frame);
            Logger.Log($"[Receiver] SendChannelCommand mode={mode} ch={channel} -> {FormatHex(frame)}");
        }

        /// <summary>
        /// 发送工作模式命令序列（3 条）。
        /// </summary>
        public void SendModeCommand(ReceiverMode mode)
        {
            byte[][] sequence = _builder.BuildModeCommandSequence(mode);
            Logger.Log($"[Receiver] SendModeCommand mode={mode} ({sequence.Length} frames)");
            SendSequence(sequence);
        }

        /// <summary>
        /// 先发送当前模式对应的通道选择命令，按 <see cref="InterSequenceIntervalMs"/> 等待后，
        /// 再发送工作模式命令序列。
        /// <para>通道命令发送失败时异常直接向上抛出，模式命令不会继续发送。</para>
        /// </summary>
        public void SendChannelThenModeCommand(int channel, ReceiverMode mode)
        {
            SendChannelCommand(channel, mode);

            if (InterSequenceIntervalMs > 0)
            {
                Thread.Sleep(InterSequenceIntervalMs);
            }

            SendModeCommand(mode);
        }

        /// <summary>
        /// 发送温度区间命令序列（2 条）。
        /// </summary>
        public void SendTemperatureCommand(TemperatureRange range)
        {
            byte[][] sequence = _builder.BuildTemperatureCommandSequence(range);
            Logger.Log($"[Receiver] SendTemperatureCommand range={range} ({sequence.Length} frames)");
            SendSequence(sequence);
        }

        /// <summary>
        /// 发送频段命令序列（2 条）。
        /// </summary>
        public void SendBandCommand(ReceiverMode mode, int bandIndex)
        {
            byte[][] sequence = _builder.BuildBandCommandSequence(mode, bandIndex);
            Logger.Log($"[Receiver] SendBandCommand mode={mode} bandIndex={bandIndex} ({sequence.Length} frames)");
            SendSequence(sequence);
        }

        /// <summary>
        /// 发送 AGC 衰减命令序列（2 条）。
        /// </summary>
        public void SendAttenuationCommand(int channel, int attenDb)
        {
            byte[][] sequence = _builder.BuildAttenuationCommandSequence(channel, attenDb);
            Logger.Log($"[Receiver] SendAttenuationCommand ch={channel} atten={attenDb}dB ({sequence.Length} frames)");
            SendSequence(sequence);
        }

        /// <summary>
        /// 按 <see cref="SendIntervalMs"/> 间隔串行发送一组命令帧。
        /// 各帧字节会经 <see cref="SerialPortBase.SendBytes"/> 发出，
        /// 并在 <see cref="Logger"/> 中留痕（详见 <c>SerialPortBase.SendBytes</c>）。
        /// </summary>
        private void SendSequence(IEnumerable<byte[]> frames)
        {
            if (frames == null)
            {
                return;
            }

            int delayMs = SendIntervalMs < 0 ? 0 : SendIntervalMs;
            bool first = true;
            foreach (var frame in frames)
            {
                if (!first && delayMs > 0)
                {
                    Thread.Sleep(delayMs);
                }
                first = false;
                SendBytes(frame);
            }
        }

        private static string FormatHex(byte[] data)
        {
            return Logger.ToHex(data);
        }
    }
}