using System;
using System.Collections.Generic;

namespace A205AutoTestSystem.ReceiverControl
{
    /// <summary>
    /// 接收机工作模式。
    /// </summary>
    public enum ReceiverMode
    {
        Mode1 = 1,
        Mode2 = 2,
        Mode3 = 3
    }

    /// <summary>
    /// 温度区间。
    /// </summary>
    public enum TemperatureRange
    {
        Normal = 0,
        High = 1,
        Low = 2
    }

    /// <summary>
    /// 各工作模式下频点（频段）命名空间。
    /// 源数据严格来自 <c>控制命令.md</c>：
    /// <list type="bullet">
    /// <item>模式 1：18 个频点（bandIndex 0..17，BN 0x00..0x11）</item>
    /// <item>模式 2：20 个频点（bandIndex 0..19，BN 0x00..0x13）</item>
    /// <item>模式 3：22 个频点（bandIndex 3..24，BN 0x03..0x18）</item>
    /// </list>
    /// 提供给 UI 显示、参数校验与测试遍历。
    /// </summary>
    public static class ModeBandTable
    {
        /// <summary>
        /// 单个频点的描述信息。
        /// </summary>
        public sealed class BandInfo
        {
            public int BandIndex { get; }
            public byte BandNumber { get; }
            public string DisplayName { get; }

            public BandInfo(int bandIndex, byte bandNumber, string displayName)
            {
                BandIndex = bandIndex;
                BandNumber = bandNumber;
                DisplayName = displayName;
            }

            public override string ToString()
            {
                return $"{DisplayName}(0x{BandNumber:X2})";
            }
        }

        private static readonly Dictionary<ReceiverMode, IReadOnlyList<BandInfo>> _bands =
            new Dictionary<ReceiverMode, IReadOnlyList<BandInfo>>
            {
                {
                    ReceiverMode.Mode1, new List<BandInfo>
                    {
                        new BandInfo(0,  0x00, "频段108"),
                        new BandInfo(1,  0x01, "频段124"),
                        new BandInfo(2,  0x02, "频段140"),
                        new BandInfo(3,  0x03, "频段156"),
                        new BandInfo(4,  0x04, "频段172"),
                        new BandInfo(5,  0x05, "频段188"),
                        new BandInfo(6,  0x06, "频段204"),
                        new BandInfo(7,  0x07, "频段220"),
                        new BandInfo(8,  0x08, "频段240"),
                        new BandInfo(9,  0x09, "频段255"),
                        new BandInfo(10, 0x0A, "频段270"),
                        new BandInfo(11, 0x0B, "频段290"),
                        new BandInfo(12, 0x0C, "频段305"),
                        new BandInfo(13, 0x0D, "频段320"),
                        new BandInfo(14, 0x0E, "频段340"),
                        new BandInfo(15, 0x0F, "频段355"),
                        new BandInfo(16, 0x10, "频段370"),
                        new BandInfo(17, 0x11, "频段400"),
                    }
                },
                {
                    ReceiverMode.Mode2, new List<BandInfo>
                    {
                        new BandInfo(0,  0x00, "频段960"),
                        new BandInfo(1,  0x01, "频段985"),
                        new BandInfo(2,  0x02, "频段1030"),
                        new BandInfo(3,  0x03, "频段1055"),
                        new BandInfo(4,  0x04, "频段1090"),
                        new BandInfo(5,  0x05, "频段1125"),
                        new BandInfo(6,  0x06, "频段1150"),
                        new BandInfo(7,  0x07, "频段1190"),
                        new BandInfo(8,  0x08, "频段1224"),
                        new BandInfo(9,  0x09, "频段1250"),
                        new BandInfo(10, 0x0A, "频段1300"),
                        new BandInfo(11, 0x0B, "频段1330"),
                        new BandInfo(12, 0x0C, "频段1360"),
                        new BandInfo(13, 0x0D, "频段1390"),
                        new BandInfo(14, 0x0E, "频段1430"),
                        new BandInfo(15, 0x0F, "频段1450"),
                        new BandInfo(16, 0x10, "频段1500"),
                        new BandInfo(17, 0x11, "频段1530"),
                        new BandInfo(18, 0x12, "频段1550"),
                        new BandInfo(19, 0x13, "频段1600"),
                    }
                },
                {
                    ReceiverMode.Mode3, new List<BandInfo>
                    {
                        new BandInfo(3,  0x03, "频段800"),
                        new BandInfo(4,  0x04, "频段900"),
                        new BandInfo(5,  0x05, "频段1000"),
                        new BandInfo(6,  0x06, "频段1100"),
                        new BandInfo(7,  0x07, "频段1300"),
                        new BandInfo(8,  0x08, "频段1400"),
                        new BandInfo(9,  0x09, "频段1500"),
                        new BandInfo(10, 0x0A, "频段1700"),
                        new BandInfo(11, 0x0B, "频段1800"),
                        new BandInfo(12, 0x0C, "频段2000"),
                        new BandInfo(13, 0x0D, "频段2100"),
                        new BandInfo(14, 0x0E, "频段2200"),
                        new BandInfo(15, 0x0F, "频段2300"),
                        new BandInfo(16, 0x10, "频段2500"),
                        new BandInfo(17, 0x11, "频段2600"),
                        new BandInfo(18, 0x12, "频段2700"),
                        new BandInfo(19, 0x13, "频段2900"),
                        new BandInfo(20, 0x14, "频段3000"),
                        new BandInfo(21, 0x15, "频段3100"),
                        new BandInfo(22, 0x16, "频段3300"),
                        new BandInfo(23, 0x17, "频段3400"),
                        new BandInfo(24, 0x18, "频段3500"),
                    }
                },
            };

