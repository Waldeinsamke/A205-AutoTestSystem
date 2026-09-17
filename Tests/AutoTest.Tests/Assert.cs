using System;

namespace A205AutoTestSystem.Tests.AutoTestTests
{
    /// <summary>
    /// 简易断言辅助类（M6 阶段）。
    /// 与 <c>Tests/Instrument.Tests.Assert</c> 同款逻辑；
    /// 独立命名空间避免与其它测试工程产生类型冲突（编译时每个测试工程独立）。
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

        public static void NotNull(object obj, string message = null)
        {
            TotalCount++;
            if (obj != null) return;
            FailureCount++;
            Console.WriteLine($"  FAIL [{CurrentTestName}] {message ?? "object was null"}");
        }

        public static void NotEqual<T>(T notExpected, T actual) where T : IEquatable<T>
        {
            TotalCount++;
            if (notExpected == null && actual == null)
            {
                FailureCount++;
                Console.WriteLine($"  FAIL [{CurrentTestName}] both are null");
                return;
            }
            if (notExpected == null) return;
            if (!notExpected.Equals(actual)) return;
            FailureCount++;
            Console.WriteLine($"  FAIL [{CurrentTestName}] both = {actual}");
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