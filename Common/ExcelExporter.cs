using System;
using System.Globalization;
using System.IO;
using A205AutoTestSystem.InstrumentControl;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace A205AutoTestSystem.Common
{
    /// <summary>
    /// Excel 测试报告导出器（M7 阶段实现）。
    /// <para>基于 NPOI 2.6.0 生成 .xlsx（Office Open XML）。</para>
    /// <para>使用方式：</para>
    /// <code>
    /// using (var exp = new ExcelExporter())
    /// {
    ///     exp.AppendResult(result1);
    ///     exp.AppendResult(result2);
    ///     string path = exp.Save();   // 不传路径则按默认规则生成
    /// }
    /// </code>
    /// <para>默认文件命名 <c>A205_测试报告_yyyyMMdd_HHmmss.xlsx</c>，
    /// 保存到 <c>bin\reports\</c> 目录下（见 <see cref="GenerateDefaultFilePath"/>）。</para>
    /// <para>表头字段对齐规划书 §6.1（共 9 列）。</para>
    /// </summary>
    public class ExcelExporter : IDisposable
    {
        /// <summary>
        /// 表头字段。顺序与规划书 §6.1 一致。
        /// </summary>
        private static readonly string[] Headers =
        {
            "测试时间",
            "温度区间",
            "通道号",
            "工作模式",
            "测试项目",
            "测试值",
            "单位",
            "判定结果",
            "备注"
        };

        private XSSFWorkbook _workbook;
        private ISheet _sheet;
        private IRow _headerRow;
        private int _rowCount;
        private string _filePath;
        private bool _disposed;

        /// <summary>当前已保存到的文件路径（首次 Save 后非 null）。</summary>
        public string FilePath => _filePath;

        /// <summary>当前工作簿已追加的数据行数（不含表头）。</summary>
        public int RowCount => _rowCount - 1;

        /// <summary>
        /// 构造一个内存中的 .xlsx 工作簿，初始化表头行。
        /// </summary>
        public ExcelExporter()
        {
            _workbook = new XSSFWorkbook();
            _sheet = _workbook.CreateSheet("测试报告");

            // 表头样式：粗体 + 浅灰底色（按规划书无强制要求，做基础美化）
            IFont boldFont = _workbook.CreateFont();
            boldFont.IsBold = true;

            ICellStyle headerStyle = _workbook.CreateCellStyle();
            headerStyle.SetFont(boldFont);
            // NPOI 2.6 的 IndexedColors 没有 LightGrey；使用 Grey50Percent (short=22)
            headerStyle.FillForegroundColor = (short)NPOI.SS.UserModel.IndexedColors.Grey50Percent.Index;
            headerStyle.FillPattern = FillPattern.SolidForeground;
            headerStyle.Alignment = HorizontalAlignment.Center;

            _headerRow = _sheet.CreateRow(0);
            for (int i = 0; i < Headers.Length; i++)
            {
                ICell cell = _headerRow.CreateCell(i);
                cell.SetCellValue(Headers[i]);
                cell.CellStyle = headerStyle;
            }
            _rowCount = 1;

            // 列宽自适应（简单做法：固定宽度）
            for (int c = 0; c < Headers.Length; c++)
            {
                _sheet.SetColumnWidth(c, 18 * 256);
            }
            // 备注列宽一点
            _sheet.SetColumnWidth(8, 36 * 256);

            // 冻结首行
            _sheet.CreateFreezePane(0, 1);
        }

        /// <summary>
        /// 追加一行测试结果（不立即落盘；调用 <see cref="Save"/> 才会生成 .xlsx）。
        /// </summary>
        public void AppendResult(TestResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            EnsureNotDisposed();

            IRow row = _sheet.CreateRow(_rowCount);
            _rowCount++;

            // 0: 测试时间 — DateTime 单元格，使用 "yyyy-MM-dd HH:mm:ss" 格式
            ICell timeCell = row.CreateCell(0);
            timeCell.SetCellValue(result.TestTime);
            ICellStyle timeStyle = _workbook.CreateCellStyle();
            timeStyle.DataFormat = _workbook.GetCreationHelper()
                .CreateDataFormat().GetFormat("yyyy-MM-dd HH:mm:ss");
            timeCell.CellStyle = timeStyle;

            // 1: 温度区间
            row.CreateCell(1).SetCellValue(SafeStr(result.TemperatureRange));
            // 2: 通道号
            row.CreateCell(2).SetCellValue(result.Channel);
            // 3: 工作模式
            row.CreateCell(3).SetCellValue(SafeStr(result.Mode));
            // 4: 测试项目
            row.CreateCell(4).SetCellValue(SafeStr(result.TestItem));
            // 5: 测试值（double）
            row.CreateCell(5).SetCellValue(result.Value);
            // 6: 单位
            row.CreateCell(6).SetCellValue(SafeStr(result.Unit));

            // 7: 判定结果 — Pass 绿色，Fail 红色
            ICell judgmentCell = row.CreateCell(7);
            judgmentCell.SetCellValue(SafeStr(result.Judgment));
            string judgment = SafeStr(result.Judgment);
            if (judgment == "Pass")
            {
                ICellStyle passStyle = _workbook.CreateCellStyle();
                IFont passFont = _workbook.CreateFont();
                passFont.Color = NPOI.SS.UserModel.IndexedColors.Green.Index;
                passFont.IsBold = true;
                passStyle.SetFont(passFont);
                judgmentCell.CellStyle = passStyle;
            }
            else if (judgment == "Fail")
            {
                ICellStyle failStyle = _workbook.CreateCellStyle();
                IFont failFont = _workbook.CreateFont();
                failFont.Color = NPOI.SS.UserModel.IndexedColors.Red.Index;
                failFont.IsBold = true;
                failStyle.SetFont(failFont);
                judgmentCell.CellStyle = failStyle;
            }

            // 8: 备注
            row.CreateCell(8).SetCellValue(SafeStr(result.Remarks));

            Logger.Log(string.Format(
                CultureInfo.InvariantCulture,
                "[Excel] 追加一行：{0} = {1:F4} {2} → {3}",
                SafeStr(result.TestItem), result.Value, SafeStr(result.Unit), SafeStr(result.Judgment)));
        }

        /// <summary>
        /// 将当前工作簿落盘为 .xlsx。
        /// <para>若 <paramref name="filePath"/> 为 null 或空，则按
        /// <see cref="GenerateDefaultFilePath"/> 规则生成默认路径。</para>
        /// <para>已存在同名文件将被覆盖（M7 阶段不锁文件，避免测试期间长开 Excel 阻塞写入）。</para>
        /// </summary>
        /// <returns>实际保存到的绝对路径。</returns>
        public string Save(string filePath = null)
        {
            EnsureNotDisposed();

            string target = string.IsNullOrWhiteSpace(filePath) ? GenerateDefaultFilePath() : filePath;
            string dir = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            using (FileStream fs = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                _workbook.Write(fs);
            }

            _filePath = target;
            Logger.Log("[Excel] 已保存到 " + target);
            return target;
        }

        /// <summary>
        /// 按规划书 §6.2 规则生成默认文件路径：
        /// <c>bin\reports\A205_测试报告_yyyyMMdd_HHmmss.xlsx</c>。
        /// </summary>
        public string GenerateDefaultFilePath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string reportsDir = Path.Combine(baseDir, "reports");
            if (!Directory.Exists(reportsDir))
            {
                Directory.CreateDirectory(reportsDir);
            }
            string fileName = string.Format(
                CultureInfo.InvariantCulture,
                "A205_测试报告_{0:yyyyMMdd_HHmmss}.xlsx",
                DateTime.Now);
            return Path.Combine(reportsDir, fileName);
        }

        /// <summary>
        /// 释放 NPOI 工作簿。可重复调用。
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                if (_workbook != null)
                {
                    _workbook.Close();
                    _workbook = null;
                }
            }
            catch
            {
                // 释放失败不影响整体退出
            }
            GC.SuppressFinalize(this);
        }

        private void EnsureNotDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ExcelExporter));
            if (_workbook == null || _sheet == null)
                throw new InvalidOperationException("ExcelExporter 工作簿已失效。");
        }

        private static string SafeStr(string s)
        {
            return s ?? string.Empty;
        }
    }
}