        /// <summary>
        /// 获取指定模式下所有频点（按 bandIndex 升序）。
        /// </summary>
        public static IReadOnlyList<BandInfo> GetBands(ReceiverMode mode)
        {
            if (!_bands.TryGetValue(mode, out var list))
            {
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "未知的工作模式。");
            }
            return list;
        }

        /// <summary>
        /// 判断 bandIndex 是否落在指定模式的合法范围内。
        /// </summary>
        public static bool IsValidBandIndex(ReceiverMode mode, int bandIndex)
        {
            if (!_bands.TryGetValue(mode, out var list))
            {
                return false;
            }
            foreach (var b in list)
            {
                if (b.BandIndex == bandIndex)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 根据 bandIndex 取出对应的 BN 字节（0x00..0x17）。
        /// </summary>
        public static bool TryGetBandByte(ReceiverMode mode, int bandIndex, out byte bandByte)
        {
            bandByte = 0;
            if (!_bands.TryGetValue(mode, out var list))
            {
                return false;
            }
            foreach (var b in list)
            {
                if (b.BandIndex == bandIndex)
                {
                    bandByte = b.BandNumber;
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// 接收机命令构建器。集中所有命令组装逻辑，
    /// 协议变更只需修改本类（M2：完整覆盖全部命令族）。
    /// <para>所有命令帧统一格式：<c>0x0D [cmd] [data1] [data2] [data3] [data4] 0x0A</c>，共 7 字节。</para>
    /// </summary>
    public class ReceiverCommandBuilder
    {
        /// <summary>帧头固定 0x0D。</summary>
        public const byte FrameHeader = 0x0D;
        /// <summary>帧尾固定 0x0A。</summary>
        public const byte FrameTail = 0x0A;

        // ---------- 静态映射表（数据源：控制命令.md）----------

        // 工作模式 → 模式选择命令第一字节（0x31 命令中的 modeByte）
        // 模式 1 / 模式 2 均为 0x00，模式 3 为 0x03
        private static readonly Dictionary<ReceiverMode, byte> _modeSelectByteMap =
            new Dictionary<ReceiverMode, byte>
            {
                { ReceiverMode.Mode1, 0x00 },
                { ReceiverMode.Mode2, 0x00 },
                { ReceiverMode.Mode3, 0x03 },
            };

        // 工作模式 → 0x11 通道选择命令第三字节（无衰减状态通道选择参数）
        // 同一模式下 8 个通道取值相同，与通道号无关。
        // 数据源：控制命令.md § 切换模式N无衰减状态通道选择参数
        // 注：文档中 Mode1 通道1 记录为 0x02，已与需求方确认为笔误，正确值为 0x03。
        private static readonly Dictionary<ReceiverMode, byte> _channelSelectParamByteMap =
            new Dictionary<ReceiverMode, byte>
            {
                { ReceiverMode.Mode1, 0x03 },
                { ReceiverMode.Mode2, 0x02 },
                { ReceiverMode.Mode3, 0x04 },
            };

        // 温度区间 → 0x30 命令第二字节
        // 0=常温 / 1=高温 / 2=低温
        private static readonly Dictionary<TemperatureRange, byte> _temperatureByteMap =
            new Dictionary<TemperatureRange, byte>
            {
                { TemperatureRange.Normal, 0x00 },
                { TemperatureRange.High,   0x01 },
                { TemperatureRange.Low,    0x02 },
            };

        // 衰减档位（dB）→ 0x10 命令第三字节（实际衰减命令的 DATAFIELD）
        // 数据源：控制命令.md § 衰减命令
        private static readonly Dictionary<int, byte> _attenuationFirstByteMap =
            new Dictionary<int, byte>
            {
                { 0,  0x00 },
                { 4,  0x02 },
                { 8,  0x04 },
                { 12, 0x06 },
                { 16, 0x08 },
                { 20, 0x0A },
                { 24, 0x0C },
                { 28, 0x0E },
                { 32, 0x0D },
                { 36, 0x0F },
                { 40, 0x0D },
                { 44, 0x0F },
                { 48, 0x0D },
                { 52, 0x0F },
                { 56, 0x0D },
                { 60, 0x0F },
            };

        // 衰减档位（dB）→ 0x13 命令第三字节（衰减状态切换命令的 DATAFIELD）
        // 数据源：控制命令.md § 衰减命令
        private static readonly Dictionary<int, byte> _attenuationSecondByteMap =
            new Dictionary<int, byte>
            {
                { 0,  0x00 },
                { 4,  0x00 },
                { 8,  0x00 },
                { 12, 0x00 },
                { 16, 0x00 },
                { 20, 0x00 },
                { 24, 0x00 },
                { 28, 0x00 },
                { 32, 0x01 },
                { 36, 0x01 },
                { 40, 0x03 },
                { 44, 0x03 },
                { 48, 0x05 },
                { 52, 0x05 },
                { 56, 0x0F },
                { 60, 0x0F },
            };

        /// <summary>
        /// 拼接 7 字节命令帧。body 必须是 5 字节（不含帧头/帧尾）。
        /// </summary>
        private static byte[] BuildFrame(params byte[] body)
        {
            if (body == null || body.Length != 5)
            {
                throw new ArgumentException("Frame body 必须为 5 字节。", nameof(body));
            }
            return new byte[]
            {
                FrameHeader,
                body[0], body[1], body[2], body[3], body[4],
                FrameTail
            };
        }

        // ---------- 公共命令构建方法 ----------

        /// <summary>
        /// 构建指定工作模式下的通道选择命令。
        /// <para>帧格式：<c>0D 11 CH P 00 00 0A</c>，CH 范围 1~8；P 为无衰减状态通道选择参数，
        /// 随工作模式变化（与通道号无关）：</para>
        /// <list type="bullet">
        /// <item>Mode1：<c>P=0x03</c></item>
        /// <item>Mode2：<c>P=0x02</c></item>
        /// <item>Mode3：<c>P=0x04</c></item>
        /// </list>
        /// </summary>
        /// <param name="channel">通道号（1..8）。</param>
        /// <param name="mode">当前工作模式。</param>
        /// <returns>7 字节命令数组。</returns>
        /// <exception cref="ArgumentOutOfRangeException">channel 不在 1..8 或 mode 未知时抛出。</exception>
        public byte[] BuildChannelCommand(int channel, ReceiverMode mode)
        {
            if (channel < 1 || channel > 8)
            {
                throw new ArgumentOutOfRangeException(nameof(channel), channel, "通道号必须在 1~8 范围内。");
            }
            if (!_channelSelectParamByteMap.TryGetValue(mode, out byte param))
            {
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "未知的工作模式。");
            }

            return BuildFrame(0x11, (byte)channel, param, 0x00, 0x00);
        }

        /// <summary>
        /// 构建工作模式命令序列（3 条命令）。
        /// <para>模式 1/2：<c>0D 31 00 00 00 00 0A → 0D 24 00 00 00 00 0A → 0D 24 00 00 00 00 0A</c></para>
        /// <para>模式 3：<c>0D 31 03 00 00 00 0A → 0D 24 00 00 00 00 0A → 0D 24 00 00 00 00 0A</c></para>
        /// </summary>
        public byte[][] BuildModeCommandSequence(ReceiverMode mode)
        {
            if (!_modeSelectByteMap.TryGetValue(mode, out byte modeByte))
            {
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "未知的工作模式。");
            }

            return new byte[][]
            {
                BuildFrame(0x31, modeByte, 0x00, 0x00, 0x00),
                BuildFrame(0x24, 0x00,     0x00, 0x00, 0x00),
                BuildFrame(0x24, 0x00,     0x00, 0x00, 0x00),
            };
        }

        /// <summary>
        /// 构建温度区间命令序列（2 条命令）。
        /// <para>常温：<c>0D 30 00 00 00 00 0A → 0D 24 00 00 00 00 0A</c></para>
        /// <para>高温：<c>0D 30 01 00 00 00 0A → 0D 24 00 00 00 00 0A</c></para>
        /// <para>低温：<c>0D 30 02 00 00 00 0A → 0D 24 00 00 00 00 0A</c></para>
        /// </summary>
        public byte[][] BuildTemperatureCommandSequence(TemperatureRange range)
        {
            if (!_temperatureByteMap.TryGetValue(range, out byte n))
            {
                throw new ArgumentOutOfRangeException(nameof(range), range, "未知的温度区间。");
            }

            return new byte[][]
            {
                BuildFrame(0x30, n,     0x00, 0x00, 0x00),
                BuildFrame(0x24, 0x00,  0x00, 0x00, 0x00),
            };
        }

        /// <summary>
        /// 构建频段命令序列（2 条命令）。
        /// <para>帧格式：<c>0D 31 BN 00 00 00 0A → 0D 24 00 00 00 00 0A</c></para>
        /// <para>BN 取值由 bandIndex 与当前模式决定，参见 <see cref="ModeBandTable"/>。</para>
        /// </summary>
        /// <param name="mode">当前工作模式。</param>
        /// <param name="bandIndex">
        /// 频段编号；合法范围：Mode1: 0..17；Mode2: 0..19；Mode3: 3..24。
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">bandIndex 越界时抛出。</exception>
        public byte[][] BuildBandCommandSequence(ReceiverMode mode, int bandIndex)
        {
            if (!ModeBandTable.TryGetBandByte(mode, bandIndex, out byte bandByte))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(bandIndex),
                    bandIndex,
                    $"频段编号 {bandIndex} 超出 {mode} 的合法范围。");
            }

            return new byte[][]
            {
                BuildFrame(0x31, bandByte, 0x00, 0x00, 0x00),
                BuildFrame(0x24, 0x00,     0x00, 0x00, 0x00),
            };
        }

        /// <summary>
        /// 构建 AGC 衰减命令序列（2 条命令）。
        /// <para>第一条（0x10）写入实际衰减值；第二条（0x13）切换衰减状态。</para>
        /// </summary>
        /// <param name="channel">通道号（1..8）。</param>
        /// <param name="attenDb">衰减值（dB），必须为 0、4、8、12、16、20、24、28、32、36、40、44、48、52、56、60 之一。</param>
        /// <exception cref="ArgumentOutOfRangeException">channel 或 attenDb 不合法时抛出。</exception>
        public byte[][] BuildAttenuationCommandSequence(int channel, int attenDb)
        {
            if (channel < 1 || channel > 8)
            {
                throw new ArgumentOutOfRangeException(nameof(channel), channel, "通道号必须在 1~8 范围内。");
            }
            if (!_attenuationFirstByteMap.TryGetValue(attenDb, out byte firstDataByte))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(attenDb),
                    attenDb,
                    "衰减档位必须为 {0,4,8,12,16,20,24,28,32,36,40,44,48,52,56,60} 之一。");
            }
            byte secondDataByte = _attenuationSecondByteMap[attenDb];

            return new byte[][]
            {
                BuildFrame(0x10, (byte)channel, firstDataByte,  0x00, 0x00),
                BuildFrame(0x13, (byte)channel, secondDataByte, 0x00, 0x00),
            };
        }

        /// <summary>
        /// 便捷辅助方法：把衰减 dB 值映射为 0x10 命令的 DATAFIELD 字节。
        /// <para>供 UI / 测试在不需要构造完整帧时复用映射表。</para>
        /// </summary>
        public byte BuildAttenuationValue(byte attenDb)
        {
            if (!_attenuationFirstByteMap.TryGetValue(attenDb, out byte v))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(attenDb),
                    attenDb,
                    "衰减档位必须为 {0,4,8,12,16,20,24,28,32,36,40,44,48,52,56,60} 之一。");
            }
            return v;
        }
    }
}