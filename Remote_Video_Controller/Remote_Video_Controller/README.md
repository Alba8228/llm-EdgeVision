# Remote Video Controller

远程视频车辆控制器：基于 **C# WinForms (.NET Framework 4.7.2, x64)** 的桌面端上位机，通过 **UDP** 协议向车端下发驾驶控制指令，接收车端 STAT 状态反馈，并叠加视频流与导航地图，用于车辆远程接管、自动驾驶路线调试与运行状态监控。

***

## 1. 项目介绍

### 定位

面向线控底盘车辆的远程操控上位机，单窗口集成：键盘驾驶控制、视频监看、车端状态解析、路径/位姿可视化与自动驾驶路线指令下发。

### 核心功能

- **键盘驾驶控制**：WASD 控制油门 / 刹车 / 转向，按键状态实时打包成 16 字节控制包，50ms 周期 UDP 下发。

- **多源视频回退链**：`GStreamer(RTSP/H.265)` → `WebRTC(WebView2)` → `MJPEG(HTTP)`，按优先级自动尝试，单源失败自动切换。

- **车端状态解析**：解析 STAT 协议反馈包，显示路线号、模式、SOC、速度、障碍物距离、IMU/RTK/充电对准状态。

- **路径与位姿可视化**：解析 PATH4 路径段与 pos7/uc1\_reply 位姿段，在自绘 NavigationMapPanel 上实时显示车辆位置、朝向与规划路径。

- **自动驾驶路线指令**：通过 MCMD 协议向路线服务下发路线编号或急停。

- **UDP 反馈监听**：独立监听端口接收车端反馈，HEX 原始报文滚动显示，便于联调。

### 适用场景

- 线控车辆远程接管 / 应急急停

- 自动驾驶路线切换与回放调试

- 车端感知与定位状态在线监控

- UDP 控制协议 / STAT 反馈协议联调

### 项目优势

- 单进程、单窗口、零外部服务（除可选 GStreamer / mediamtx）

- 控制包周期稳定、键盘中断响应及时

- 协议解析与 MATLAB `pnc_base.m` 字节布局对齐，便于核对

- 视频源三级回退，部署灵活

***

## 2. 环境依赖

### 系统要求

- **操作系统**：Windows 10 / 11（x64）

- **.NET 运行时**：.NET Framework 4.7.2 或更高（系统自带或随 Visual Studio 安装）

### 编译环境

- Visual Studio 2022（含 .NET 桌面开发工作负载）或同等 MSBuild 工具链

- 项目目标平台：`x64`（已在 csproj 中固定，禁用 Prefer32Bit）

### 运行依赖（按视频源选配）

| 视频源               | 是否必需      | 依赖                                                                                     |
| ----------------- | --------- | -------------------------------------------------------------------------------------- |
| GStreamer (RTSP)  | 选配（最高优先级） | GStreamer 1.0 MinGW x86\_64，安装到 `C:\Program Files\gstreamer\1.0\mingw_x86_64\`         |
| WebRTC (WebView2) | 选配        | mediamtx WebRTC 服务页 + WebView2 Runtime（Windows 11 自带，Windows 10 需安装 Evergreen Runtime） |
| MJPEG (HTTP)      | 选配        | 任意 MJPEG 流服务（`multipart/x-mixed-replace`）                                              |

> 三者均未安装时，程序仍可启动并下发控制指令，仅视频区显示「全部视频源启动失败」。

### NuGet 包

- `Microsoft.Web.WebView2` v1.0.4129.50（通过项目 `packages.config` 管理）

### 网络前置条件

- 上位机与车端控制服务、视频源、路线服务网络可达

- 本地 UDP 端口未被占用（默认 9999 控制端口、5001 路线端口）

***

## 3. 快速安装 / 部署

### 源码拉取

本项目为本地工程，直接拷贝 `Remote_Video_Controller` 文件夹即可。打开解决方案：

```bash
# 双击或在 VS 开发者命令行中
devenv Remote_Video_Controller.sln
```

### 依赖还原

NuGet 包以二进制形式随仓库提供（`packages/Microsoft.Web.WebView2.1.0.4129.50/`），首次编译前 VS 会自动还原。若提示缺失，执行：

```powershell
nuget restore Remote_Video_Controller.sln
```

### 编译

- Visual Studio：`Ctrl+Shift+B`

- 命令行：

```powershell
msbuild Remote_Video_Controller.sln /p:Configuration=Debug /p:Platform=x64
```

输出目录：`bin\Debug\`（含 `Remote_Video_Controller.exe`、`WebView2Loader.dll`、`Microsoft.Web.WebView2.Core.dll`、`Microsoft.Web.WebView2.WinForms.dll`）。

### 运行

直接双击 `bin\Debug\Remote_Video_Controller.exe`，或在 VS 中 `F5` 调试运行。

***

## 4. 使用步骤

### 4.1 启动系统

1. 填写右侧控制参数：

   - 目标 IP / 端口（车端控制服务，默认 `10.168.1.200:9999`）

   - 本地端口（接收车端反馈，默认 `9999`）
2. 填写视频源地址（按可用源任填一项即可）：

   - WebRTC 页面（默认 `http://10.168.1.144:8889/live/stream`）

   - MJPEG 地址（可选）
