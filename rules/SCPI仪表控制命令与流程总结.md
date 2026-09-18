# SCPI 仪表控制命令与流程总结

> 本文档整理自项目源码，仅包含代码中**实际用到的** SCPI 命令，不做推断性补充。

---

## 一、通信基础

### 1.1 VISA 连接方式

所有 SCPI 仪表通过 **National Instruments VISA** 库进行通信，地址格式为 VISA Resource Address（如 `TCPIP::192.168.1.100::INSTR`、`GPIB0::1::INSTR` 等）。

### 1.2 基类 InstrumentBase 通用方法

`InstrumentBase` 是所有 SCPI 仪表的抽象基类，提供以下通用通信方法：

| 方法 | 说明 | 备注 |
|------|------|------|
| `Connect()` | 打开 VISA 会话，建立连接 | 内部创建 `ResourceManager` → `Open(address)` |
| `Disconnect()` | 关闭 VISA 会话，释放资源 | |
| `CheckConnection()` | 发送 `*IDN?` 验证连接是否有效 | 用于心跳检测 |
| `Reconnect()` | 先断开再重新连接 | |
| `EnsureConnection()` | 先检查连接，无效则重连 | 每次 Write/Query 前自动调用 |
| `Reset()` | 发送 `*RST` + `*CLS` | 复位仪表并清除错误队列 |
| `Write(string command)` | 发送命令（无返回值） | 内部追加 `\n` 换行符 |
| `Query(string command)` | 发送命令并读取响应 | 内部追加 `\n`，返回 `ReadString().Trim()` |
| `WriteAsync(...)` | Write 的异步版本 | 带 `CancellationToken` 超时保护 |
| `QueryAsync(...)` | Query 的异步版本 | 带 `CancellationToken` 超时保护 |

### 1.3 基类中用到的 SCPI 命令

| SCPI 命令 | 用途 | 出现位置 |
|-----------|------|----------|
| `*IDN?` | 查询仪表身份标识 | `CheckConnection()` |
| `*RST` | 仪表复位 | `Reset()` |
| `*CLS` | 清除状态寄存器和错误队列 | `Reset()` |

### 1.4 超时与异常处理

- **同步超时**：`TimeoutMilliseconds = 30000`（30 秒）
- **异步超时**：`AsyncTimeoutMilliseconds = 15000`（15 秒）
- 发生 `IOTimeoutException` 或 `NativeVisaException` 时，自动将 `IsConnected` 置为 `false`
- `SpectrumAnalyzer` 特例：`EnableDelay = true`，每条 Write 后会额外 `Thread.Sleep(100)`

---

## 二、各仪表 SCPI 命令清单

### 2.1 ZNB8 矢量网络分析仪

继承自 `InstrumentBase`，类文件：`Instruments/ZNB8.cs`

#### 频率设置

| SCPI 命令 | C# 方法 | 用途 |
|-----------|---------|------|
| `:FREQ:CENT {freq} MHz` | `SetFrequency(double frequency)` | 设置中心频率 |
| `:FREQ:SPAN {span} MHz` | `SetSpan(double span)` | 设置频率跨度 |
| `:FREQ:STAR {freq} MHz` | `SetStartFrequency(double frequency)` | 设置起始频率 |
| `:FREQ:STOP {freq} MHz` | `SetStopFrequency(double frequency)` | 设置终止频率 |

#### 显示与扫描参数

| SCPI 命令 | C# 方法 | 用途 |
|-----------|---------|------|
| `:DISP:WIND:TRAC:Y:RLEV {level} dBm` | `SetReferenceLevel(double level)` | 设置参考电平 |
| `:SWE:POIN {points}` | `SetSweepPoints(int points)` | 设置扫描点数 |
| `:SWE:TIME {time} s` | `SetSweepTime(double time)` | 设置扫描时间 |
| `:INIT:CONT {0\|1}` | `SetContinuousScan(bool enable)` | 启用/禁用连续扫描 |

#### 扫描触发

| SCPI 命令 | C# 方法 | 用途 |
|-----------|---------|------|
| `:INIT:CONT 0` + `:INIT` | `TriggerSingleScan()` | 触发单次扫描（先切单次模式再触发） |

#### S 参数读取

