using System;

namespace A205AutoTestSystem.Tests.InstrumentTests
{
    /// <summary>
    /// 简易断言辅助类。M4 测试在 .NET Framework 4.8 控制台工程中运行，
    /// 不引入 NUnit / xUnit / MSTest。每个失败累加计数器，统一在
    /// <c>Program.Main</c> 末尾输出 PASS / FAIL。
    /// </summary>
    public static class Assert
    {
        public static int FailureCount { get; private set; }
        public static int TotalCount { get; private set; }
        public static string CurrentTestName { get; set; }

        public static void Reset()
        {
            FailureCount = 0;
            TotalCount = 0;
            CurrentTestName = null;
        }

        public static void True(bool condition, string message = null)
        {
            TotalCount++;
            if (condition) return;
            FailureCount++;
            Console.WriteLine($"  FAIL [{CurrentTestName}] {message ?? "condition was false"}");
        }

        public static void Equal<T>(T expected, T actual) where T : IEquatable<T>
        {
            TotalCount++;
            if (expected == null && actual == null) return;
            if (expected != null && expected.Equals(actual)) return;
            FailureCount++;
            Console.WriteLine($"  FAIL [{CurrentTestName}] expected={expected} actual={actual}");
        }

        public static void Equal(string expected, string actual)
        {
            TotalCount++;
            if (string.Equals(expected, actual, StringComparison.Ordinal)) return;
            FailureCount++;
            Console.WriteLine($"  FAIL [{CurrentTestName}]");
            Console.WriteLine($"    expected: {expected}");
            Console.WriteLine($"    actual:   {actual}");
        }

        public static void Contains(string expectedSubstring, string actual)
        {
            TotalCount++;
            if (actual != null && actual.Contains(expectedSubstring)) return;
            FailureCount++;
            Console.WriteLine($"  FAIL [{CurrentTestName}] expected to contain '{expectedSubstring}'");
            Console.WriteLine($"    actual: {actual}");
        }

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
                Console.WriteLine($"  FAIL [{CurrentTestName}] expected {typeof(T).Name} but got {ex.GetType().Name}: {ex.Message}");
                return;
            }
            FailureCount++;
            Console.WriteLine($"  FAIL [{CurrentTestName}] expected {typeof(T).Name} but no exception was thrown.");
        }

        public static void DoesNotThrow(Action action)
        {
            TotalCount++;
            try
            {
                action();
            }
            catch (Exception ex)
            {
                FailureCount++;
                Console.WriteLine($"  FAIL [{CurrentTestName}] expected no exception but got {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}