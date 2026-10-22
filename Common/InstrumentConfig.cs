using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace A205AutoTestSystem.Common
{
    /// <summary>
    /// 单台仪表的连接健康状态。拓扑图节点按此三态着色。
    /// </summary>
    public enum InstrumentLinkState
    {
        /// <summary>从未连接上（或已正常断开），无异常。</summary>
        Idle = 0,

        /// <summary>已连接且通信正常。</summary>
        Ready = 1,

        /// <summary>
        /// 出现异常：配置地址为空、连接抛异常、掉线后探测失败，或连接期间掉线。
        /// 该状态下节点标红，是操作工最需要关注的告警。
        /// </summary>
        Fault = 2
    }

    /// <summary>
    /// 仪表 VISA 地址配置（对应 <c>config/Instruments.xml</c>）。
    /// <para>设计要点：</para>
    /// <list type="bullet">
    /// <item>配置文件独立于可执行程序，产线换仪表时直接改文本，无需重新编译。</item>
    /// <item>加载失败不抛异常：返回空结果并由调用方按“地址为空”标记故障，
    /// 避免配置问题导致程序直接崩溃而看不到任何提示。</item>
    /// </list>
    /// </summary>
    public class InstrumentConfig
    {
        private const string RootElementName = "Instruments";
        private const string ResourceNameAttribute = "resourceName";

        /// <summary>RF 信号源 VISA 资源地址。</summary>
        public string RfGeneratorAddress { get; set; } = string.Empty;

        /// <summary>LO 信号源 VISA 资源地址。</summary>
        public string LoGeneratorAddress { get; set; } = string.Empty;

        /// <summary>频谱仪 VISA 资源地址。</summary>
        public string SpectrumAnalyzerAddress { get; set; } = string.Empty;

        /// <summary>直流电源 VISA 资源地址。</summary>
        public string PowerSupplyAddress { get; set; } = string.Empty;

        /// <summary>本次加载是否成功（文件存在且格式正确）。</summary>
        public bool Loaded { get; set; }

        /// <summary>配置文件绝对路径。</summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>加载失败原因，成功时为空字符串。</summary>
        public string LoadError { get; set; } = string.Empty;

        /// <summary>
        /// 配置文件路径：优先取可执行程序目录旁的 <c>config\Instruments.xml</c>，
        /// 该文件随部署拷贝、可被用户直接编辑。
        /// </summary>
        public static string DefaultConfigPath => Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "config", "Instruments.xml");

        /// <summary>
        /// 从指定路径加载配置。任何异常都被吞掉并记录到 <see cref="LoadError"/>，
        /// 保证配置问题不会让程序崩溃。
        /// </summary>
        public static InstrumentConfig Load(string path = null)
        {
            string configPath = string.IsNullOrWhiteSpace(path) ? DefaultConfigPath : path;
            InstrumentConfig config = new InstrumentConfig { FilePath = configPath };

            try
            {
                if (!File.Exists(configPath))
                {
                    config.LoadError = "配置文件不存在：" + configPath;
                    Logger.Log("[Error] 仪表配置文件缺失: " + configPath);
                    return config;
                }

                XDocument doc = XDocument.Load(configPath);
                XElement root = doc.Root;
                if (root == null || root.Name.LocalName != RootElementName)
                {
                    config.LoadError = $"配置文件根元素应为 <{RootElementName}>：{configPath}";
                    Logger.Log("[Error] " + config.LoadError);
                    return config;
                }

                config.RfGeneratorAddress = ReadResourceName(root, "RfGenerator");
                config.LoGeneratorAddress = ReadResourceName(root, "LoGenerator");
                config.SpectrumAnalyzerAddress = ReadResourceName(root, "SpectrumAnalyzer");
                config.PowerSupplyAddress = ReadResourceName(root, "PowerSupply");
                config.Loaded = true;

                Logger.Log(
                    $"[UI] 仪表配置已加载: RF=\"{config.RfGeneratorAddress}\" " +
                    $"LO=\"{config.LoGeneratorAddress}\" " +
                    $"SA=\"{config.SpectrumAnalyzerAddress}\" " +
                    $"PWR=\"{config.PowerSupplyAddress}\"");
                return config;
            }
            catch (Exception ex)
            {
                config.LoadError = "配置文件读取失败：" + ex.Message;
                Logger.Log("[Error] " + config.LoadError);
                return config;
            }
        }

        /// <summary>
        /// 读取子元素的 <c>resourceName</c> 属性。元素缺失或属性为空时返回空字符串，
        /// 由调用方按“地址为空”处理。
        /// </summary>
        private static string ReadResourceName(XElement root, string elementName)
        {
            XElement element = root.Elements()
                .FirstOrDefault(e => string.Equals(e.Name.LocalName, elementName, StringComparison.OrdinalIgnoreCase));

            if (element == null)
            {
                Logger.Log($"[Warn] 仪表配置缺少 <{elementName}> 节点，按地址为空处理。");
                return string.Empty;
            }

            XAttribute attr = element.Attribute(ResourceNameAttribute);
            return attr == null ? string.Empty : attr.Value.Trim();
        }
    }
}