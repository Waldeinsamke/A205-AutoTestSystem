using System;
using A205AutoTestSystem.Common;
using A205AutoTestSystem.ReceiverControl;

namespace A205AutoTestSystem.AutoTest.Shared
{
    /// <summary>
    /// 测试温度档位的共享上下文 + 温度切换命令下发入口。
    /// <para><b>为什么需要它</b>：温度档位由 UI 在「加载测试」时确定，测试项自身在构造期
    /// 并不知道当前跑的是常温 / 高温 / 低温。但业务要求"开始测试流程时，向串口发送的
    /// 第一条命令必须是温度切换命令"——温度档位必须先落到硬件上，后续的通道 / 模式 /
    /// 频段命令才有意义。因此档位放在这里，由 UI 写入、测试项读取并下发命令。</para>
    /// <para><b>谁会发这条命令</b>：每个真正操作接收机的测试项在 <c>ExecuteAsync</c> 开头
    /// 调用一次 <see cref="SendTo"/>。占位项（未实现）和纯计算项不调用——它们没有接收机引用。</para>
    /// <para>线程安全：所有公共方法用 <see cref="_lock"/> 串行化
    /// （UI 线程写、测试项后台线程读）。</para>
    /// </summary>
    public static class TemperatureContext
    {
        private static readonly object _lock = new object();

        // 当前测试温度档位。默认常温——与 UI 下拉框的初始选中项一致。
        private static TemperatureRange _current = TemperatureRange.Normal;

        /// <summary>
        /// 当前测试温度档位。
        /// </summary>
        public static TemperatureRange Current
        {
            get { lock (_lock) { return _current; } }
        }

        /// <summary>
        /// 写入当前测试温度档位。由 UI 在「加载测试」/ 切换温度档时调用。
        /// </summary>
        /// <param name="range">新的温度档位。</param>
        public static void Set(TemperatureRange range)
        {
            lock (_lock) { _current = range; }
        }

        /// <summary>
        /// 温度档位 → 界面 / 报表显示名（"常温" / "高温" / "低温"）。
        /// </summary>
        public static string DisplayName(TemperatureRange range)
        {
            switch (range)
            {
                case TemperatureRange.High: return "高温";
                case TemperatureRange.Low: return "低温";
                default: return "常温";
            }
        }

        /// <summary>
        /// 向接收机下发温度切换命令——测试流程向串口发送的第一条命令。
        /// <para>由每个"真跑硬件"的测试项在 <c>ExecuteAsync</c> 开头调用一次。</para>
        /// </summary>
        /// <param name="receiver">接收机串口（必须已 Open）。</param>
        /// <param name="testItemName">测试项名称，用于日志与进度文案。</param>
        /// <param name="progress">进度上报通道，可为 null。</param>
        /// <exception cref="InvalidOperationException">串口未打开时抛出。</exception>
        public static void SendTo(ReceiverSerialPort receiver, string testItemName, IProgress<string> progress)
        {
            if (receiver == null) throw new ArgumentNullException(nameof(receiver));
            if (!receiver.IsOpen)
                throw new InvalidOperationException("发送温度切换命令需要接收机串口已连接，请在『接收机控制』页打开串口。");

            TemperatureRange range = Current;
            receiver.SendTemperatureCommand(range);

            string msg = "[" + testItemName + "] 温度档位已切换为 " + DisplayName(range) + "。";
            Logger.Log(msg);
            if (progress != null) progress.Report(msg);
        }
    }
}
