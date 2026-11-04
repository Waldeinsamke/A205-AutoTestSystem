using System;
using System.Reflection;
using System.Runtime.Serialization;
using A205AutoTestSystem.InstrumentControl.TestItems;

namespace A205AutoTestSystem.Tests.AutoTestTests
{
    /// <summary>
    /// 防回归测试：<c>FailResult</c> 工厂方法生成的失败行<b>必须记入报表</b>。
    /// <para><b>缺陷背景（docs/11.3审查代码报告.md #2）</b>：
    /// <c>InputP1Test</c> / <c>SaturatedOutputTest</c> / <c>HarmonicRejectionTest</c> /
    /// <c>InBandSpuriousTest</c> 的 <c>FailResult</c> 只 <c>return</c> 一个
    /// <c>TestResult</c>，从不调用 <c>TestReportManager.Instance.Record(...)</c>。
    /// UI 层也不补救（<c>AutoTestPanel</c> 的注释假定"测试项内部已 Record"），
    /// 于是读数失败的频点在导出 Excel 中<b>整行消失</b>——
    /// 报表行数少于实际执行的频点数，审核无法发现"某些频点根本没测出来"，
    /// 形成<b>漏判即通过</b>的风险。</para>
    /// <para><b>为什么用反射直接调用 private 方法</b>：
    /// 这 4 条失败分支的前置条件是"仪表读数失败"，在单元测试里稳定复现需要
    /// 完整的仪表桩 + 串口桩；而本缺陷的本质是"<c>FailResult</c> 是否调用了
    /// <c>Record</c>"这一行为契约，与触发路径无关。直接验证契约既精确又能
    /// 覆盖全部 4 个测试项。若将来有人再次漏掉 <c>Record</c>，本测试立刻失败。</para>
    /// </summary>
    public static class FailResultRecordingTests
    {
        /// <summary>
        /// 待验证的测试项类型 → <c>FailResult</c> 方法名。
        /// <para>4 个测试项的 <c>FailResult</c> 参数签名各不相同（3 参 / 5 参 / 6 参 / 7 参），
        /// 这里按各自签名分别构造实参。</para>
        /// </summary>
        private static readonly Type[] TestedTypes =
        {
            typeof(InputP1Test),
            typeof(SaturatedOutputTest),
            typeof(HarmonicRejectionTest),
            typeof(InBandSpuriousTest),
        };

        /// <summary>
        /// 执行全部断言。
        /// </summary>
        public static void Run()
        {
            Console.WriteLine("[Test] FailResult 失败行必须记入报表（docs/11.3审查代码报告.md #2）");

            foreach (Type t in TestedTypes)
            {
                AssertOneTypeRecordsFailResult(t);
            }

            AssertRealWorldScenarioRecordsFailRow();
        }

