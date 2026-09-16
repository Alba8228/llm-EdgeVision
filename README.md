### 知识点补充
USB 摄像头自带ISP将raw缩放为刚设置的尺寸，并将raw转YUV/MJPG及各种白平衡处理。如果你设置的分辨率和程序从rknn文件中读取到的适配分辨率不同，又会在convert_image_with_letterbox()中进行填充缩放为适配npu推理的格式（rga，cpu都在这）。但输出的图像是和你设置的分辨率一致的，只是内部处理为适合rknn的后面会映射会你设置的图像里。

mipi摄像头同上只是用开发板上的ISP。
yolo模型的640*480还是640*640不只表示长宽比例还表具体的分辨率，当你输入分辨率大于yolo适配分辨率时缩放填充操作会将多个像素融为一个。
对于 1280×720 的 16:9 输入：
用 640×640 正方形输入：缩放比同样是 0.5，有效图像还是 640×360，但上下总共要补 280 像素的填充边，无效算力浪费更多。
用 640×480 输入：无效填充只有 120 像素，有效像素占比更高，同等检测效果下推理速度更快，这也是 4:3 传感器场景常用这个尺寸的原因
优先用 RGA 硬件加速 （开发板的 2D 加速器），失败或宽度不是 16 对齐时回退到 CPU。

ISP作用：
1、去马赛克 (Demosaic) 把单通道 Bayer 数据还原成 RGB/YUV 彩色
2、自动白平衡 (AWB) 不同色温下白色还是白色 
3、自动曝光 (AEC) 控制亮暗，避免过曝/欠曝 
4、镜头阴影校正 (LSC) 消除镜头边缘暗角 
5、降噪 (NR) 去除 sensor 噪点 
6、色彩校正 (CCM) 还原真实色彩 
7、伽马校正 (Gamma) 适配显示设备亮度曲线 
8、缩放/裁剪 (Crop/Scale) 把 4224×3136 缩放到你请求的尺寸



# EdgeVision-RK3588 车载视觉问答与遥控系统