3. 点击 **Start** 按钮，程序将：

   - 绑定本地 UDP 端口

   - 按优先级依次尝试 GStreamer / WebRTC / MJPEG 视频源

   - 启动 50ms 周期控制包定时器
4. 视频区左上角显示当前生效视频源（如 `视频源: GStreamer (RTSP)`）。

### 4.2 键盘驾驶控制

点击主窗体获取焦点后，使用 WASD：

| 按键                | 作用               |
| ----------------- | ---------------- |
| `W`               | 前进（accel = 0.25，gear=1） |
| `S`               | 倒车（accel = 0.25，gear=2） |
| `A`               | 左转（steer = -0.5 → -500）  |
| `D`               | 右转（steer = +0.5 → +500）  |
| 同时按 `W+S` / `A+D` | 对应轴归零            |

> 空闲时下发的默认姿态：`HWA=0`（直行）、`VrTilt=5.0`、`VrPan=-2.0`（车端无云台，tilt/pan 仅透传）。
> WebView2 抢焦点时会立刻将焦点还回主窗体，保证键盘控制不中断。

### 4.3 模式与急停

- 模式单选：`Manual`(0) / `Autonomous`(1) / `Res`(2)

- **ESTOP** 按钮：再次点击切换激活 / 解除；激活时油门刹车强制清零

### 4.4 自动驾驶路线（MCMD）

在「自动驾驶路线 (MCMD)」分组中：

1. 填写路线服务 IP / 端口（默认 `10.168.1.108:5001`）
2. 填写本地监听端口（默认 `5001`，失焦后自动重绑）
3. 通过 NumericUpDown 选择路线编号 1～10
4. 点击 **发送路线** 下发；点击 **路线急停** 下发 routeId=0
5. `lblRouteStatus` 显示最近一次发送结果

### 4.5 状态与反馈查看

- **车端状态**分组：路线、速度、SOC、IMU、RTK、充电对准、位姿 X/Y/H、模式码

- **导航地图**：车辆位置（黄色三角）、规划路径（绿色折线）、网格。滚轮缩放、左键拖拽平移、右键复位跟随

- **UDP 反馈 (HEX)**：滚动显示原始报文（含时间戳、来源 IP:Port、字节数、HEX 内容），可点击「清空」

### 4.6 停止

再次点击 **Start**（按钮文本切换为「开始」），程序释放视频进程、UDP 收发线程与定时器。

***

## 5. 项目架构

### 架构图

