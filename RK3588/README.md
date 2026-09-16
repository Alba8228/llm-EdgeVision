用绝对路径矫正推理
./run.sh ./rknn_yolov8_stream model/best.rknn /dev/video-camera0 1280 720 30 "" 8080 --calib /home/pi/Desktop/RK3588/camera_calib.yaml

./run.sh ./rknn_yolov8_stream model/best.rknn /dev/video-camera0 1280 720 30 "" \
    rtmp://127.0.0.1:1936/live/stream --calib /home/pi/Desktop/RK3588/camera_calib.yaml
# RKNN YOLOv8 多模式检测 Demo
基于 Rockchip [RKNN Model Zoo 2.3.0](https://github.com/airockchip/rknn_model_zoo) 的 YOLOv8目标检测示例。


实时检测模式特性：
- 直接 V4L2 ioctl 采集，**绕开 OpenCV videoio 与 libv4l2 兼容层**（解决 Orbbec Astra Pro 等 UVC 摄像头 `invalid fd` / `VIDIOCSPICT` 报错）
- MJPG 用 3rdparty `libturbojpeg.a` 静态链接解码，NV12/YUYV/UYVY 使用 OpenCV `cv::cvtColor` 转换（内部 NEON 优化，比手写循环更快且可移植）
- NPU 零拷贝推理，letterbox 直写 DMA 内存，省memcpy
- MJPEG 编码用 `tjCompress2`，避免 OpenCV 3.4 `cv::imencode` 在 ARM 上 segfault
- H.264 编码用 RK3588 MPP 硬件编码器（640x480@30fps, 1Mbps CBR），CPU 占用极低
- RGB→NV12 转换优先用 RGA 硬件加速（`imcvtcolor`），CPU 回退兜底
- **HEADLESS 模式**（默认开启）：去除 OpenCV highgui/videoio 依赖，运行时 .so 从 100+ 降至 ~20 个
- RTMP 断连自动退出进程，不浪费 CPU 空转


### 编译依赖（开发板上本机编译）

| 库 | 安装方式 | 用途 |
|---|---------|------|
| gcc / g++ / cmake | 系统自带 | 编译工具链 |
| librockchip-mpp-dev | `sudo apt install librockchip-mpp-dev` | MPP 头文件（H.264 编码） |
| librtmp-dev | `sudo apt install librtmp-dev` | RTMP 推流库 |
> 若不装 MPP/RTMP 开发包，仍可编译，只是 RTMP 推流功能不可用（CMake 会输出 WARNING）。

### 运行时依赖
| 库 | 来源 | 说明 |
|---|------|------|
| libopencv_core.so.4.8 | 3rdparty/opencv-4.8.0 | 图像处理核心 |
| libopencv_imgproc.so.4.8 | 3rdparty/opencv-4.8.0 | 图像处理（cvtColor 等） |
| librknnrt.so | 3rdparty/rknpu2 | NPU 推理运行时 |
| librga.so | 3rdparty/librga | RGA 硬件加速（letterbox / RGB→NV12） |
| librockchip_mpp.so* | 系统 /usr/lib | MPP H.264 编码（仅 RTMP 模式） |
| librtmp.so* | 系统 /usr/lib | RTMP 推流（仅 RTMP 模式） |
| libssl.so* / libcrypto.so* | 系统 /usr/lib | librtmp 依赖（仅 RTMP 模式） |
| libturbojpeg.a | 3rdparty/jpeg_turbo | 静态链接，无需额外 .so |

> **HEADLESS 模式**（默认开启）下，stream 目标只链接 `libopencv_core` + `libopencv_imgproc`，
> 不再依赖 `highgui/videoio/imgcodecs`，因此无需 GTK3/GStreamer/FFmpeg 等传递依赖。
> 新板子上只需 `libgomp1 zlib1g` 即可运行 stream 目标。

**新板子还需安装**（OpenCV 传递依赖，HEADLESS 模式下极少）：
```bash
# HEADLESS 模式（默认，仅需 2 个包）
sudo apt install libgomp1 zlib1g
# 非 HEADLESS 模式（-DHEADLESS=OFF 编译时需要更多）
sudo apt install libgstreamer1.0-0 libgstreamer-plugins-base1.0-0 \
    libgtk-3-0 libavcodec58 libavformat58 libavutil56 libswscale5 \
    libv4l-0 libdc1394-25 libgomp1 zlib1g
```

### 外部工具（可选）
| 工具 | 用途 | 安装 |
|------|------|------|
| mediamtx | RTMP→WebRTC 流媒体服务器 | 项目根目录已有二进制 |
| Tailscale | 跨网络 VPN 访问 | `curl -fsSL https://tailscale.com/install.sh \| sh` |

## 编译
```bash
cd /home/orangepi/rknn_model_zoo-2.3.0
./build-linux.sh -t rk3588 -a aarch64 -d yolov8
```
参数说明：
- `-t` 目标 SoC：`rk356x` / `rk3588` / `rk3576` / `rv1106` / `rk1808` / `rv1126`
- `rk356x` 代表 rk3562 / rk3566 / rk3568
- `rv1106` 代表 rv1103 / rv1106
- `-a` 架构：`aarch64` / `armhf`
- `-d` demo 名，固定为 `yolov8`
- `-b` Release / Debug（可选，默认 Release）
- `-m` 开启 ASan（需要 `-b Debug`）
- `-r` 关闭 RGA（CPU resize 替代，rv1103/rv1106 无效）
交叉编译时通过 `GCC_COMPILER` 环境变量指定工具链前缀：
```bash
export GCC_COMPILER=aarch64-linux-gnu
./build-linux.sh -t rk3588 -a aarch64 -d yolov8
```
输出位置：`install/<soc>_linux_<arch>/rknn_yolov8_demo/`。

## 部署与可移植性
`make install` 会将所有运行时 .so（含 MPP/RTMP/SSL/RGA）拷贝到 `install/.../lib/`。
可执行文件 RPATH 设为 `$ORIGIN/../lib`，整个 `rknn_yolov8_demo/` 目录可直接打包发板。

### 一键收集依赖
```bash
# 脚本会自动扫描 ldd 依赖 → 复制非系统库 → 复制 mediamtx → 验证完整性。
./deploy_libs.sh install/rk3588_linux_aarch64/rknn_yolov8
```

### 同架构板子部署
```bash
# 开发机上打包
tar czf rknn_yolov8_demo.tar.gz -C install/rk3588_linux_aarch64 rknn_yolov8_demo
# 传到新板子
scp rknn_yolov8_demo.tar.gz user@newboard:~/
ssh user@newboard
tar xzf rknn_yolov8_demo.tar.gz
cd rknn_yolov8_demo
# 安装最少传递依赖（HEADLESS 模式仅需 2 个包）
sudo apt install libgomp1 zlib1g

HEADLESS（默认开启） 非 HEADLESS（编译时 -DHEADLESS=OFF ） OpenCV 链接 只链接 core + imgproc 链接全部（含 highgui/videoio/imgcodecs） imshow/预览窗口 ❌ 不可用 ✅ 可以弹出窗口预览 VideoWriter 录像 ❌ 不可用 ✅ 可录 mp4 运行时 .so 依赖 ~20 个 ~100+ 个 新板子 apt 依赖 libgomp1 zlib1g （2个包） libgtk-3-0 libgstreamer1.0-0 libavcodec58 ... （10+个包） 典型场景 无头板部署、远程推流、Docker 开发调试、本地接显示器

### 不同架构板子
需要重新编译（3rdparty 中有 aarch64/armhf/armhf_uclibc/x64 的预编译库）：
```bash
# 例如 RK3566 (armhf)
./build-linux.sh -t rk356x -a armhf -d yolov8
```

## Demo 用法

### 1. 图片检测
```bash
./run.sh ./rknn_yolov8 model/best.rknn model/bus.jpg
```
### 2. 实时摄像头检测
```bash
./run.sh ./rknn_yolov8_demo_stream <model> <camera_id|device_path> \
                          [width] [height] [fps] [save_path] [push_arg]
```
- `camera_id`：整数（如 `0`）或设备路径（如 `/dev/video20`）
- `width/height/fps`：请求的分辨率与帧率，传 `0` 用默认 640x480@30
- `save_path`：可选，把带检测框的画面录成 mp4，传 `""` 不录像（HEADLESS 模式下不支持录像）
- `push_arg`：推流方式，见下方两种模式

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

#### 支持的摄像头类型
| 类型 | 设备路径 | 格式 | 说明 | 
|------|---------|------|------|
| USB UVC 摄像头 | `/dev/video*` | MJPG / YUYV | 即插即用，设备号可能变化 |
| MIPI OV13855 | `/dev/video-camera0` 或 `/dev/video11` | NV12 | 需要正确的 overlay（见下方） |
> ```bash
> v4l2-ctl --list-devices
> # 找到你的摄像头名下的设备路径，例如:
> #   Astra Pro HD Camera: ... /dev/video20 ...
> ```

## 实时推流 - MJPEG 模式
`push_arg` 传入端口号即可开启 MJPEG-over-HTTP 推流，输出格式与 mjpg_streamer 的
`output_http.so` 完全兼容（`multipart/x-mixed-replace`）。
```bash
# 启动推流（USB 摄像头示例，请先用 v4l2-ctl --list-devices 确认设备号）
./run.sh ./rknn_yolov8_stream model/best.rknn /dev/video20 1280 720 30 "" 8080
# MIPI 摄像头
./run.sh ./rknn_yolov8_stream model/best.rknn /dev/video-camera0 1280 720 30 "" 8080
# 局域网浏览器打开
http://<board-ip>:8080/
```
适用场景：同一局域网内观看，码率 3-8Mbps。
## 实时推流 - H.264 RTMP 模式
`push_arg` 传入 `rtmp://` 开头的地址即开启 H.264 RTMP 推流，需要搭配 mediamtx 流媒体服务器。
### 架构
```
摄像头 → V4L2采集 → NPU推理 → MPP H.264编码 → librtmp推流
                                                    ↓
                                              mediamtx (RTMP)
                                                    ↓
                                           WebRTC / HLS 播放 / rtsp
```
### 启动步骤

```bash
# 1. 启动 mediamtx（后台运行，需与 mediamtx.yml 在同一目录）
./mediamtx mediamtx.yml &

# 2. 启动推流（USB 摄像头示例，请先用 v4l2-ctl --list-devices 确认设备号）
./run.sh ./rknn_yolov8_stream model/best.rknn /dev/video20 1280 720 30 "" \
    rtmp://127.0.0.1:1936/live/stream
# MIPI 摄像头
./run.sh ./rknn_yolov8_stream model/best.rknn /dev/video-camera0 1280 720 30 "" \
    rtmp://127.0.0.1:1936/live/stream
```
> ⚠️ `mediamtx.yml` 必须和 `mediamtx` 二进制放在同一目录，且 `rtmpAddress` 配置为 `:1936`（非标准端口，因 1935 被其他进程占用）。
> 推流地址中的端口必须与 `mediamtx.yml` 中 `rtmpAddress` 一致。

## 跨网络观看（不下载Tailscale）
你的板子 (mediamtx)
├── :8889 (WebRTC) ←── Tailscale 内网:  https://100.90.126.70:8889/live/stream
│                         需要登录 Tailscale 才能访问
│                         走 WebRTC，延迟低
│
└── :8888 (HLS)   ←── Funnel 公网:      https://xxx.ts.net/live/stream
                          任何人无需 Tailscale 即可访问
                          走 HLS，延迟 2-5s
MJPEG 模式码率过高（3-8Mbps），通过 Tailscale 等虚拟网络观看会严重卡顿。
H.264 RTMP 模式码率仅 ~1Mbps，适合跨网络观看。
```bash
nohup tailscale funnel 8888 > /dev/null 2>&1 &   # 保持后台运行

sudo kill $(pgrep -f "tailscale funnel")         # 杀掉后台进程

https://jjuer.tailf054ab.ts.net/live/stream/    # 打开即可观看很卡
### 设置步骤
1. 开发板和手机/电脑都安装 [Tailscale](https://tailscale.com) 并登录同一账号
2. 开发板上启动 mediamtx + RTMP 推流（同上）
3. 手机/电脑浏览器打开 `http://<tailscale-ip>:8889/live/stream`
# 查看开发板 Tailscale IP：
```bash
tailscale ip -4
# 输出类似 100.90.126.70
```
也可使用 Tailscale MagicDNS 域名：`http://<hostname>.<tailnet>.ts.net:8889/live/stream`
> Tailscale IP 是固定的（100.x.x.x），不随局域网 IP 变化。

### 观看
| 方式 | 地址 | 说明 |
|------|------|------|
| WebRTC | `http://<board-ip>:8889/live/stream` | 超低延迟 (~200ms)，推荐 |
| HLS | `http://<board-ip>:8888/live/stream` | 兼容性好，延迟较高 (~3s) |
| RTSP | `rtsp://<board-ip>:8554/live/stream` | VLC 等播放器 |
> mediamtx.yml 中 RTMP 端口为 1936（非标准），因 1935 被其他进程占用。推流地址需与配置一致。

### 停止
```bash
# 停止推流程序: Ctrl+C 或
pkill rknn_yolov8_demo_stream
# 停止 mediamtx
pkill mediamtx
```

### RTMP 模式参数
| 参数 | 值 | 说明 |
|------|---|------|
| 分辨率 | 640x480 | 与摄像头采集一致（16 对齐） |
| 帧率 | 30fps | 可在代码中调整 |
| 码率 | 1Mbps CBR | 比 MJPEG 省 3-8 倍带宽 |
| 编码 | H.264 High Profile, CABAC | RK3588 VPU 硬件编码 |
| GOP | 30 | 1秒一个关键帧 |
> RTMP 连接断开后程序自动退出（不浪费 CPU 空转）。

## 修改点（相对官方）
| 文件 | 改动 |
| --- | --- |
| `examples/yolov8/cpp/CMakeLists.txt` | 新增 `_stream` 目标；`_stream` 静态链接 libturbojpeg.a；MPP/RTMP/RGA 自动检测链接；HEADLESS 模式选项（默认开启）；`install(CODE file(GLOB))` 精确打包 .so；运行时库打包到 install/lib；mediamtx + run.sh 打包；OpenSSL 链接修复（避免误匹配 NSS 的 libssl3.so，glob 兜底定位 libssl.so.3/libcrypto.so.3） |
| `examples/yolov8/cpp/main_stream.cc` | 新增：实时摄像头 + 预览 + 录像 + MJPEG/RTMP 双模式推流；HEADLESS 条件编译 |
| `examples/yolov8/cpp/v4l2_capture.{h,cc}` | 新增：直接 V4L2 采集（绕开 libv4l2）+ libturbojpeg 解码 + Multi-planar NV12/YUYV/UYVY 支持（MIPI RKISP）；YUV→RGB 用 `cv::cvtColor` 替代手写循环；`poll()` 替代 `select()`；条件 `create()` 避免重复分配 |
| `examples/yolov8/cpp/mjpeg_http_sink.{h,cc}` | 新增：MJPEG-over-HTTP 推流（兼容 mjpg_streamer） |
| `examples/yolov8/cpp/rtmp_sink.{h,cc}` | 新增：MPP H.264 编码 + RTMP 推流（异步 sender 线程）；RGA 硬件加速 RGB→NV12 + CPU 回退 |
| `examples/yolov8/cpp/rknpu2/yolov8.cc` | 修复 RKNN 输入张量维度解析：NHWC 用 `dims[3]` 取 channel；`rknn_init` 补第 5 个参数 NULL |
| `3rdparty/CMakeLists.txt` | 精确安装 5 个 OpenCV 模块 .so（core/imgproc/imgcodecs/videoio/highgui） |
| `run.sh` | 新增：启动脚本，设置 `LD_LIBRARY_PATH` 解决 OpenCV .so 残留构建路径 RUNPATH |
| `deploy_libs.sh` | 新增：自动收集运行时依赖（ldd 扫描 + 打包到 install/lib），跳过系统基础库 |

## 常见问题
**Q：浏览器连 MJPEG 推流后没有画面？**
A：PC 防火墙可能挡住了端口；或在板子上 `iptables -I INPUT -p tcp --dport 8080 -j ACCEPT`。
也可在 PC 用 VLC 打开 `http://<board-ip>:8080/` 验证。

**Q：OpenCV 报 `libopencv_core.so: cannot open shared object file`？**
A：用 `./run.sh` 启动程序（自动设置 `LD_LIBRARY_PATH`）。OpenCV .so 中残留了构建机器路径的
RUNPATH（`/home/orangepi/Desktop/opencv-4.8.0/install/lib`），`LD_LIBRARY_PATH` 可以覆盖它。

**Q：USB 摄像头报 `[v4l2] open /dev/video0 failed: No such device`？**
A：`/dev/video0` 不一定是 USB 摄像头，可能是 rkcif/ISP 等其他设备。设备号会随硬件配置变化。
请用 `v4l2-ctl --list-devices` 查看实际设备号，例如 `/dev/video20`。

**Q：UVC 摄像头报 `invalid fd` / `VIDIOCSPICT` 失败？**
A：用本项目自带的 `rknn_yolov8_demo_stream` 即可，已绕开 libv4l2 兼容层。

**Q：`rknn_inputs_set, param input size(409600) < model input size(1228800)`？**
A：模型是 3 通道 NHWC 输入，`dims[3]` 才是 channel。本项目 `rknpu2/yolov8.cc` 已修复。
如果换模型后仍报，请确认 RKNN 是 NHWC 还是 NCHW（看打印 `fmt=NHWC/NCHW`）。

**Q：浏览器看 RTMP/WebRTC 显示"连接被该站点断开"？**
A：通常是 mediamtx 未运行。需要先启动 mediamtx（`./mediamtx mediamtx.yml &`），再启动推流。
确认 mediamtx 正在监听：`ss -tlnp | grep 1936`。

**Q：mediamtx 报 `no tracks found`？**
A：RTMP 推流需要先发送 SPS/PPS（AVC sequence header），否则 mediamtx 无法识别视频轨道。
本项目已通过 `MPP_ENC_SET_HEADER_MODE` + `EACH_IDR` 自动嵌入 SPS/PPS。

**Q：RGA COLORFILL fail 日志刷屏？**
A：RGA 硬件不支持 rgb888 格式的 imfill 操作，代码有 CPU memset 兜底，检测功能正常。这是日志刷屏问题，不影响功能。

**Q：换了开发板运行不起来？**
A：
1. 拷贝整个 `rknn_yolov8_demo/` 目录到新板子
2. 安装最少传递依赖（HEADLESS 模式）：`sudo apt install libgomp1 zlib1g`
3. 用 `./run.sh ./rknn_yolov8_demo_stream ...` 启动（自动设置 LD_LIBRARY_PATH）
4. 如果新板子没有 MPP（非 Rockchip SoC），RTMP 模式不可用，请用 MJPEG 模式
5. 运行 `./deploy_libs.sh` 可自动收集缺失的依赖库

**Q：MJPEG 模式通过 Tailscale 观看很卡？**
A：MJPEG 码率 3-8Mbps，不适合虚拟网络。请使用 H.264 RTMP 模式（~1Mbps）

**Q：MIPI OV13855 摄像头报 `is not a V4L2 capture device`？**
A：如果用的是 `/dev/video0`，那是 rkcif 直出设备（只支持 Raw Bayer），不是 ISP 输出。
MIPI 摄像头应使用 `/dev/video-camera0` 或 `/dev/video11`（RKISP mainpath，输出 NV12）。

**Q：MIPI OV13855 采集帧率只有约 15fps？**
A：OV13855 sensor 输出 4224x3136@30fps，ISP 缩放到 640x480 需要处理时间。
15fps 对检测推流已足够流畅。如需更高帧率，可尝试请求 1920x1080 等中间分辨率。

**Q：HEADLESS 模式下录像功能不可用？**
A：HEADLESS 模式去除了 OpenCV videoio 依赖（VideoWriter），因此无法录像。这是设计选择，
用于减少部署依赖。如需录像，用 `-DHEADLESS=OFF` 重新编译。

**Q：mediamtx 启动后推流连不上？**
A：检查 `mediamtx.yml` 中的 `rtmpAddress` 是否为 `:1936`，推流地址需与之一致：
`rtmp://127.0.0.1:1936/live/stream`。默认 1935 端口可能被其他进程占用。
