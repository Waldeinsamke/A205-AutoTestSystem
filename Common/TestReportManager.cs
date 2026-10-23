using System;
using System.Globalization;
using A205AutoTestSystem.InstrumentControl;

namespace A205AutoTestSystem.Common
{
    /// <summary>
    /// 测试报告全局管理器（M7 阶段新增）。
    /// <para>采用进程内单例（<see cref="Lazy{T}"/>），聚合一段时间内产生的全部
    /// <see cref="TestResult"/>，到用户点击「导出 Excel」时一次性落盘。</para>
    /// <para>订阅（生产者）：各 <see cref="ITestItem"/> 在 <c>ExecuteAsync</c>
    /// 末尾调用 <see cref="Record"/> 把结果塞进当前批次的 <see cref="ExcelExporter"/>。</para>
    /// <para>落盘（消费者）：UI 的「导出 Excel」按钮调用 <see cref="SaveCurrentReport"/>，
    /// 把当前批次写为 <c>A205_测试报告_yyyyMMdd_HHmmss.xlsx</c>。</para>
    /// <para>温度区间兜底：各测试项构造时不知道当前温度档位（<see cref="TestCatalog"/>
    /// 用完即丢），导致 <see cref="TestResult.TemperatureRange"/> 默认为空、Excel 该列为空白。
    /// UI 在「加载测试」时调用 <see cref="SetTemperatureContext"/> 写入档位，
    /// <see cref="Record"/> 发现结果缺温度时按此兜底。</para>
    /// <para>线程安全：所有公共方法都用 <see cref="_lock"/> 串行化，
    /// 防止 WinForms 异步测试项并发调用导致行乱序。</para>
    /// </summary>
    public class TestReportManager
    {
        private static readonly Lazy<TestReportManager> _instance =
            new Lazy<TestReportManager>(() => new TestReportManager(), isThreadSafe: true);

        private readonly object _lock = new object();
        private ExcelExporter _exporter;

        /// <summary>
        /// 当前温度档位的显示名（"常温" / "高温" / "低温"），供 <see cref="Record"/> 兜底填充。
        /// 默认 null：尚未经 <see cref="SetTemperatureContext"/> 设置时不做任何兜底。
        /// </summary>
        private string _temperatureContext;

        /// <summary>进程内单例。</summary>
        public static TestReportManager Instance
        {
            get { return _instance.Value; }
        }

        private TestReportManager()
        {
            // 私有构造，确保单例
        }

        /// <summary>当前批次是否已有任何测试结果。</summary>
        public bool HasPendingResults
        {
            get
            {
                lock (_lock)
                {
                    return _exporter != null && _exporter.RowCount > 0;
                }
            }
        }

        /// <summary>当前批次已追加的数据行数（不含表头）。</summary>
        public int PendingRowCount
        {
            get
            {
                lock (_lock)
                {
                    return _exporter == null ? 0 : _exporter.RowCount;
                }
            }
        }

        /// <summary>
        /// 设置当前温度档位的显示名，供后续 <see cref="Record"/> 兜底填充
        /// <see cref="TestResult.TemperatureRange"/>。
        /// <para>由 UI 在「加载测试」确定档位时调用（须早于任何测试项执行）；
        /// 传 null / 空串表示清除上下文，此后再记录的结果温度列保持空白。</para>
        /// </summary>
        public void SetTemperatureContext(string displayName)
        {
            lock (_lock)
            {
                _temperatureContext = string.IsNullOrWhiteSpace(displayName) ? null : displayName;
            }
        }

        /// <summary>
        /// 由测试项在 <c>ExecuteAsync</c> 末尾调用，把结果追加到当前批次。
        /// <para>第一次调用时会惰性创建 <see cref="ExcelExporter"/>。</para>
        /// <para>温度兜底：测试项本身不掌握温度档位（<see cref="TestCatalog"/> 构造时
        /// 用完即丢），若结果未带 <see cref="TestResult.TemperatureRange"/>，
        /// 则用 <see cref="SetTemperatureContext"/> 设定的档位名补上，
        /// 避免 Excel 温度列出现空白。</para>
        /// </summary>
        /// <param name="result">测试结果（不可为 null）。</param>
        public void Record(TestResult result)
        {
            if (result == null) return;
            lock (_lock)
            {
                if (_exporter == null)
                {
                    _exporter = new ExcelExporter();
                    Logger.Log("[Report] 新批次开始，等待测试结果…");
                }

                if (string.IsNullOrWhiteSpace(result.TemperatureRange) && _temperatureContext != null)
                {
                    result.TemperatureRange = _temperatureContext;
                }

                _exporter.AppendResult(result);
                Logger.Log(string.Format(
                    CultureInfo.InvariantCulture,
                    "[Report] 已记录：{0} = {1:F4} {2} → {3}",
                    SafeStr(result.TestItem), result.Value, SafeStr(result.Unit), SafeStr(result.Judgment)));
            }
        }

        /// <summary>
        /// 把当前批次落盘，返回实际文件路径；若当前没有结果则返回 null。
        /// </summary>
        /// <param name="filePath">可选的目标文件路径；为 null 时自动按
        /// <c>A205_测试报告_yyyyMMdd_HHmmss.xlsx</c> 规则生成。</param>
        public string SaveCurrentReport(string filePath = null)
        {
            lock (_lock)
            {
                if (_exporter == null || _exporter.RowCount == 0)
                {
                    Logger.Log("[Report] 当前无任何测试结果，跳过保存。");
                    return null;
                }
                try
                {
                    string savedPath = _exporter.Save(filePath);
                    Logger.Log("[Report] 导出完成：" + savedPath + "（共 " + _exporter.RowCount + " 行）");
                    return savedPath;
                }
                finally
                {
                    // 落盘后释放旧 exporter，下次 Record() 会创建新批次
                    try { _exporter.Dispose(); } catch { }
                    _exporter = null;
                }
            }
        }

        /// <summary>
        /// 显式开始新批次：若当前有未保存的 exporter，则释放并清空。
        /// <para>典型调用：用户切换通道 / 模式 / 温度时。</para>
        /// </summary>
        public void StartNewBatch()
        {
            lock (_lock)
            {
                if (_exporter != null)
                {
                    try { _exporter.Dispose(); } catch { }
                    _exporter = null;
                    Logger.Log("[Report] 已开启新批次（之前的未保存结果已丢弃）。");
                }
            }
        }

        private static string SafeStr(string s)
        {
            return s ?? string.Empty;
        }
    }
}