```
+-----------------------------------------------------------------------------------+
|                              Form1 (主窗体 / WinForms)                            |
|                                                                                   |
|  +---------------------+   +--------------------------------+                      |
|  | 控制面板 (右侧)     |   | 视频区 panel1 (左上)            |                      |
|  | - WASD 键盘状态     |   |  GStreamer 嵌入窗口 / WebView2 / PictureBox 三选一   |
|  | - 模式 / 急停       |   +--------------------------------+                      |
|  | - UDP 参数          |                                                          |
|  | - 路线 MCMD         |   +--------------------------------+                      |
|  +---------------------+   | 导航地图 navMapPanel            |                      |
|                            | (NavigationMapPanel 自绘)        |                      |
|                            +--------------------------------+                      |
|                                                                                   |
|  +---------- UDP 控制收发 ----------+   +---------- 路线 UDP ----------+          |
|  | SendControlPacket (50ms Timer)    |   | routeUdpClient (监听+发送)   |          |
|  | ReceiveFeedback (Thread)          |   | ReceiveRouteFeedback (Thread)|         |
|  +-----------------------------------+   +-----------------------------+          |
|                                                                                   |
|  +------------- 协议解析 (静态类) ------------+   +------------- 视频回退 --------+|
|  | StatusPacketParser  ← STAT 包入口          |   | TryStartGStreamerVideoAsync  ||
|  |   ├─ Path4Parser    ← PATH4 路径段         |   | TryStartWebRtcVideoAsync     ||
|  |   └─ Pos7Parser     ← pos7 位姿段         |   | TryStartMjpegVideoAsync      ||
|  +-------------------------------------------+   +------------------------------+|
+-----------------------------------------------------------------------------------+
                                        |
                                        v
                        +---------------------------------+
                        |  车端 (UDP 9999) / 路线服务 (UDP 5001)  |
                        |  视频源 (RTSP / WebRTC / MJPEG)        |
                        +---------------------------------+
```

### 目录结构

```
Remote_Video_Controller/
├── Program.cs                 # 入口：Application.Run(new Form1())
├── Form1.cs                   # 主窗体逻辑：UDP 收发、视频回退、键盘、状态 UI
├── Form1.Designer.cs          # 窗体控件布局（可视化设计器生成）
├── Form1.resx                 # 窗体资源
├── NavigationMapPanel.cs      # 自绘导航地图控件（车辆位姿 + 路径 + 网格）
├── StatusPacketParser.cs      # STAT 反馈包解析（魔数 / 头 / 分段 / 校验和）
├── Path4Parser.cs             # PATH4 路径段解析（首点 + 增量 MotEst）
├── Pos7Parser.cs              # pos7/uc1_reply 位姿与车辆状态字节解析
├── App.config                 # 运行时版本声明
├── packages.config            # NuGet 包清单
├── Remote_Video_Controller.csproj   # 工程文件
├── Remote_Video_Controller.sln     # 解决方案
├── Properties/
│   ├── AssemblyInfo.cs        # 程序集信息（版本 1.0.0.0）
│   ├── Resources.*            # 资源
│   └── Settings.*             # 设置
├── packages/                  # NuGet 本地缓存（WebView2）
├── bin/Debug/                 # 编译输出 + WebView2Data 运行时缓存
└── obj/                       # 中间产物
```

### 核心模块职责

| 模块                   | 职责                                                                                                      |
| -------------------- | ------------------------------------------------------------------------------------------------------- |
| `Form1`              | UI 编排；UDP 控制包定时下发与反馈接收；视频源三级回退；键盘事件；急停 / 模式切换；MCMD 路线指令；状态 UI 更新                                        |
| `StatusPacketParser` | STAT 包入口：魔数校验、声明长度、校验和、头部字段、分段（0x01 PATH / 0x02 Freespace）分发                                            |
| `Path4Parser`        | PATH4 段：定位 `PATH4` 标识、首点绝对坐标（int32 ×0.001 / int16 ×0.0001）、后续点增量 MotEst 运动估计、对齐与校验                      |
| `Pos7Parser`         | Freespace 段内定位 `pos7`：EKF 位姿 (X/Y int32 ×0.01, H int16 ×0.0001)、车辆状态字节（IMU 第8位 / RTK 第6-7位 / 充电第3位）、SOC |
| `NavigationMapPanel` | 自绘 Panel：世界坐标 ↔ 屏幕坐标变换、网格自适应间距、车辆三角形绘制、路径折线、鼠标缩放 / 平移 / 右键复位                                            |

### 数据流

**控制下行**：`键盘 WASD / 模式 / 急停` → `SendControlPacket (50ms)` → `PackControlData (16B)` → `udpClient.Send → 车端:9999`

