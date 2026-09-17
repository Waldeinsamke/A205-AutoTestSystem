using System;
using System.Threading;
using System.Threading.Tasks;
using A205AutoTestSystem.InstrumentControl;

namespace A205AutoTestSystem.InstrumentControl.TestItems
{
    /// <summary>
    /// 自动测试项接口。所有测试用例（如 GainTest / BandwidthTest）均实现该接口，
    /// 由 <see cref="AutoTestEngine"/> 统一调度执行。
    /// </summary>
    public interface ITestItem
    {
        /// <summary>测试项目名称（如 "线性最大增益"）。</summary>
        string TestName { get; }

        /// <summary>测量单位（dB / MHz 等）。</summary>
        string Unit { get; }

        /// <summary>判据下限（包含），null 表示不设下限。</summary>
        double? LowerLimit { get; }

        /// <summary>判据上限（包含），null 表示不设上限。</summary>
        double? UpperLimit { get; }

        /// <summary>
        /// 异步执行单次测试，返回 <see cref="TestResult"/>。
        /// </summary>
        /// <param name="progress">进度上报通道，可用于 UI 进度条/状态更新。</param>
        /// <param name="ct">取消令牌。</param>
        Task<TestResult> ExecuteAsync(IProgress<string> progress, CancellationToken ct);
    }
}