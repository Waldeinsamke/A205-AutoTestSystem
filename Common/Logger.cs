using System;
using System.IO;
using System.Text;

namespace A205AutoTestSystem.Common
{
    /// <summary>
    /// 统一日志输出器。提供 UI 事件回调与文件落盘双通道。
    /// 默认落盘到 <c>bin\logs\yyyyMMdd.log</c>。
    /// </summary>
    public static class Logger
    {
        private static readonly object SyncRoot = new object();
        private static string _logDirectory;

        /// <summary>
        /// 任意日志输出后触发，UI 可订阅以实时显示。
        /// </summary>
        public static event Action<string> MessageLogged;

        /// <summary>
        /// 日志输出目录（默认 bin\logs，可在初始化时调整）。
        /// </summary>
        public static string LogDirectory
        {
            get
            {
                if (string.IsNullOrEmpty(_logDirectory))
                {
                    _logDirectory = ResolveDefaultLogDirectory();
                }
                return _logDirectory;
            }
            set
            {
                _logDirectory = value;
                EnsureDirectory(_logDirectory);
            }
        }

        /// <summary>
        /// 写入一条普通日志。
        /// </summary>
        public static void Log(string message)
        {
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}";
            WriteToConsole(line);
            WriteToFile(line);
            RaiseMessageLogged(line);
        }

        /// <summary>
        /// 记录发送的字节数据，自动转 HEX 字符串。
        /// </summary>
        public static void LogSend(byte[] data)
        {
            Log(">> " + ToHex(data));
        }

        /// <summary>
        /// 记录接收的字节数据，自动转 HEX 字符串。
        /// </summary>
        public static void LogRecv(byte[] data)
        {
            Log("<< " + ToHex(data));
        }

        /// <summary>
        /// 字节数组转 HEX 字符串（带空格分隔）。
        /// </summary>
        public static string ToHex(byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                return string.Empty;
            }

            StringBuilder sb = new StringBuilder(data.Length * 3);
            for (int i = 0; i < data.Length; i++)
            {
                sb.Append(data[i].ToString("X2"));
                if (i < data.Length - 1)
                {
                    sb.Append(' ');
                }
            }
            return sb.ToString();
        }

        private static string ResolveDefaultLogDirectory()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string logs = Path.Combine(baseDir, "logs");
            EnsureDirectory(logs);
            return logs;
        }

        private static void EnsureDirectory(string dir)
        {
            try
            {
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
            }
            catch
            {
                // 忽略目录创建失败
            }
        }

        private static void WriteToConsole(string line)
        {
            try
            {
                Console.WriteLine(line);
            }
            catch
            {
                // 非控制台环境可能抛出，忽略
            }
        }

        private static void WriteToFile(string line)
        {
            try
            {
                string dir = LogDirectory;
                string fileName = DateTime.Now.ToString("yyyyMMdd") + ".log";
                string fullPath = Path.Combine(dir, fileName);
                lock (SyncRoot)
                {
                    File.AppendAllText(fullPath, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
                // 落盘失败时静默，避免影响业务调用
            }
        }

        private static void RaiseMessageLogged(string line)
        {
            Action<string> handler = MessageLogged;
            if (handler != null)
            {
                try
                {
                    handler(line);
                }
                catch
                {
                    // 订阅方异常不应影响主流程
                }
            }
        }
    }
}