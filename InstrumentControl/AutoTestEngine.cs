using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using A205AutoTestSystem.Common;
using A205AutoTestSystem.InstrumentControl.TestItems;

namespace A205AutoTestSystem.InstrumentControl
{
    /// <summary>
    /// 自动测试引擎。负责测试项注册、串行执行、进度与日志回调。
    /// M1 阶段仅落地骨架，具体测试用例在 M6 阶段补齐。
    /// </summary>
    public class AutoTestEngine
    {
        private readonly List<ITestItem> _items = new List<ITestItem>();

        /// <summary>测试过程中的日志事件。</summary>
        public event Action<string> LogEmitted;

        /// <summary>当前注册的测试项数量。</summary>
        public int Count => _items.Count;

        /// <summary>注册一个测试项。</summary>
        public void RegisterTest(ITestItem item)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            _items.Add(item);
            Emit($"[Engine] Register: {item.TestName}");
        }

        /// <summary>按名称注销一个测试项。</summary>
        public bool UnregisterTest(string testName)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (string.Equals(_items[i].TestName, testName, StringComparison.OrdinalIgnoreCase))
                {
                    Emit($"[Engine] Unregister: {_items[i].TestName}");
                    _items.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        /// <summary>清空所有已注册测试项。</summary>
        public void Clear()
        {
            _items.Clear();
            Emit("[Engine] Clear");
        }

        /// <summary>
        /// 串行执行所有已注册的测试项。M1 阶段为空跑骨架。
        /// </summary>
        public async Task RunAllAsync(IProgress<string> progress, CancellationToken ct)
        {
            Emit($"[Engine] RunAllAsync count={_items.Count}");

            for (int i = 0; i < _items.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                ITestItem item = _items[i];
                string prefix = $"[{i + 1}/{_items.Count}] {item.TestName}";
                Emit($"{prefix} start");
                progress?.Report(prefix);

                try
                {
                    TestResult result = await item.ExecuteAsync(progress, ct).ConfigureAwait(false);
                    Emit($"{prefix} done value={result.Value} {result.Unit} judgment={result.Judgment}");
                }
                catch (OperationCanceledException)
                {
                    Emit($"{prefix} canceled");
                    throw;
                }
                catch (Exception ex)
                {
                    Emit($"{prefix} error: {ex.Message}");
                }
            }

            Emit("[Engine] RunAllAsync completed");
        }

        private void Emit(string message)
        {
            Logger.Log(message);
            LogEmitted?.Invoke(message);
        }
    }
}