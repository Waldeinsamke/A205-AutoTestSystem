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
    /// <para>M6.x 阶段：新增 <see cref="IsMock"/> 开关，置 true 时所有 Send* 方法只记日志、
    /// 不真发字节，用于 AutoTestPanel 走 Fake 流程时让 GainTest 模式×频点循环可跑通。</para>
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
        /// 是否为 Mock 模式。M6.x 阶段由 AutoTestPanel 走 Fake 流程时构造并置 true：
        /// 所有 Send* 方法只打日志、不真发字节；IsOpen 也直接返回 true。
        /// 真机阶段保持 false。
        /// </summary>
        public bool IsMock { get; set; }

        /// <summary>
        /// 是否已打开。Mock 模式下直接返回 true，真机模式委托基类判断。
        /// <para>用 <c>new</c> 隐藏基类的 <c>IsOpen</c>（基类是表达式体 getter，
        /// 这里需要重写行为因此采用 new 隐藏）。注意：通过基类引用访问时仍是基类行为，
        /// 但 GainTest 与 AutoTestPanel 持有的是 <see cref="ReceiverSerialPort"/> 引用，
        /// 因此走派生类实现。</para>
        /// </summary>
        public new bool IsOpen => IsMock || base.IsOpen;

        /// <summary>
        /// 发送指定工作模式下的通道选择命令（单条帧）。
        /// </summary>
        public void SendChannelCommand(int channel, ReceiverMode mode)
        {
            if (IsMock)
            {
                Logger.Log($"[Receiver:Mock] SendChannelCommand mode={mode} ch={channel}");
                return;
            }
            byte[] frame = _builder.BuildChannelCommand(channel, mode);
            SendBytes(frame);
            Logger.Log($"[Receiver] SendChannelCommand mode={mode} ch={channel} -> {FormatHex(frame)}");
        }

        /// <summary>
        /// 发送工作模式命令序列（3 条）。
        /// </summary>
        public void SendModeCommand(ReceiverMode mode)
        {
            if (IsMock)
            {
                Logger.Log($"[Receiver:Mock] SendModeCommand mode={mode}");
                return;
            }
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
            if (IsMock)
            {
                Logger.Log($"[Receiver:Mock] SendTemperatureCommand range={range}");
                return;
            }
            byte[][] sequence = _builder.BuildTemperatureCommandSequence(range);
            Logger.Log($"[Receiver] SendTemperatureCommand range={range} ({sequence.Length} frames)");
            SendSequence(sequence);
        }

        /// <summary>
        /// 发送频段命令序列（2 条）。
        /// </summary>
        public void SendBandCommand(ReceiverMode mode, int bandIndex)
        {
            if (IsMock)
            {
                Logger.Log($"[Receiver:Mock] SendBandCommand mode={mode} bandIndex={bandIndex}");
                return;
            }
            byte[][] sequence = _builder.BuildBandCommandSequence(mode, bandIndex);
            Logger.Log($"[Receiver] SendBandCommand mode={mode} bandIndex={bandIndex} ({sequence.Length} frames)");
            SendSequence(sequence);
        }

        /// <summary>
        /// 发送 AGC 衰减命令序列（2 条）。
        /// </summary>
        public void SendAttenuationCommand(int channel, int attenDb)
        {
            if (IsMock)
            {
                Logger.Log($"[Receiver:Mock] SendAttenuationCommand ch={channel} atten={attenDb}dB");
                return;
            }
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
