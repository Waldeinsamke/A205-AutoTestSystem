using System;
using System.Globalization;
using System.IO;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using A205AutoTestSystem.Common;
using A205AutoTestSystem.InstrumentControl;

namespace A205AutoTestSystem.Tests.ExcelExporterTests
{
    /// <summary>
    /// M7 阶段：ExcelExporter 端到端测试主程序。
    /// <para>通过 NPOI 的 XSSFWorkbook 重新打开保存的 .xlsx 文件，
    /// 断言行数、表头、数据字段值都符合预期。</para>
    /// <para>覆盖：</para>
    /// <list type="bullet">
    /// <item><see cref="ExcelExporter"/> 直接 append+save 的 round-trip</item>
    /// <item><see cref="TestReportManager"/> 单例 + Record + SaveCurrentReport 流</item>
    /// <item>默认文件命名 <c>A205_测试报告_yyyyMMdd_HHmmss.xlsx</c></item>
    /// </list>
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            Console.WriteLine("=== ExcelExporter.Tests（M7 数据管理与 Excel 报告）===");
            Console.WriteLine($"启动时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine();

            try
            {
                TestDirectExcelExporter();
                TestTestReportManager();
                TestDefaultFileNamePattern();
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Fatal] 未捕获异常：" + ex);
                return 2;
            }