| SCPI 命令 | C# 方法 | 用途 |
|-----------|---------|------|
| `:CALC1:PAR:SEL 'Trc1'` + `:CALC1:DATA? FDATA` | `ReadS11()` | 读取 S11 参数（dB） |
| `:CALC2:PAR:SEL 'Trc2'` + `:CALC2:DATA? FDATA` | `ReadS21()` | 读取 S21 参数（dB） |

#### 峰值搜索

| SCPI 命令 | C# 方法 | 用途 |
|-----------|---------|------|
| `:CALC:MARK1:STAT ON` + `:CALC:MARK1:MAX` + `:CALC:MARK1:MAX:PEAK:SEARCH` + `:CALC:MARK1:Y?` | `MeasurePeak()` | 执行峰值搜索并返回峰值（dB） |

#### 平均

| SCPI 命令 | C# 方法 | 用途 |
|-----------|---------|------|
| `:AVER:STAT {0\|1}` | `SetAveraging(bool enable, int count)` | 启用/禁用平均模式 |
| `:AVER:COUN {count}` | `SetAveraging(...)` | 设置平均次数 |
| `:AVER:CLE` | `NewAveraging()` | 开启新一轮平均（清除历史） |

#### 输出

| SCPI 命令 | C# 方法 | 用途 |
|-----------|---------|------|
| `:SOUR:POW {power} dBm` | `SetOutputPower(double power)` | 设置源功率 |
| `:OUTP {0\|1}` | `EnableOutput(bool enable)` | 启用/禁用射频输出 |

#### 轨迹与 Marker 操作

| SCPI 命令 | C# 方法 | 用途 |
|-----------|---------|------|
| `:CALCulate1:PARameter:SELect '{traceName}'` | `SelectTrace(string traceName)` | 选择轨迹（如 `Trc1`、`Trc2`） |
| `:CALCulate1:MARKer1:STAT ON` + `:CALCulate1:MARKer1:Y?` | `ReadTrace1Mark()` | 读取轨迹 1 的 Marker1 值（相位） |
| 同上 | `ReadTrace2Mark()` | 读取轨迹 2 的 Marker1 值（幅度） |

---

### 2.2 信号源

继承自 `InstrumentBase`，类文件：`Instruments/SignalGenerator.cs`

#### 基本参数

| SCPI 命令 | C# 方法 | 用途 |
|-----------|---------|------|
| `:FREQ {freq} MHz` | `SetFrequency(double frequency)` | 设置输出频率 |
| `:POW {power} dBm` | `SetPower(double power)` | 设置输出功率 |
| `:OUTP {0\|1}` | `EnableOutput(bool enable)` | 启用/禁用信号输出 |

#### 调制

| SCPI 命令 | C# 方法 | 用途 |
|-----------|---------|------|
| `:MOD:TYPE {type}` | `SetModulationType(string modulationType)` | 设置调制类型（AM/FM/PM 等） |
| `:MOD:STAT {0\|1}` | `EnableModulation(bool enable)` | 启用/禁用调制 |
| `:PULM:STAT {0\|1}` | `SetPulseModulation(bool enable)` | 启用/禁用脉冲调制 |

#### 扫频模式

| SCPI 命令 | C# 方法 | 用途 |
|-----------|---------|------|
| `:FREQ:MODE SWE` | `SetFrequencyModeSweep()` | 切换到扫频模式 |
| `:FREQ:STAR {freq}` | `SetSweepStartFrequency(double frequencyHz)` | 设置扫频起始频率 |
| `:FREQ:STOP {freq}` | `SetSweepStopFrequency(double frequencyHz)` | 设置扫频终止频率 |
| `:SWE:POIN {points}` | `SetSweepPoints(int points)` | 设置扫频点数 |
| `:INIT:CONT {0\|1}` | `SetContinuousSweep(bool enable)` | 启用/禁用连续扫频 |

---

### 2.3 频谱分析仪

继承自 `InstrumentBase`，类文件：`Instruments/SpectrumAnalyzer.cs`

> 注意：`EnableDelay = true`，每条 Write 后自动 `Thread.Sleep(100)`。

#### 功率测量

