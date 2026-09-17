using System;

namespace A205AutoTestSystem.Tests.BuilderTests
{
    /// <summary>
    /// 简易断言辅助类。M2 测试在 .NET Framework 4.8 控制台工程中运行，
    /// 不引入 NUnit / xUnit / MSTest。每个失败累加计数器，统一在
    /// <c>Program.Main</c> 末尾输出 PASS / FAIL。
    /// </summary>
    public static class Assert
    {
        /// <summary>
        /// 累计失败次数。
        /// </summary>
        public static int FailureCount { get; private set; }

        /// <summary>
        /// 累计总断言次数。
        /// </summary>
        public static int TotalCount { get; private set; }

        /// <summary>
        /// 当前测试用例名（用于失败时打印上下文）。
        /// </summary>
        public static string CurrentTestName { get; set; }

        /// <summary>
        /// 重置计数器。
        /// </summary>
        public static void Reset()
        {
            FailureCount = 0;
            TotalCount = 0;
            CurrentTestName = null;
        }

        /// <summary>
        /// 断言两个字节数组完全相等（按顺序）。
        /// </summary>
        public static void Equal(byte[] expected, byte[] actual)
        {
            TotalCount++;
            if (BytesEqual(expected, actual))
            {
                return;
            }
            FailureCount++;
            Console.WriteLine(
                $"  FAIL [{CurrentTestName}] expected={{{Hex(expected)}}} actual={{{Hex(actual)}}}");
        }

        /// <summary>
        /// 断言两个二维字节数组（即 byte[][]）完全相等。
        /// 用于命令序列比对。
        /// </summary>
        public static void Equal(byte[][] expected, byte[][] actual)
        {
            TotalCount++;
            if (SequenceEqual(expected, actual))
            {
                return;
            }
            FailureCount++;
            Console.WriteLine(
                $"  FAIL [{CurrentTestName}] expected sequence len={len(expected)} actual len={len(actual)}");
            Console.WriteLine($"    expected: {SequenceToString(expected)}");
            Console.WriteLine($"    actual:   {SequenceToString(actual)}");
        }

        /// <summary>
        /// 断言执行 <paramref name="action"/> 会抛出 <typeparamref name="T"/> 类型的异常。
        /// </summary>
        public static void Throws<T>(Action action) where T : Exception
        {
            TotalCount++;
            try
            {
                action();
            }
            catch (T)
            {
                return;
            }
            catch (Exception ex)
            {
                FailureCount++;
                Console.WriteLine(
                    $"  FAIL [{CurrentTestName}] expected exception {typeof(T).Name} but got {ex.GetType().Name}: {ex.Message}");
                return;
            }

            FailureCount++;
            Console.WriteLine(
                $"  FAIL [{CurrentTestName}] expected exception {typeof(T).Name} but no exception was thrown.");
        }

        /// <summary>
        /// 断言条件为真。
        /// </summary>
        public static void True(bool condition, string message = null)
        {
            TotalCount++;
            if (condition)
            {
                return;
            }
            FailureCount++;
            Console.WriteLine(
                $"  FAIL [{CurrentTestName}] {message ?? "condition was false"}");
        }

        private static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }

        private static bool SequenceEqual(byte[][] a, byte[][] b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (!BytesEqual(a[i], b[i])) return false;
            }
            return true;
        }

        private static int len(byte[][] seq)
        {
            return seq == null ? 0 : seq.Length;
        }

        private static string Hex(byte[] bytes)
        {
            if (bytes == null) return "null";
            System.Text.StringBuilder sb = new System.Text.StringBuilder(bytes.Length * 3);
            for (int i = 0; i < bytes.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(bytes[i].ToString("X2"));
            }
            return sb.ToString();
        }

        private static string SequenceToString(byte[][] seq)
        {
            if (seq == null) return "null";
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < seq.Length; i++)
            {
                if (i > 0) sb.Append(" | ");
                sb.Append(Hex(seq[i]));
            }
            return sb.ToString();
        }
    }
}