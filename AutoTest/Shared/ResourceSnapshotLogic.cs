using System;

namespace A205AutoTestSystem.AutoTest.Shared
{
    /// <summary>
    /// H-6 资源快照判定的纯逻辑（从 AutoTestPanel 抽出）。
    /// <para>设计动机：AutoTestPanel 是 WinForms UserControl，测试工程通过
    /// <c><c><Compile Include></c></c> 链接源文件时不能实例化 UserControl。
    /// 把"5 个布尔 → 判定结果"的纯逻辑下沉到本独立类后，测试工程仅需链接
    /// 本文件即可覆盖所有组合，无需引入 System.Windows.Forms / System.Drawing 依赖。</para>
    /// <para>原报告条目：审查报告 §3 H-6 "AutoTestPanel 三层分支竞态"。</para>
    /// </summary>
    public static class ResourceSnapshotLogic
    {
        /// <summary>
        /// 纯判定：接收 5 个布尔值，返回"全就绪?"与"具体原因"。
        /// 优先级：receiver 缺失 > receiver 已断开 > RF > LO > SA。
        /// </summary>
        /// <param name="hasReceiver">接收机串口对象是否非 null。</param>
        /// <param name="receiverOpen">接收机串口是否已 Open（仅在 hasReceiver=true 时有意义）。</param>
        /// <param name="rfReady">RF 信号源是否已 Connect。</param>
        /// <param name="loReady">LO 信号源是否已 Connect。</param>
        /// <param name="saReady">频谱仪是否已 Connect。</param>
        /// <returns>判定结果：AllReady + Reason。Reason 为空表示全就绪。</returns>
        public static ResourceSnapshotDecision EvaluateReadiness(
            bool hasReceiver, bool receiverOpen,
            bool rfReady, bool loReady, bool saReady)
        {
            if (!hasReceiver) return new ResourceSnapshotDecision(false, "接收机串口未连接");
            if (!receiverOpen) return new ResourceSnapshotDecision(false, "接收机串口已断开");
            if (!rfReady) return new ResourceSnapshotDecision(false, "RF 信号源未连接");
            if (!loReady) return new ResourceSnapshotDecision(false, "LO 信号源未连接");
            if (!saReady) return new ResourceSnapshotDecision(false, "频谱仪未连接");
            return new ResourceSnapshotDecision(true, string.Empty);
        }
    }

    /// <summary>
    /// 纯判定结果。
    /// <para>公开 readonly struct，便于测试代码直接调用；
    /// 生产端由 AutoTestPanel.SnapshotResources 包装使用。</para>
    /// </summary>
    public readonly struct ResourceSnapshotDecision
    {
        public readonly bool AllReady;
        public readonly string Reason;

        public ResourceSnapshotDecision(bool allReady, string reason)
        {
            AllReady = allReady;
            Reason = reason;
        }
    }
}