> 检测模型源自 [EdgeVision-RK3588](https://github.com/Alba8228/EdgeVision-RK3588)（YOLOv8n 训练+量化）；
> 本仓库是其**端侧落地 + 全栈交付**：板端 C++ 推理推流、云端多模态问答、Windows 上位机、UART 车控。

## 一、项目介绍

**项目定位**：面向低算力边缘平台的"实时检测 + 视频推流 + 大模型问答 + 车控"一体化全栈系统，
基于 Orange Pi 5 Max（RK3588），覆盖板端 C++、Windows 上位机 C#、云端多模态 API 三层。

**核心功能**：
- **YOLOv8 实时检测**：NPU 零拷贝推理（letterbox 直写 DMA，省 memcpy），MIPI/USB 摄像头 V4L2 直采，镜头畸变矫正
- **双模式推流**：MJPEG-over-HTTP（局域网）/ H.264 RTMP→WebRTC（跨网络，码率仅 ~1Mbps）
- **LLM 场景问答**：提问时上传"画框前干净帧 + YOLO 检测结果"到多模态 API，SSE 流式返回；支持 ROI 框选聚焦
- **UDP↔UART 车控**：上位机 16 字节控制包经板端转发至 MCU 底盘，状态回显

**项目优势**：单进程多线程（采集/推理/推流/问答解耦）；**全部参数收敛一份 `llm.conf`，零命令行启动**；
**HEADLESS 编译**使运行时依赖从 100+ 个 .so 降至 ~20 个，新板仅装 2 个包即可跑；上位机断线自动重连。

## 二、环境依赖

### 板端编译依赖（开发板本机编译）
| 库 | 安装 | 用途 |
|---|---|---|
| gcc / g++ / cmake | 系统自带 | 编译工具链 |
| librockchip-mpp-dev | `sudo apt install` | MPP 头文件（H.264 编码） |
| librtmp-dev | `sudo apt install` | RTMP 推流 |
| libcurl4-openssl-dev | `sudo apt install` | HTTPS 调用 LLM API |
| libturbojpeg-dev | `sudo apt install` | JPEG 编解码（帧上传 API） |

> 不装 MPP/RTMP 开发包仍可编译，仅 RTMP 推流不可用（CMake 输出 WARNING）。
> OpenCV(headless)/RKNN 运行时/RGA/cJSON 已内置 `RK3588/3rdparty/`，无需安装。

### 板端运行时依赖
| 库 | 来源 | 说明 |
|---|------|------|
| libopencv_core/imgproc.so.4.8 | 3rdparty/opencv-4.8.0 | 图像处理（cvtColor 内部 NEON） |
| librknnrt.so | 3rdparty/rknpu2 | NPU 推理运行时 |
| librga.so | 3rdparty/librga | RGA 硬件加速（letterbox / RGB→NV12） |
| libturbojpeg.a | 3rdparty/jpeg_turbo | 静态链接，无额外 .so |
| librockchip_mpp / librtmp / libssl / libcrypto | 系统 /usr/lib | 仅 RTMP 模式需要 |

> **HEADLESS 模式（默认）**：stream 目标只链接 core+imgproc，不依赖 highgui/videoio/imgcodecs，
> 故无需 GTK3/GStreamer/FFmpeg 传递依赖，**新板只需 `sudo apt install libgomp1 zlib1g`**。

### 上位机（Windows）
- Visual Studio 2022 + .NET Framework 4.7.2（WinForms，无 NuGet 依赖）
- 可选：GStreamer（RTSP 拉流）、WebView2（WebRTC 播放）

### 外部工具（可选）
| 工具 | 用途 | 获取 |
|------|------|------|
| mediamtx | RTMP→WebRTC/HLS/RTSP 转换 | 仓库已含二进制 |
| Tailscale | 跨网络 VPN 观看 | `curl -fsSL https://tailscale.com/install.sh \| sh` |

## 三、快速安装/部署

```bash
# 1. 编译（本机；交叉编译先 export GCC_COMPILER=aarch64-linux-gnu）
cd RK3588
./build-linux.sh -t rk3588 -a aarch64 -d yolov8
#   -t SoC: rk3588/rk356x/rk3576/rv1106/rk1808  -a: aarch64/armhf  -d: 固定 yolov8
#   -b Release/Debug  -r 关闭RGA(CPU resize)  产物→ install/rk3588_linux_aarch64/rknn_yolov8/

# 2. 部署：make install 已把所有运行时 .so 拷进 install/.../lib/，
#    可执行文件 RPATH=$ORIGIN/../lib → 整个 rknn_yolov8/ 目录可直接打包发板
tar czf yolov8.tar.gz -C install/rk3588_linux_aarch64 rknn_yolov8
scp yolov8.tar.gz user@newboard:~/ && ssh user@newboard
tar xzf yolov8.tar.gz && cd rknn_yolov8
sudo apt install libgomp1 zlib1g        # HEADLESS 仅需 2 个包
```
> 换不同架构板子需重编（3rdparty 含 aarch64/armhf/x64 预编译库）；
> 依赖缺失时运行 `./deploy_libs.sh install/.../rknn_yolov8` 自动 ldd 扫描收集。

## 四、使用步骤

**所有参数读自工作目录 `./llm.conf`，程序不接命令行参数**（详见第六节）。

### 4.1 板端启动
```bash
cd install/rk3588_linux_aarch64/rknn_yolov8
./run.sh ./rknn_yolov8_stream    # 检测+推流+LLM问答（主程序）
./run.sh ./rknn_udp_uart         # 车控转发（需控车时另开终端）
```
成功标志：
```
[main] conf: model=model/best.rknn camera=/dev/video-camera0 1280x720@30 push=8080 ...
[http] MJPEG push server listening on 0.0.0.0:8080
[llm]  service started on TCP port 12346
```

### 4.2 MJPEG 推流（局域网）
`llm.conf` 设 `push=8080`，浏览器开 `http://<board-ip>:8080/`（兼容 mjpg_streamer）。码率 3~8Mbps。

### 4.3 H.264 RTMP 推流（跨网络/低带宽推荐）
`llm.conf` 设 `push=rtmp://127.0.0.1:1936/live/stream`，先起 mediamtx 再起主程序：
```bash
./mediamtx mediamtx.yml &      # 必须与 mediamtx.yml 同目录
./run.sh ./rknn_yolov8_stream
```
```
摄像头 → V4L2采集 → NPU推理 → MPP H.264编码 → librtmp推流 → mediamtx → WebRTC/HLS/RTSP
```
| 观看方式 | 地址 | 延迟 |
|------|------|------|
| WebRTC | `http://<board-ip>:8889/live/stream` | ~200ms（推荐） |
| HLS | `http://<board-ip>:8888/live/stream` | ~3s |
| RTSP | `rtsp://<board-ip>:8554/live/stream` | VLC 播放 |

> ⚠️ RTMP 端口是 **1936 非标准**（1935 被占），须与 `mediamtx.yml` 的 `rtmpAddress: :1936` 一致；
> 编码参数：640x480@30、1Mbps CBR、H.264 High Profile+CABAC、GOP 30；断连自动退出不空转。
> 停止：`pkill mediamtx`。

**跨网络（Tailscale）**：板与观看端登录同账号，`tailscale ip -4` 查固定内网 IP（100.x.x.x），
开 `https://<tailscale-ip>:8889/live/stream`；公网分享 `nohup tailscale funnel 8888 &`（走 HLS 免登录）。

### 4.4 LLM 场景问答 + ROI
上位机「场景问答」区：填板端 IP → 点「开始」（连接 TCP:12346）→ 在 MJPEG 画面上「框选 ROI」→
输入问题 → 「提问」→ 流式显示回答。ROI 内无目标时模型自动降级为整帧理解。

### 4.5 车控
`llm.conf` uart 段配好后启动 `rknn_udp_uart`；上位机 WASD 发 16 字节包（帧头 `AA BB AA BB`）经
UDP:12345→`/dev/ttyS1`→MCU，状态回显显示在「UDP 反馈(HEX)」。

### 4.6 上位机
VS 打开 `Remote_Video_Controller/*.sln` 编译运行；视频源自动回退链：GStreamer(RTSP)→WebRTC→MJPEG。

### 4.7 测试
```bash
ss -tlnp | grep -E "8080|12346"                       # 验证监听
echo '{"cmd":"state"}' | nc -q1 127.0.0.1 12346       # 验证问答协议(返回当前ROI/帧率状态)
```

### 板端脚本速查（RK3588/）
| 脚本 | 用法 | 作用 |
|---|---|---|
| `build-linux.sh` | `-t rk3588 -a aarch64 -d yolov8` | 编译，产物进 install/ |
| `run.sh` | `./run.sh ./rknn_yolov8_stream` | 设 LD_LIBRARY_PATH 启动（解决 OpenCV 残留 RUNPATH） |
| `deploy_libs.sh` | `./deploy_libs.sh <install_dir>` | ldd 扫描→拷非系统 .so→验证 |
| `monitor.sh` | `./monitor.sh` | 1s 刷新 NPU/DDR/GPU/RGA 负载与 CPU 频率 |
| `scaling_frequency.sh` | `sudo ./scaling_frequency.sh -c rk3588` | 锁定/恢复 CPU-GPU-NPU 调频 |

## 五、项目架构

```
┌──────────────── Orange Pi 5 Max (RK3588) ────────────────────┐
│ rknn_yolov8_stream（单进程多线程）                             │
│  V4L2直采(MJPG/NV12) → RGA letterbox → NPU零拷贝推理          │
│    ├─ 画框前干净帧快照 ──┐                                    │
│    ├─ 检测结果+ROI过滤 ──┤→ LlmAgent(TCP:12346)               │
│    │                     └ turbojpeg→base64→cJSON→curl SSE ──┼──→ DeepSeek API
│    ├─ 画框后帧 → MJPEG:8080 / MPP H.264 → RTMP:1936 → mediamtx│──→ WebRTC:8889
│ rknn_udp_uart：UDP:12345 ↔ /dev/ttyS1 ↔ MCU底盘              │
└───────────────────────────────────────────────────────────────┘
        ▲ 视频(HTTP/WS)      ▲ UDP车控       ▲ TCP问答(JSON行)
┌───────┴────────────────────┴───────────────┴──────────────────┐
│ Remote_Video_Controller（Windows C# WinForms .NET 4.7.2）      │
│  视频回退链 │ UdpControlClient │ LlmClient(重连) │ RoiDrawer   │
└───────────────────────────────────────────────────────────────┘
```

**核心模块**（`RK3588/examples/yolov8/cpp/`）：
| 文件 | 职责 |
|---|---|
| `main_stream.cc` | 主入口、检测循环、LLM 快照钩子（读 llm.conf） |
| `llm_agent.{h,cc}` | 问答服务：TCP 协议、三快照 mutex、SSE 流式、ROI 过滤 |
| `v4l2_capture.cc` | 直接 V4L2 采集（绕开 libv4l2）+ turbojpeg 解码 + Multi-planar NV12/YUYV |
| `mjpeg_http_sink.cc` / `rtmp_sink.cc` | MJPEG-over-HTTP / MPP H.264+RTMP 推流 |
| `udp_uart1.c` | UDP↔UART 车控转发 |
| `conf_util.c` | 极简 key=value 解析器（C 实现，C/C++ 共用） |

**数据流要点**：问答上传的是**画框前的干净帧**（避免检测框干扰模型）；检测结果以文字（类别+置信度+归一化位置）随图上传；ROI 常驻板端，`state` 命令重连后可恢复。

**支持的摄像头**：
| 类型 | 设备路径 | 格式 |
|------|---------|------|
| USB UVC | `/dev/video*`（`v4l2-ctl --list-devices` 确认） | MJPG / YUYV |
| MIPI OV13855 | `/dev/video-camera0` 或 `/dev/video11`（RKISP mainpath） | NV12 |

**相对官方 RKNN Model Zoo 的修改点**（工程深度）：
| 文件 | 改动 |
|---|---|
| `CMakeLists.txt` | 新增 `_stream`/`rknn_udp_uart` 目标；静态链接 turbojpeg；MPP/RTMP/RGA 自动检测；HEADLESS 选项；精确打包 .so；OpenSSL 链接修复 |
| `main_stream.cc` | 实时采集+双模式推流+LLM 钩子+畸变矫正+HEADLESS 条件编译 |
| `v4l2_capture.cc` | 直连 V4L2 绕开 libv4l2；turbojpeg 解码；Multi-planar；poll 替代 select |
| `rknpu2/yolov8.cc` | 修复 NHWC 张量维度解析（`dims[3]` 取 channel）；`rknn_init` 补参数 |
| `run.sh`/`deploy_libs.sh` | LD_LIBRARY_PATH 启动器；一键收集运行时依赖 |

## 六、配置说明（./llm.conf 统一配置）

三段式 `key=value`，支持 `#` 整行注释与值后 ` #` 行内注释：
```ini
# ---------- LLM 场景问答 ----------
api_key=sk-xxxx              # DeepSeek API 密钥（必填）
model=deepseek-flash         # 仅该型号支持图片输入
port=12346                   # 问答服务 TCP 端口
jpeg_quality=80              # 上传帧 JPEG 质量
timeout_s=30                 # API 超时
max_new_tokens=128           # 回答长度上限（可调 256 换更长回答）

# ---------- 检测+推流 ----------
model_path=model/best.rknn   # 必填
camera=/dev/video-camera0    # 必填（MIPI；USB 用 /dev/videoN）
width=1280
height=720
fps=30
push=8080                    # 数字=MJPEG端口；rtmp://…=RTMP；注释=不推
calib=camera_calib.yaml      # 畸变矫正标定，注释=不矫正

# ---------- UDP↔UART 车控 ----------
uart_dev=/dev/ttyS1
uart_baud=115200             # 白名单 9600~921600
udp_port=12345
```
> 改 conf 后需重启程序生效；`examples/yolov8/llm.conf` 与 `install/.../rknn_yolov8/llm.conf`
> 两份须一致，**以 install 目录（实际运行目录）为准**。

## 七、常见问题与报错解决

**部署 / 启动**
| 现象 | 原因 | 解决 |
|---|---|---|
| `libopencv_core.so: cannot open` | OpenCV .so 残留构建机 RUNPATH | 用 `./run.sh` 启动（自动设 LD_LIBRARY_PATH） |
| 换板跑不起来 | 缺传递依赖 | HEADLESS 装 `libgomp1 zlib1g`；非 Rockchip 无 MPP 只能用 MJPEG |
| 上位机"未连接" | 板端跑旧版二进制（无 LLM）或运行目录无 llm.conf | 重新部署新 `rknn_yolov8_stream` + `llm.conf` 到运行目录 |

**摄像头**
| 现象 | 原因 | 解决 |
|---|---|---|
| `open /dev/video0 failed` | 设备号变化/用了 rkcif 直出节点 | `v4l2-ctl --list-devices` 确认；MIPI 用 `/dev/video-camera0` |
| UVC 报 `invalid fd`/`VIDIOCSPICT` | libv4l2 兼容层冲突 | 本项目已绕开，直接用自带 stream |
| MIPI 报 `not a V4L2 capture device` | 用了 rkcif 直出（Raw Bayer） | 改用 RKISP mainpath `/dev/video11` |
| MIPI 只有 ~15fps | OV13855 4224x3136@30 缩放耗时 | 检测推流够用；要更高可请求 1920x1080 |

**推流**
| 现象 | 原因 | 解决 |
|---|---|---|
| 浏览器无 MJPEG 画面 | PC 防火墙挡端口 | `iptables -I INPUT -p tcp --dport 8080 -j ACCEPT` |
| WebRTC 连不上 | mediamtx 未起/端口非 1936 | `./mediamtx mediamtx.yml &`；`ss -tlnp\|grep 1936` |
| `no tracks found` | RTMP 缺 SPS/PPS 序列头 | 已用 `MPP_ENC_SET_HEADER_MODE`+`EACH_IDR` 自动嵌入，勿改 |
| Tailscale 看 MJPEG 很卡 | 3~8Mbps 超虚拟带宽 | 改用 H.264 RTMP（~1Mbps） |
| RGA `COLORFILL fail` 刷屏 | RGA 不支持 rgb888 imfill | 有 CPU memset 兜底，功能正常，仅日志噪音 |

**LLM 问答**
| 现象 | 原因 | 解决 |
|---|---|---|
| `[错误] api http error (400)` | model 名不支持图片/不存在 | 用 `deepseek-flash`；`deepseek-v4-pro` 收不到图 |
| 回答开头就断 | thinking 模式吃光 token | 代码已默认 `thinking:disabled`，勿删该字段 |
| HEADLESS 下录像不可用 | 去除了 videoio | 设计取舍；需录像用 `-DHEADLESS=OFF` 重编 |

## 八、项目更新日志
- **2026-09-15** 参数收敛：命令行读取全删，detect/uart/llm 统一进 `llm.conf`；`udp_uart1` 并入主工程（新目标 `rknn_udp_uart`）；共享解析器 `conf_util.c`；行内注释剥离
- **2026-09-15** 修复回答截断（禁用 thinking）；ROI 无目标时引导直接分析画面
- **2026-09-15** LLM 场景问答上线：干净帧+检测结果上传、SSE 流式、上位机 ROI 框选（LlmClient/RoiDrawer）
- **2026-09-14** 弃用本地 RKLLM（核分配不可控），改外接多模态 API，YOLO 独占 NPU
- **2026-09-06** 采集与推流增强：V4L2 直采、MJPEG/RTMP 双模式、HEADLESS、畸变矫正

## 附录：关键原理速记
- **letterbox 尺寸选择**：1280×720 输入用 640×480 比 640×640 少补 160px 边、有效像素占比更高、推理更快；缩放优先 RGA 硬加速，宽度非 16 对齐时回退 CPU
- **ISP 流水线**：去马赛克→白平衡→曝光→阴影校正→降噪→色彩校正→伽马→缩放裁剪，把 sensor raw 转成可用 NV12/YUV
- **零拷贝**：letterbox 结果直写 NPU DMA 输入内存，省去用户态↔内核态 memcpy