**反馈上行**：`车端:9999` → `ReceiveFeedback 线程` → `HandleIncomingUdp` →

- `TryUpdateVehicleStatusUI`（STAT 包 → 状态标签 + 导航地图）

- `TryUpdateLegacyFeedbackUI`（旧版 0xBBAA BBAA 反馈 → 转向 / 档位 / 模式）

- `AppendFeedbackLog`（HEX 滚动显示）

**路线指令**：`按钮` → `SendRouteCommand` → `PackRoutePacket (10B MCMD)` → `routeUdpClient.Send → 路线服务:5001`

**视频流**：`StartVideoStream` → `GStreamer(3次重试)` → `WebRTC(导航+video readyState≥2)` → `MJPEG(首帧解码)`，首个成功者即用。

***

## 6. 配置说明

### 6.1 控制包常量（`Form1.cs` 顶部）

| 常量                           | 默认值                                                   | 说明               |
| ---------------------------- | ----------------------------------------------------- | ---------------- |
| `AccelValue`                 | 0.25f                                                 | W/S 键油门归一值        |
| `SteerAngle`                 | 0.5f                                                  | A/D 键转向量（×1000=±500，即差速满幅） |
| `DefaultSteerHWA`            | 0.0f                                                  | 空闲下发的转向量（0=直行） |
| `DefaultVrTilt`              | 5.0f                                                  | 空闲下发的云台 tilt     |
| `DefaultVrPan`               | -2.0f                                                 | 空闲下发的云台 pan      |
| `gearForward / gearReverse`  | 1 / 2                                                 | 档位编码（2=倒车，STM32 motor_apply 已支持） |
| `RtspUrl`                    | `rtsp://admin:@10.168.1.144/stream1`                  | GStreamer 默认拉流地址 |
| `GstLaunchPath / GstBinPath` | `C:\Program Files\gstreamer\1.0\mingw_x86_64\bin\...` | GStreamer 可执行路径  |

### 6.2 界面可编辑参数（运行时）

| 控件                          | 默认值                                    | 用途                              |
| --------------------------- | -------------------------------------- | ------------------------------- |
| `txtRemoteIP`               | 10.168.1.200                           | 车端控制服务 IP                       |
| `txtRemotePort`             | 9999                                   | 车端控制服务端口                        |
| `txtLocalPort`              | 9999                                   | 本地接收反馈端口（与目标端口相同时复用路线监听 socket） |
| `txtWebRtcUrl`              | <http://10.168.1.144:8889/live/stream> | WebRTC 播放页                      |
| `txtMjpegUrl`               | (空)                                    | MJPEG 流地址，留空则跳过此源               |
| `txtRouteIP / txtRoutePort` | 10.168.1.108 / 5001                    | 路线服务地址                          |
| `txtRouteLocalPort`         | 5001                                   | 路线反馈本地监听端口                      |
| `numRoute`                  | 1 (1\~10)                              | 待下发的路线编号                        |

### 6.3 自定义配置方法

- **更换车端 / 视频源 IP**：直接在界面文本框修改，下次 Start 生效

- **更换 GStreamer 路径**：修改 `Form1.cs` 中 `GstLaunchPath`、`GstBinPath` 常量后重新编译

- **更换 RTSP 地址**：修改 `RtspUrl` 常量

- **调整控制周期**：修改 `StartCommunication` 中 `new Timer(..., 0, 50)` 的周期值（毫秒）

***

## 7. 协议参考

### 7.1 控制包（16 字节，下行）

| 偏移    | 类型        | 字段                       | 缩放    |
| ----- | --------- | ------------------------ | ----- |
| 0-3   | magic     | `AA BB AA BB`            | —     |
| 4-5   | int16 LE  | HWA 转向角                  | ×1000 |
| 6     | uint8     | 油门 accel                 | ×100  |
| 7     | uint8     | 刹车 decel                 | ×100  |
| 8     | uint8     | 档位（1=前 / 2=倒）            | —     |
| 9     | uint8     | `(estop<<3) \| mode`     | —     |
| 10-11 | int16 LE  | VR tilt                  | ×100  |
| 12-13 | int16 LE  | VR pan                   | ×100  |
| 14-15 | uint16 LE | 校验和（前 14 字节累加 mod 65536） | —     |