| SCPI 命令 | C# 方法 | 用途 |
|-----------|---------|------|
| `:FREQ:CENT 70 MHz`（硬编码）+ `:SWE:TIME:AUTO ON` + `:CALC:MARK1:STAT ON` + `:CALC:MARK1:MAX:PEAK` + `:CALC:MARK1:MAX:PEAK:SEARCH` + `:CALC:MARK1:Y?` | `MeasurePower(double frequency, double bandwidth)` | 测量中频功率（中心频率固定 70MHz） |
| `:CALC:MARK1:STAT ON` + `:CALC:MARK1:MAX:PEAK` + `:CALC:MARK1:Y?` | `MeasureMarkerPeak()` | 峰值搜索并返回功率（dBm） |
| `:CALC:MARK1:STAT ON` + `:CALC:MARK1:Y?` | `ReadCurrentMarkerValue()` | 直接读取当前 Marker 值 |
| `:CALC:MARK1:Y?` | `ReadMarkerValue()` | 读取 Marker1 的 Y 值（dB） |

#### Marker 操作

| SCPI 命令 | C# 方法 | 用途 |
|-----------|---------|------|
| `:CALC:MARK1:MODE DELTA` + `:CALC:MARK1:X {freq} MHz` + `:CALC:MARK1:Y?` | `ReadDeltaValue(double markerFreq)` | 设置 DELTA Marker 并读取差值（dB） |
| `:CALC:MARK1:MODE DELTA` + `:CALC:MARK1:Y?` | `SetDeltaValue()` | 切换到 DELTA 模式并读取差值 |
| `:CALC:MARK:PTP` + `:CALC:MARK:Y?` | `ReadPeakToPeak()` | 峰峰值搜索（Peak-to-Peak） |
| `:CALC:MARK1:AOFF` | `SetAllMarkOFF()` | 关闭所有 Marker |

#### 频率与带宽

| SCPI 命令 | C# 方法 | 用途 |
|-----------|---------|------|
| `:FREQ:CENT {freq} MHz` + `:FREQ:SPAN {span} MHz` | `SetFrequencySpan(double centerFrequency, double span)` | 设置中心频率和跨度 |
| `:BWID:RES {bw} kHz` | `SetResolutionBandwidth(double bandwidth)` | 设置分辨率带宽 |
| `:BWID:RES:AUTO ON` | `SetResolutionBandwidthAuto()` | 分辨率带宽设为自动 |
| `:BWID:VID {bw} kHz` | `SetVideoBandwidth(double bandwidth)` | 设置视频带宽 |
| `:BWID:VID:AUTO ON` | `SetVideoBandwidthAuto()` | 视频带宽设为自动 |

#### 显示与触发

| SCPI 命令 | C# 方法 | 用途 |
|-----------|---------|------|
| `:TRAC:MODE 1,AVG` / `:TRAC:MODE 1,WRIT` | `SetAverageMode(bool enable)` | 设置平均模式/清除 |
| `:TRACE1:MODE MAXH` / `:TRACE1:MODE WRIT` | `SetMaxHold(bool enable)` | 最大保持模式 |
| `:DISP:WIND:TRAC:CLE` | `ClearDisplay()` | 清除显示 |
| `:DISP:WIND:TRAC:Y:RLEV {level} dBm` | `SetReferenceLevel(double leveldBm)` | 设置参考电平 |
| `:DISP:WIND:TRAC:Y:RLEV:OFFS {offset}` | `SetReferenceLevelOffset(double offsetdBm)` | 设置参考电平偏移 |
| `:SWE:TIME {seconds} s` | `SetSweepTime(double seconds)` | 设置扫描时间 |
| `:TRIG:SOUR VID` + `:TRIG:LEV:AUTO ON` | `EnableVideoTrigger()` | 启用视频触发 |
| `:INIT:CONT ON` / `:INIT:CONT OFF` | `SetContinuousScan(bool continuous)` | 连续/单次扫描切换 |
| `:CALC:MARK:MAX:AUTO ON` | `EnablePeakAutoSearch()` | 峰值自动搜索 |

#### 谐波测试

| SCPI 命令 | C# 方法 | 用途 |
|-----------|---------|------|
| `:INITiate:HARMonics` | `InitializeHarmonicsTest()` | 进入谐波测试模式 |
| `:FETCh:HARMonics:AMPLitude:ALL?` | `FetchHarmonicsAmplitude()` | 读取所有谐波幅度（返回逗号分隔） |