        /// <summary>
        /// 断言某个测试项的 <c>FailResult</c> 调用后，报表行数恰好 +1。
        /// </summary>
        /// <param name="type">测试项类型。</param>
        private static void AssertOneTypeRecordsFailResult(Type type)
        {
            string typeName = type.Name;

            // FailResult 是 private 工厂方法：用反射取到它。
            MethodInfo factory = type.GetMethod(
                "FailResult",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.CurrentTestName = $"FailResult: {typeName} 存在 FailResult 工厂方法";
            Assert.NotNull(factory, $"{typeName} 应当有 FailResult 方法");
            if (factory == null)
            {
                return;
            }

            // 用未初始化的实例调用：FailResult 只用到 _channel / TestName / Unit 等
            // 纯字段与常量，不触碰仪表与串口，构造函数是否执行不影响被测行为。
            object instance = FormatterServices.GetUninitializedObject(type);

            // 按各类型的参数签名构造实参（都是不可变标量，安全）。
            object[] args = BuildArgs(factory, typeName);

            A205AutoTestSystem.Common.TestReportManager.Instance.StartNewBatch();
            int before = A205AutoTestSystem.Common.TestReportManager.Instance.PendingRowCount;

            object result = null;
            Exception invokeError = null;
            try
            {
                result = factory.Invoke(instance, args);
            }
            catch (TargetInvocationException tie)
            {
                invokeError = tie.InnerException ?? tie;
            }
            catch (Exception ex)
            {
                invokeError = ex;
            }

            Assert.CurrentTestName = $"FailResult: {typeName} 调用不抛异常";
            if (invokeError != null)
            {
                Assert.True(false, $"{typeName}.FailResult 抛出异常: {invokeError.Message}");
                return;
            }

            Assert.CurrentTestName = $"FailResult: {typeName} 返回非 null TestResult";
            Assert.NotNull(result, $"{typeName}.FailResult 应当返回 TestResult");
            if (result == null)
            {
                return;
            }

            int after = A205AutoTestSystem.Common.TestReportManager.Instance.PendingRowCount;

            Assert.CurrentTestName =
                $"FailResult: {typeName} 生成的失败行已记入报表（行数 +1，防漏判即通过）";
            Assert.Equal(before + 1, after);

            // 同时校验落库内容：判定列必须是 Fail，否则报表里的"失败行"没有意义。
            var tr = result as A205AutoTestSystem.Models.TestResult;
            Assert.CurrentTestName = $"FailResult: {typeName} 返回结果的判定为 Fail";
            Assert.NotNull(tr);
            if (tr != null)
            {
                Assert.Equal("Fail", tr.Judgment);
            }
        }

        /// <summary>
        /// 端到端视角：失败行不仅进了报表，而且导出到 xlsx 后仍占一行——
        /// 这才是"审核能看到漏测频点"的意义所在。
        /// </summary>
        private static void AssertRealWorldScenarioRecordsFailRow()
        {
            A205AutoTestSystem.Common.TestReportManager.Instance.StartNewBatch();

            // 走一条真实的失败路径：InBandSpuriousTest 传入 Mode3 频段表里不存在的频点，
            // 触发"未找到频段"失败分支。此处直接调其 FailResult 工厂（同上，验证契约）。
            Type t = typeof(InBandSpuriousTest);
            MethodInfo factory = t.GetMethod(
                "FailResult",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (factory == null)
            {
                Assert.CurrentTestName = "FailResult: 端到端场景可定位 InBandSpuriousTest.FailResult";
                Assert.True(false, "InBandSpuriousTest 应当有 FailResult 方法");
                return;
            }

            object instance = FormatterServices.GetUninitializedObject(t);
            factory.Invoke(instance, new object[] { 1234.0, "未在 ModeBandTable 中找到对应频段" });

            string tempFile = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "failresult_recording_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".xlsx");
            string saved = A205AutoTestSystem.Common.TestReportManager.Instance.SaveCurrentReport(tempFile);

            Assert.CurrentTestName = "FailResult: 失败行可导出为 xlsx";
            Assert.True(!string.IsNullOrEmpty(saved) && System.IO.File.Exists(saved),
                "xlsx 应当存在：" + saved);

            if (!string.IsNullOrEmpty(saved) && System.IO.File.Exists(saved))
            {
                using (var fs = new System.IO.FileStream(saved, System.IO.FileMode.Open, System.IO.FileAccess.Read))
                {
                    var wb = new NPOI.XSSF.UserModel.XSSFWorkbook(fs);
                    var sheet = wb.GetSheet("测试报告");

                    Assert.CurrentTestName = "FailResult: xlsx 中失败行未被丢弃（表头 + 1 行）";
                    Assert.Equal(2, sheet.PhysicalNumberOfRows);

                    var row = sheet.GetRow(1);
                    Assert.CurrentTestName = "FailResult: xlsx 失败行判定列为 Fail";
                    if (row != null)
                    {
                        Assert.Equal("Fail", row.GetCell(9).StringCellValue);
                        Assert.CurrentTestName = "FailResult: xlsx 失败行备注保留了失败原因";
                        Assert.Contains("未在 ModeBandTable", row.GetCell(10).StringCellValue);
                    }

                    wb.Close();
                }
                try { System.IO.File.Delete(saved); } catch { /* ignored */ }
            }
        }

        /// <summary>
        /// 按 <c>FailResult</c> 的参数签名构造实参。
        /// <para>4 个测试项签名不同：
        /// <c>HarmonicRejectionTest</c> 是 5 参（含 freqMHz），
        /// <c>InBandSpuriousTest</c> 是 2 参（freqMHz + reason），
        /// 其余为多参版本。用参数个数 + 类型匹配来定位，避免硬编码顺序出错。</para>
        /// </summary>
        private static object[] BuildArgs(MethodInfo factory, string typeName)
        {
            System.Reflection.ParameterInfo[] ps = factory.GetParameters();

            // 通用做法：按参数名语义填值——freq/freqMHz → 频率，reason → 原因，
            // 其余（value / ifFreqHz / rfStartPowerDbm / refPeak / finalDelta）→ 数值占位。
            object[] args = new object[ps.Length];
            for (int i = 0; i < ps.Length; i++)
            {
                string pname = ps[i].Name ?? string.Empty;
                Type pt = ps[i].ParameterType;

                if (pname.IndexOf("reason", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    args[i] = "未在 ModeBandTable 中找到对应频段";
                }
                else if (pname.IndexOf("finalDelta", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    args[i] = "0.0/0.0";
                }
                else if (pt == typeof(double))
                {
                    // freqMHz 走一个合法频点，其余数值占位（NaN 表示"无读数"）
                    if (pname.IndexOf("freqMHz", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        args[i] = 108.0;
                    }
                    else if (pname.IndexOf("ifFreqHz", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        args[i] = 750_000_000.0;
                    }
                    else if (pname.IndexOf("rfStartPowerDbm", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        args[i] = -20.0;
                    }
                    else if (pname.IndexOf("refPeak", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        args[i] = -10.0;
                    }
                    else
                    {
                        args[i] = double.NaN;   // value
                    }
                }
                else if (pt == typeof(string))
                {
                    args[i] = "未在 ModeBandTable 中找到对应频段";
                }
                else
                {
                    args[i] = null;
                }
            }
            return args;
        }
    }
}