### 7.2 路线包 MCMD（10 字节，下行）

| 偏移  | 字段                      |
| --- | ----------------------- |
| 0-3 | `M C M D`               |
| 4   | 路线 ID（0 = 路线急停）         |
| 5-7 | `00 00 00`              |
| 8-9 | 校验和（前 8 字节累加 mod 65536） |

### 7.3 STAT 反馈包（上行，变长）

| 偏移     | 字段                                                                                  |
| ------ | ----------------------------------------------------------------------------------- |
| 0-3    | `S T A T` 魔数                                                                        |
| 6-7    | 声明长度（uint16 LE）                                                                     |
| 8-9    | 序列号                                                                                 |
| 10     | 路线 ID                                                                               |
| 11     | 模式码（0 idle / 1 forward / 2 backward / 3 stop\_loc / 4 wait\_charging / 255 unknown） |
| 12     | SOC                                                                                 |
| 13-14  | 速度 cm/s（int16，÷100 → m/s）                                                           |
| 15-16  | 障碍物距离 cm（uint16，÷100 → m）                                                           |
| 18+    | 分段：`[segType(1) + segLen(2 LE)]`，`0x01`=PATH4 路径，`0x02`=Freespace（含 pos7）           |
| 末 2 字节 | 校验和（全包累加 mod 65536）                                                                 |

### 7.4 PATH4 路径段（段内布局）

- 标识 `PATH4`（5 字节）

- 10 字节头 + 首点 15 字节（X int32 ×0.001，Y int32 ×0.001，H int16 ×0.0001）

- 后续每点 11 字节增量（dx/dy int16 ×0.001，dh int16 ×0.0001）

- 通过 `MotEst`（运动估计）由前点姿态旋转到全局坐标

- 末 2 字节 PATH4 内部校验和

### 7.5 pos7 / uc1\_reply（Freespace 段内）

- Freespace 段：8 字节头 + 359 测距点（uint16）= `BinLen`

- 紧随其后定位 `pos7`（4 字节 ASCII）

- 相对 pos7 偏移：EKFx=34、EKFy=38、EKFh=42（int32/int32/int16，÷100/÷100/÷10000）

- `vehicle_status` 字节（偏移 49）：bit8=IMU、bit6-7=RTK（>1 视为 OK）、bit3=充电对准

- SOC（偏移 50）

***

## 8. 常见问题与报错解决

### 8.1 启动失败：「端口被占用」

- **现象**：点击 Start 弹出「启动失败: ... 端口被占用」

- **原因**：本地 9999 / 5001 被其他进程或上一个未正常退出的本程序实例占用

- **解决**：

  1. 先点「停止」（按钮显示「开始」）释放端口
  2. 任务管理器结束残留 `Remote_Video_Controller.exe` 进程
  3. 查看占用：`netstat -ano | findstr :9999`，`taskkill /PID <pid> /F`

### 8.2 视频区显示「全部视频源启动失败」

- **原因 1**：未安装 GStreamer，且未配置 WebRTC / MJPEG 地址

  - 解决：安装 GStreamer 到默认路径，或在界面填写 WebRTC / MJPEG 地址

- **原因 2**：GStreamer 已安装但路径不是默认 MinGW x86\_64

  - 解决：修改 `Form1.cs` 中 `GstLaunchPath`、`GstBinPath`