---

### 2.4 ODP3063 电源

继承自 `InstrumentBase`，类文件：`Instruments/ODP3063.cs`

> 注：代码中只操作第二路通道（CH2）。

| SCPI 命令 | C# 方法 | 用途 |
|-----------|---------|------|
| `:SOUR2:VOLT {voltage}` | `SetChannel2Voltage(double voltage)` | 设置 CH2 输出电压（V） |
| `:SOUR2:CURR {current}` | `SetChannel2Current(double current)` | 设置 CH2 输出电流（A） |
| `:OUTP2:STAT {0\|1}` | `EnableChannel2Output(bool enable)` | 启用/禁用 CH2 输出 |
| `:OUTP2:STAT?` | `QueryChannel2OutputStatus()` | 查询 CH2 输出状态 |
| `:SOUR2:VOLT:PROT {voltage}` | `SetChannel2OverVoltageProtection(double voltage)` | 设置 CH2 过压保护值 |
| `:SOUR2:CURR:PROT {current}` | `SetChannel2OverCurrentProtection(double current)` | 设置 CH2 过流保护值 |

---

## 三、仪表控制总体流程

### 3.1 连接初始化流程

```
1. 为每个仪表设置 VISA 地址
2. 依次调用各仪表的 Connect()
3. 连接成功后调用 Reset() 进行复位（*RST + *CLS）
4. 根据测试需求设置初始参数（频率、功率、扫描点数等）
5. 频谱仪额外设置 SetContinuousScan(true) 进入连续扫描模式
```

### 3.2 测试执行流程

```
1. 连接工装板串口（切换通道、设置频率、设置衰减）
2. 设置信号源参数：
   - SetFrequency(频率)
   - SetPower(功率)
   - EnableOutput(true)
3. 设置频谱仪/网分参数：
   - SetFrequencySpan(中心频率, 跨度)
   - SetResolutionBandwidth(带宽)
   - 选择 Marker 模式
4. 等待信号稳定（Thread.Sleep）
5. 触发单次扫描（如需）或直接读取：
   - 频谱仪：MeasureMarkerPeak() / MeasurePower() / ReadMarkerValue()
   - 网分：TriggerSingleScan() → ReadS11() / ReadS21() / ReadTrace1Mark() / ReadTrace2Mark()
6. 记录测试结果
7. 关闭信号源输出 EnableOutput(false)
8. 切换下一通道/下一频率，重复步骤 1-7
```

### 3.3 校准流程（Antenna 模式）

```
1. 发送 Antenna 模式命令序列（工装板控制）：
   - FXJZ 拉高 → 校准源上电 → 天线矢能
2. 网分设置测试参数
3. 执行校准测试，读取 S 参数
4. 发送 Normal 模式命令序列（工装板控制）：
   - FXJZ 拉低 → 校准源下电 → 天线去能
```

### 3.4 复位与断开流程

```
1. 所有仪表调用 Reset()（*RST + *CLS）
2. 依次调用 Disconnect() 或 InstrumentManager.DisconnectAll()
3. InstrumentManager.Dispose() 释放资源（包含所有仪表断开 + 工装板释放）
```

---

## 四、InstrumentManager 关键方法汇总

`InstrumentManager` 是所有仪表的统一管理器，提供以下便捷方法：

| 方法 | 说明 |
|------|------|
| `ConnectSpectrumAnalyzer(address)` | 连接频谱仪 |
| `ConnectSignalGenerator(address)` | 连接信号源 |
| `ConnectZNB8(address)` | 连接 ZNB8 |
| `ConnectODP3063(address)` | 连接 ODP3063 电源 |
| `DisconnectAll()` | 断开所有仪表 + 工装板 |
| `ResetAll()` | 复位频谱仪、信号源，然后设频谱仪连续扫描 |
| `AreAllInstrumentsConnected()` | 检查频谱仪、信号源、ZNB8 是否全部连接 |
| `SwitchChannel(channel)` | 工装板通道切换（1-5，非 SCPI，仅列出用于上下文） |
| `SetPowerState(isOn)` / `IsPowerOn` | 通过 ODP3063 控制电源开关 |
