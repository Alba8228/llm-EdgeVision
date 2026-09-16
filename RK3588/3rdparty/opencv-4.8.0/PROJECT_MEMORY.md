# 项目记忆：OpenCV 4.8.0 源码工程

## 项目概况
- 路径：/home/orangepi/Desktop/opencv-4.8.0
- 平台：aarch64（orangepi / 香橙派，arm64 Linux）
- 版本：OpenCV 4.8.0 源码树，已做 cmake 配置并部分编译

## 构建配置（关键，改动前必读）
- 构建目录：build/（CMake 配置已在其中）
- 构建类型：Release
- **关键约束：`BUILD_LIST=core,imgproc,imgcodecs,videoio,highgui,flann,features2d,calib3d,objdetect`**
  - 见 build/CMakeCache.txt 第 132 行
  - 只有这 9 个模块被实际编译；其他模块即使 `BUILD_opencv_xxx=ON` 也会被 BUILD_LIST 过滤掉
  - 若后续需要 dnn/ml/video 等模块，必须把模块名加进 BUILD_LIST 后重新 cmake
- install 前缀：/home/orangepi/Desktop/opencv/opencv-4.8.0/install（注意：安装前缀的父路径与当前工程路径不一致，目录可能被移动过）
- 语言绑定：js、python3 显式 OFF；java/objc 仅生成器 ON

## 当前编译产物
- build/lib 下 9 个动态库：core, imgproc, imgcodecs, videoio, highgui, flann, features2d, calib3d, objdetect
- build/bin 下 4 个工具：opencv_version, opencv_annotation, opencv_interactive-calibration, opencv_visualisation
- install/ 下仅 5 个库（core/imgproc/imgcodecs/videoio/highgui），少于 build 产物，install 可能未完整执行（未验证）

## 未编译模块（源码存在，BUILD_LIST 未包含）
dnn（深度学习推理）、gapi（计算图）、ml（传统机器学习）、photo（计算摄影）、
stitching（图像拼接）、video（视频分析/光流/跟踪）、ts（测试框架）、world（聚合库）、
java/js/objc/python（语言绑定）

## 注意
- 该项目使用场景为 aarch64 嵌入式板卡，规则上应避免 NEON SIMD 等不易移植的优化
- 排查外设类问题时遵循：系统识别 → 应用逻辑 → 总线 → 设备树/内核日志