            Console.WriteLine();
            Console.WriteLine("--- 汇总 ---");
            Console.WriteLine($"总断言数: {Assert.TotalCount}");
            Console.WriteLine($"失败断言数: {Assert.FailureCount}");
            if (Assert.FailureCount == 0)
            {
                Console.WriteLine("结果: PASS");
                return 0;
            }
            Console.WriteLine("结果: FAIL");
            return 1;
        }

        // ====================================================================
        // (1) 直接 ExcelExporter round-trip
        // ====================================================================

        private static void TestDirectExcelExporter()
        {
            Console.WriteLine("[Test] ExcelExporter round-trip（Pass + Fail + Pass）");

            string tempFile = Path.Combine(
                Path.GetTempPath(),
                string.Format(CultureInfo.InvariantCulture,
                    "excel_exporter_roundtrip_{0:yyyyMMdd_HHmmss_fff}.xlsx",
                    DateTime.Now));

            // 准备 3 条结果：Pass / Fail / Pass
            TestResult r1 = BuildResult("R1-Pass", "常温", 1, "Mode1", "线性最大增益", 12.34, "dB", "Pass", "gain=12.34");
            TestResult r2 = BuildResult("R2-Fail", "高温", 2, "Mode2", "信号带宽",     99.99, "MHz", "Fail", "bw too large");
            TestResult r3 = BuildResult("R3-Pass", "低温", 3, "Mode3", "线性最大增益", 18.50, "dB", "Pass", "cold test");

            string savedPath;
            using (ExcelExporter exp = new ExcelExporter())
            {
                Assert.CurrentTestName = "ExcelExporter: 创建后 RowCount == 0";
                Assert.Equal(0, exp.RowCount);

                exp.AppendResult(r1);
                Assert.CurrentTestName = "ExcelExporter: 追加第 1 条后 RowCount == 1";
                Assert.Equal(1, exp.RowCount);

                exp.AppendResult(r2);
                exp.AppendResult(r3);
                Assert.CurrentTestName = "ExcelExporter: 追加 3 条后 RowCount == 3";
                Assert.Equal(3, exp.RowCount);

                savedPath = exp.Save(tempFile);

                Assert.CurrentTestName = "ExcelExporter: Save 返回非空路径";
                Assert.True(!string.IsNullOrEmpty(savedPath), "savedPath 不应为空");
                Assert.CurrentTestName = "ExcelExporter: Save 返回路径等于入参";
                Assert.Equal(tempFile, savedPath);
                Assert.CurrentTestName = "ExcelExporter: Save 后文件存在";
                Assert.True(File.Exists(savedPath), "xlsx 文件应存在：" + savedPath);
                Assert.CurrentTestName = "ExcelExporter: Save 后 FilePath 属性非空";
                Assert.True(!string.IsNullOrEmpty(exp.FilePath), "FilePath 应当被设置");
            }

            // 重新打开并校验内容
            using (FileStream fs = new FileStream(savedPath, FileMode.Open, FileAccess.Read))
            {
                XSSFWorkbook wb = new XSSFWorkbook(fs);
                ISheet sheet = wb.GetSheet("测试报告");

                Assert.CurrentTestName = "Round-trip: 工作表名 '测试报告'";
                Assert.NotNull(sheet);
                Assert.CurrentTestName = "Round-trip: 总行数 == 4（1 表头 + 3 数据）";
                Assert.Equal(4, sheet.PhysicalNumberOfRows);

                // 表头
                IRow header = sheet.GetRow(0);
                Assert.CurrentTestName = "Round-trip: 表头首列 == '测试时间'";
                Assert.Equal("测试时间", header.GetCell(0).StringCellValue);
                Assert.CurrentTestName = "Round-trip: 表头第二列 == '温度区间'";
                Assert.Equal("温度区间", header.GetCell(1).StringCellValue);
                Assert.CurrentTestName = "Round-trip: 表头第三列 == '通道号'";
                Assert.Equal("通道号", header.GetCell(2).StringCellValue);
                Assert.CurrentTestName = "Round-trip: 表头第四列 == '工作模式'";
                Assert.Equal("工作模式", header.GetCell(3).StringCellValue);
                Assert.CurrentTestName = "Round-trip: 表头第五列 == '测试项目'";
                Assert.Equal("测试项目", header.GetCell(4).StringCellValue);
                Assert.CurrentTestName = "Round-trip: 表头第六列 == '测试值'";
                Assert.Equal("测试值", header.GetCell(5).StringCellValue);
                Assert.CurrentTestName = "Round-trip: 表头第七列 == '单位'";
                Assert.Equal("单位", header.GetCell(6).StringCellValue);
                Assert.CurrentTestName = "Round-trip: 表头第八列 == '判定结果'";
                Assert.Equal("判定结果", header.GetCell(7).StringCellValue);
                Assert.CurrentTestName = "Round-trip: 表头第九列 == '备注'";
                Assert.Equal("备注", header.GetCell(8).StringCellValue);

                // 第 1 行数据
                IRow row1 = sheet.GetRow(1);
                Assert.CurrentTestName = "Round-trip: r1.TemperatureRange == '常温'";
                Assert.Equal("常温", row1.GetCell(1).StringCellValue);
                Assert.CurrentTestName = "Round-trip: r1.Channel == 1";
                Assert.Equal(1, (int)row1.GetCell(2).NumericCellValue);
                Assert.CurrentTestName = "Round-trip: r1.Mode == 'Mode1'";
                Assert.Equal("Mode1", row1.GetCell(3).StringCellValue);
                Assert.CurrentTestName = "Round-trip: r1.TestItem == '线性最大增益'";
                Assert.Equal("线性最大增益", row1.GetCell(4).StringCellValue);
                Assert.CurrentTestName = "Round-trip: r1.Value == 12.34";
                Assert.True(Math.Abs(row1.GetCell(5).NumericCellValue - 12.34) < 1e-9,
                    "Value 应当是 12.34, got " + row1.GetCell(5).NumericCellValue);
                Assert.CurrentTestName = "Round-trip: r1.Unit == 'dB'";
                Assert.Equal("dB", row1.GetCell(6).StringCellValue);
                Assert.CurrentTestName = "Round-trip: r1.Judgment == 'Pass'";
                Assert.Equal("Pass", row1.GetCell(7).StringCellValue);
                Assert.CurrentTestName = "Round-trip: r1.Remarks == 'gain=12.34'";
                Assert.Equal("gain=12.34", row1.GetCell(8).StringCellValue);

                // 第 2 行（Fail）
                IRow row2 = sheet.GetRow(2);
                Assert.CurrentTestName = "Round-trip: r2.Judgment == 'Fail'";
                Assert.Equal("Fail", row2.GetCell(7).StringCellValue);
                Assert.CurrentTestName = "Round-trip: r2.Value == 99.99";
                Assert.True(Math.Abs(row2.GetCell(5).NumericCellValue - 99.99) < 1e-9,
                    "Value 应当是 99.99");
                Assert.CurrentTestName = "Round-trip: r2.TestItem == '信号带宽'";
                Assert.Equal("信号带宽", row2.GetCell(4).StringCellValue);

                // 第 3 行
                IRow row3 = sheet.GetRow(3);
                Assert.CurrentTestName = "Round-trip: r3.Judgment == 'Pass'";
                Assert.Equal("Pass", row3.GetCell(7).StringCellValue);
                Assert.CurrentTestName = "Round-trip: r3.TemperatureRange == '低温'";
                Assert.Equal("低温", row3.GetCell(1).StringCellValue);

                wb.Close();
            }

            // 清理临时文件
            try { File.Delete(savedPath); } catch { /* ignored */ }
        }

        // ====================================================================
        // (2) TestReportManager 单例流
        // ====================================================================

        private static void TestTestReportManager()
        {
            Console.WriteLine("[Test] TestReportManager 单例 + Record + Save");

            string tempFile = Path.Combine(
                Path.GetTempPath(),
                string.Format(CultureInfo.InvariantCulture,
                    "test_report_manager_{0:yyyyMMdd_HHmmss_fff}.xlsx",
                    DateTime.Now));

            // 确保干净状态
            TestReportManager.Instance.StartNewBatch();
            Assert.CurrentTestName = "TRM: 初始 HasPendingResults == false";
            Assert.True(!TestReportManager.Instance.HasPendingResults,
                "fresh start should be empty");
            Assert.CurrentTestName = "TRM: 初始 PendingRowCount == 0";
            Assert.Equal(0, TestReportManager.Instance.PendingRowCount);

            TestReportManager.Instance.Record(BuildResult("Mgr1", "常温", 5, "Mode1", "线性最大增益", 14.5, "dB", "Pass", "mgr1"));
            TestReportManager.Instance.Record(BuildResult("Mgr2", "常温", 5, "Mode1", "信号带宽",     0.20, "MHz", "Pass", "mgr2"));

            Assert.CurrentTestName = "TRM: Record 后 HasPendingResults == true";
            Assert.True(TestReportManager.Instance.HasPendingResults, "应当存在待导出结果");
            Assert.CurrentTestName = "TRM: PendingRowCount == 2";
            Assert.Equal(2, TestReportManager.Instance.PendingRowCount);

            string savedPath = TestReportManager.Instance.SaveCurrentReport(tempFile);

            Assert.CurrentTestName = "TRM: SaveCurrentReport 返回非空";
            Assert.True(!string.IsNullOrEmpty(savedPath), "savedPath 不应为空");
            Assert.CurrentTestName = "TRM: SaveCurrentReport 返回入参路径";
            Assert.Equal(tempFile, savedPath);
            Assert.CurrentTestName = "TRM: SaveCurrentReport 后文件存在";
            Assert.True(File.Exists(savedPath), "xlsx 应当存在：" + savedPath);

            // 验证文件可以重新打开 + 内容正确
            using (FileStream fs = new FileStream(savedPath, FileMode.Open, FileAccess.Read))
            {
                XSSFWorkbook wb = new XSSFWorkbook(fs);
                ISheet sheet = wb.GetSheet("测试报告");
                Assert.CurrentTestName = "TRM round-trip: 总行数 1+2=3";
                Assert.Equal(3, sheet.PhysicalNumberOfRows);
                Assert.CurrentTestName = "TRM round-trip: Mgr1.TestItem";
                Assert.Equal("线性最大增益", sheet.GetRow(1).GetCell(4).StringCellValue);
                Assert.CurrentTestName = "TRM round-trip: Mgr2.Unit='MHz'";
                Assert.Equal("MHz", sheet.GetRow(2).GetCell(6).StringCellValue);
                wb.Close();
            }

            // Save 之后批次已清空
            Assert.CurrentTestName = "TRM: Save 后 PendingRowCount == 0";
            Assert.Equal(0, TestReportManager.Instance.PendingRowCount);
            Assert.CurrentTestName = "TRM: Save 后 HasPendingResults == false";
            Assert.True(!TestReportManager.Instance.HasPendingResults, "保存后批次应清空");

            // 再调用一次 SaveCurrentReport（在已清空状态下）应返回 null
            string nullPath = TestReportManager.Instance.SaveCurrentReport();
            Assert.CurrentTestName = "TRM: 空状态下 SaveCurrentReport 返回 null";
            Assert.True(nullPath == null, "空状态应返回 null，实际：" + (nullPath ?? "null"));

            try { File.Delete(savedPath); } catch { /* ignored */ }
        }

        // ====================================================================
        // (3) 默认文件名规则
        // ====================================================================

        private static void TestDefaultFileNamePattern()
        {
            Console.WriteLine("[Test] 默认文件名遵守 A205_测试报告_yyyyMMdd_HHmmss.xlsx");

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            using (ExcelExporter exp = new ExcelExporter())
            {
                string defaultPath = exp.GenerateDefaultFilePath();

                Assert.CurrentTestName = "DefaultPath: 文件名以 'A205_测试报告_' 开头";
                Assert.True(
                    Path.GetFileName(defaultPath).StartsWith("A205_测试报告_", StringComparison.Ordinal),
                    "应当是 'A205_测试报告_*.xlsx'，实际：" + Path.GetFileName(defaultPath));
                Assert.CurrentTestName = "DefaultPath: 扩展名 == .xlsx";
                Assert.Equal(".xlsx", Path.GetExtension(defaultPath).ToLowerInvariant());
                Assert.CurrentTestName = "DefaultPath: 位于 bin/reports";
                Assert.True(defaultPath.Contains("reports"),
                    "应当包含 'reports' 子目录，实际：" + defaultPath);
                Assert.CurrentTestName = "DefaultPath: 在 bin/ 下";
                Assert.True(defaultPath.StartsWith(baseDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase),
                    "应当以 BaseDirectory 开头，实际：" + defaultPath);
            }
        }

        // ====================================================================
        // Helpers
        // ====================================================================

        private static TestResult BuildResult(
            string name,
            string tempRange,
            int channel,
            string mode,
            string testItem,
            double value,
            string unit,
            string judgment,
            string remarks)
        {
            // 给每个 TestResult 一个略不同的时间戳，便于 round-trip 校验日期回读
            return new TestResult
            {
                TestTime = DateTime.Now,
                TemperatureRange = tempRange ?? string.Empty,
                Channel = channel,
                Mode = mode ?? string.Empty,
                TestItem = testItem ?? string.Empty,
                Value = value,
                Unit = unit ?? string.Empty,
                Judgment = judgment ?? string.Empty,
                Remarks = remarks ?? string.Empty
            };
        }
    }
}