- **原因 3**：WebRTC 页面导航超时（15s）或 video 元素 readyState 长时间 <2

  - 解决：检查 mediamtx 服务是否在线、WebRTC 信令是否完成；查看 `bin\Debug\` 下输出

- **原因 4**：MJPEG 流 15s 内未收到首帧

  - 解决：用浏览器直接访问 MJPEG URL 验证；确认服务返回 `multipart/x-mixed-replace` 且 boundary 正确

### 8.3 GStreamer 窗口未嵌入 / 黑屏

- **现象**：`视频源: GStreamer (RTSP)` 但 panel1 内无画面

- **原因**：`gst-launch` 进程已启动但视频窗口在 3 秒内未被枚举到

- **解决**：

  1. 确认 `panel1` 可见且尺寸 >0
  2. 查看 `bin\Debug\debug_keyboard.log` 与 VS 输出窗口的 `[GStreamer]` 日志
  3. 单独在命令行运行管线验证可出画面：

     ```powershell
     gst-launch-1.0 rtspsrc location="rtsp://admin:@10.168.1.144/stream1" latency=0 protocols=udp ! queue ! rtph265depay ! h265parse ! d3d11h265dec ! queue ! d3d11download ! queue ! videoconvert ! autovideosink
     ```
  4. 若管线本身无画面，排查 RTSP 链路 / H.265 解码器 / 显卡驱动

### 8.4 键盘控制失效

- **现象**：WASD 按下后车端不动，`lblKeyStatus` 不更新

- **原因**：焦点跑到 WebView2 或其他控件，主窗体未收到 KeyDown

- **解决**：点击主窗体空白区域找回焦点；程序已挂接 `webView21.GotFocus` 自动还焦，若仍异常请确认 `KeyPreview = true`

### 8.5 状态栏全部显示「--」

- **原因**：未收到 STAT 包或包校验失败

- **排查**：查看「UDP 反馈 (HEX)」是否有数据；若 HEX 以 `53 54 41 54` 开头表示已收到 STAT 包

- **校验失败**：HEX 中状态字段会显示「校验和错误 (len=NB)」，对照车端打包逻辑核对

- **非 STAT 包**：若以 `BB AA BB AA` 开头，走旧版反馈分支（仅更新转向/档位/模式）

### 8.6 导航地图无车辆 / 路径

- **无车辆**：未收到含 pos7 的 Freespace 段，且无 PATH4 段首点（程序会用 PATH4 首点作为位置回退）

- **无路径**：未收到 PATH4 段或 PATH4 校验失败，查看 `lblVehParse` 是否提示 `PATH4 校验和错误` / `PATH4 长度不对齐`

- **车辆三角看不到**：位姿坐标超出当前视图范围 → 在地图上右键复位跟随

### 8.7 debug\_keyboard.log 持续增长

- 位置：`bin\Debug\debug_keyboard.log`

- 用途：键盘事件与控制包变化日志，用于定位按键链路

- 处理：定位完根因后可清理；代码中标注为「临时调试」，根因解决后可移除 `LogDebug` 调用

***

## 9. 项目更新日志

| 版本      | 内容                                                                                                                               |
| ------- | -------------------------------------------------------------------------------------------------------------------------------- |
| 1.0.0.0 | 初始版本：UDP 键盘控制、GStreamer RTSP 嵌入、STAT 反馈解析、PATH4 + pos7 位姿解析、导航地图自绘、MCMD 路线指令、视频源三级回退（GStreamer → WebRTC → MJPEG）、WebView2 焦点自动还回 |

***

## 10. 开源 / 使用说明

- **协议**：内部工程，暂未指定开源协议，默认保留全部权利

- **使用规范**：仅可用于本项目对应的线控车辆调试与运维场景

- **禁止行为**：禁止将硬编码的车端 IP、视频源地址、协议细节外泄至非授权环境

- **贡献指南**：本地工程以直接修改源码并提交本地版本库为准；新增协议字段时同步更新本 README 第 7 节

- **联系方式**：项目内部维护，无对外联系方式

***

## 附：调试与自测要点

- **键盘链路自测**：按 WASD，查看 `bin\Debug\debug_keyboard.log` 应有 `[KeyDown]`/`[KeyUp]` 与 `[Send] W=.. A=.. HWA=.. accel=..` 行

- **控制包自测**：用 Wireshark 抓 UDP 9999，确认 16 字节包结构与第 7.1 节一致

- **STAT 解析自测**：在「UDP 反馈 (HEX)」复制一行 STAT 包，用第 7.3/7.4/7.5 节字段手动核对

- **视频源自测**：先在命令行单独跑 GStreamer 管线 / 浏览器打开 WebRTC 页 / 浏览器打开 MJPEG，确认源可用后再到程序内启用

