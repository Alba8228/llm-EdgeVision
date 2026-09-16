// Copyright (c) 2023 by Rockchip Electronics Co., Ltd. All Rights Reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

/*
================================================================================
【文件总说明】
文件：main_stream.cc
功能：YOLOv8 实时摄像头检测演示程序入口
用途：通过 V4L2 直接采集摄像头画面，逐帧推理检测，支持 MJPEG/RTMP 推流和视频录制

【整体工作流程】
1. main() 从 ./llm.conf 读取全部参数（model_path/camera/width/height/fps/save_path/push/calib，
   命令行读取已删除）
2. 注册 SIGINT/SIGTERM 信号处理器，实现优雅退出
3. 根据 push_arg 创建推流对象：
   - 纯数字端口 → MjpegHttpSink（MJPEG-over-HTTP 推流）
   - rtmp:// URL → RtmpSink（H.264 硬编码 + RTMP 推流）
4. init_post_process() + init_yolov8_model()：初始化后处理和 RKNN 模型
5. run_stream_detect() 核心循环：
   5.1 V4L2Capture 直接采集 RGB 帧（跳过 BGR→RGB 转换）
   5.2 封装为 image_buffer_t，RV1106 平台拷贝到 CMA 内存
   5.3 inference_yolov8_model()：推理 + 后处理
   5.4 在 RGB 图上画检测框和标签
   5.5 预览窗口 imshow + VideoWriter 录制（HEADLESS 模式下均禁用）
   5.6 异步推流：submit() 一次 Mat swap 即返回，编码/发送在独立线程
   5.7 V4L2 连续失败超过 2s 才退出，短暂 USB 抖动不中断
6. 检测到信号/RTMP 断开/用户按 q 后退出，释放推流对象和模型资源

关键技术点：
- V4L2 直出 RGB，跳过 BGR→RGB 转换开销
- 预分配 cv::Mat 缓冲帧外复用，避免每帧 allocate/deallocate
- 异步 producer-consumer 推流模型，检测线程不阻塞
- HEADLESS 模式去除 OpenCV highgui/videoio 依赖，减少部署体积
================================================================================
*/

/*-------------------------------------------
                Includes
-------------------------------------------*/
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>
#include <signal.h>
#include <atomic>

#include <opencv2/imgproc.hpp>
#include <opencv2/core.hpp>
#include <opencv2/calib3d.hpp>   // cv::initUndistortRectifyMap (畸变矫正)
#ifndef HEADLESS
#include <opencv2/highgui.hpp>
#include <opencv2/videoio.hpp>
#endif

#include "yolov8.h"
#include "image_utils.h"
#include "file_utils.h"
#include "image_drawing.h"
#include "v4l2_capture.h"
#include "mjpeg_http_sink.h"
#include "conf_util.h"
#include "llm_agent.h"
#ifdef HAVE_MPP_RTMP
#include "rtmp_sink.h"
#endif

// RTMP 推流能力: 编译期决定
#ifdef HAVE_MPP_RTMP
#define RTMP_AVAILABLE 1
#else
#define RTMP_AVAILABLE 0
#endif

#if defined(RV1106_1103)
#include "dma_alloc.hpp"
#endif

// Ctrl+C / kill 触发的优雅退出标志 (async-signal-safe, 仅置位)
static std::atomic<int> g_stop_requested{0};

/**
 * @brief SIGINT/SIGTERM 信号处理函数，仅置位原子标志，不执行任何不安全操作
 * @param signo 信号编号（未使用）
 */
static void on_sigint(int signo)
{
    (void)signo;
    g_stop_requested.store(1);
}

/**
 * @brief 实时摄像头检测核心循环，逐帧采集→推理→绘制→推流
 * @param rknn_app_ctx RKNN 应用上下文
 * @param source 摄像头设备路径（如 /dev/video0）或序号（如 "0"）
 * @param req_w 请求分辨率宽度，0 表示使用默认值
 * @param req_h 请求分辨率高度，0 表示使用默认值
 * @param req_fps 请求帧率，0 表示使用默认值
 * @param save_path 视频录制输出路径，NULL 或空串表示不录制
 * @param show_window 是否开启预览窗口
 * @param mjpeg_sink MJPEG 推流对象指针，NULL 表示不推 MJPEG
 * @param rtmp_sink_raw RTMP 推流对象指针，NULL 表示不推 RTMP
 * @param calib_path 相机标定 YAML 文件路径，NULL 或空串表示不矫正
 * @return 0成功，-1失败
 */
static int run_stream_detect(rknn_app_context_t *rknn_app_ctx,
                             const char *source,
                             int req_w, int req_h, int req_fps,
                             const char *save_path,
                             int show_window,
                             MjpegHttpSink *mjpeg_sink,
                             void *rtmp_sink_raw,
                             const char *calib_path,
                             LlmAgent *llm_agent)
{
#ifdef HAVE_MPP_RTMP
    RtmpSink *rtmp_sink = static_cast<RtmpSink *>(rtmp_sink_raw);
#else
    (void)rtmp_sink_raw;
#endif
    V4L2Capture cap;
    if (cap.open(source, req_w, req_h, req_fps) != 0)
    {
        return -1;
    }

    int frame_w = cap.width();
    int frame_h = cap.height();
    double fps = cap.fps();
    if (fps <= 0)
        fps = 30.0; // RKISP 等设备可能不支持帧率查询, 默认 30

#ifndef HEADLESS
    cv::VideoWriter writer;
    int save_enabled = (save_path != NULL && save_path[0] != '\0');
    if (save_enabled)
    {
        writer.open(save_path,
                    cv::VideoWriter::fourcc('m', 'p', '4', 'v'),
                    fps,
                    cv::Size(frame_w, frame_h));
        if (!writer.isOpened())
        {
            printf("open output video fail! path=%s (continue without recording)\n", save_path);
            save_enabled = 0; // 关掉录制, 继续检测 + 推流
        }
        else
        {
            printf("save stream to: %s\n", save_path);
        }
    }

    if (show_window)
    {
        cv::namedWindow("yolov8_stream", cv::WINDOW_AUTOSIZE);
    }
#else
    // HEADLESS 模式: 无 VideoWriter, 无预览窗口
    int save_enabled = 0;
    if (save_path != NULL && save_path[0] != '\0')
    {
        printf("HEADLESS mode: video recording disabled (no OpenCV videoio)\n");
    }
    show_window = 0;
#endif

    // ==================== 畸变矫正预计算 (可选) ====================
    // 加载 camera_calib.yaml, 用 initUndistortRectifyMap 预计算映射表
    // 循环中每帧只做 cv::remap (查表插值), 比直接 cv::undistort 快很多
    cv::Mat map_x, map_y;
    cv::Mat undistorted_frame;  // remap 的目标 Mat (循环外预分配, 避免每帧 malloc)
    bool undistort_enabled = false;
    if (calib_path != NULL && calib_path[0] != '\0')
    {
        cv::FileStorage fs(calib_path, cv::FileStorage::READ);
        if (!fs.isOpened())
        {
            printf("Warning: cannot open calib file %s, undistort disabled\n", calib_path);
        }
        else
        {
            cv::Mat camera_matrix, dist_coeffs;
            fs["camera_matrix"] >> camera_matrix;
            fs["distortion_coefficients"] >> dist_coeffs;
            fs.release();

            if (camera_matrix.empty() || dist_coeffs.empty())
            {
                printf("Warning: invalid calib data in %s, undistort disabled\n", calib_path);
            }
            else
            {
                // 直接用原始内参作为 newCameraMatrix (无需 calib3d 的 getOptimalNewCameraMatrix)
                // 矫正后图像与原图同尺寸, 边缘可能有少量黑边
                cv::initUndistortRectifyMap(
                    camera_matrix, dist_coeffs, cv::Mat(), camera_matrix,
                    cv::Size(frame_w, frame_h), CV_32FC1, map_x, map_y);
                undistort_enabled = true;
                printf("Undistort enabled: %s (%dx%d)\n", calib_path, frame_w, frame_h);
            }
        }
    }

    // ==================== 优化: 预分配复用 Mat ====================
    // 整段循环共用 3 个 Mat, 避免每帧 new/delete / 重新分配
    cv::Mat rgb_frame; // V4L2 直出 RGB (TJPF_RGB), 同时也是绘制画布
    cv::Mat bgr_for_save;
#ifndef HEADLESS
    cv::Mat bgr_for_show;
#endif
    if (save_enabled || show_window)
    {
        bgr_for_save.create(frame_h, frame_w, CV_8UC3);
    }
#ifndef HEADLESS
    if (show_window)
    {
        bgr_for_show.create(frame_h, frame_w, CV_8UC3);
    }
#endif

    int frame_index = 0;
    int ret = 0;
    bool loop = true;
    int v4l2_fail_streak = 0;                  // 连续 V4L2 失败计数, 短暂抖动不退出
    const int v4l2_retry_us = 33000;           // 重试间隔 ~33ms
    const int v4l2_max_fail_time_us = 2000000; // 连续失败 2 秒才真退出

    while (loop)
    {
        // SIGINT / SIGTERM: 跳出循环, 让 main() 走 push_sink->stop() 清理
        if (g_stop_requested.load())
        {
            printf("signal received, stopping stream...\n");
            break;
        }

        // RTMP 发送失败 (连接断开): 自动退出, 不浪费 CPU
#ifdef HAVE_MPP_RTMP
        if (rtmp_sink != nullptr && rtmp_sink->sendFailed())
        {
            printf("RTMP connection lost, stopping stream...\n");
            break;
        }
#endif

        if (cap.read(rgb_frame) != 0 || rgb_frame.empty())
        {
            // V4L2 偶发抽风 (驱动插入非 JPEG 状态字节, USB 抖动等);
            // 短暂 sleep 后重试, 连续失败 2s 才真退出 (不依赖帧率)
            v4l2_fail_streak++;
            if (v4l2_fail_streak * v4l2_retry_us >= v4l2_max_fail_time_us)
            {
                printf("read frame fail %d times (%dms), give up.\n",
                       v4l2_fail_streak, v4l2_fail_streak * v4l2_retry_us / 1000);
                break;
            }
            printf("[v4l2] read fail (%d), retry...\n", v4l2_fail_streak);
            usleep(v4l2_retry_us);
            continue;
        }
        v4l2_fail_streak = 0;

        // 畸变矫正: remap 查表插值 (启动时已预计算 map_x/map_y)
        if (undistort_enabled)
        {
            cv::remap(rgb_frame, undistorted_frame, map_x, map_y, cv::INTER_LINEAR);
            std::swap(rgb_frame, undistorted_frame);
        }

        // ==================== 优化: 省 BGR2RGB ====================
        // 旧流程: cv::cvtColor(bgr_frame, rgb_mat, COLOR_BGR2RGB) 一次整帧转换
        // 新流程: V4L2 解码已直出 RGB, 跳过

        image_buffer_t src_image;
        memset(&src_image, 0, sizeof(image_buffer_t));
        src_image.width = rgb_frame.cols;
        src_image.height = rgb_frame.rows;
        src_image.width_stride = rgb_frame.cols;
        src_image.height_stride = rgb_frame.rows;
        src_image.format = IMAGE_FORMAT_RGB888;
        src_image.size = (int)(rgb_frame.total() * rgb_frame.elemSize());
        src_image.virt_addr = rgb_frame.data;
        src_image.fd = -1;

#if defined(RV1106_1103)
        rknn_app_ctx->img_dma_buf.size = src_image.size;
        int dma_ret = dma_buf_alloc(RV1106_CMA_HEAP_PATH, src_image.size,
                                    &rknn_app_ctx->img_dma_buf.dma_buf_fd,
                                    (void **)&(rknn_app_ctx->img_dma_buf.dma_buf_virt_addr));
        if (dma_ret != 0)
        {
            printf("dma_buf_alloc fail! ret=%d\n", dma_ret);
            ret = -1;
            break;
        }
        memcpy(rknn_app_ctx->img_dma_buf.dma_buf_virt_addr, src_image.virt_addr, src_image.size);
        dma_sync_cpu_to_device(rknn_app_ctx->img_dma_buf.dma_buf_fd);
        src_image.virt_addr = (unsigned char *)rknn_app_ctx->img_dma_buf.dma_buf_virt_addr;
        src_image.fd = rknn_app_ctx->img_dma_buf.dma_buf_fd;
#endif

        object_detect_result_list od_results;
        int inf_ret = inference_yolov8_model(rknn_app_ctx, &src_image, &od_results);
#if defined(RV1106_1103)
        dma_buf_free(rknn_app_ctx->img_dma_buf.size,
                     &rknn_app_ctx->img_dma_buf.dma_buf_fd,
                     rknn_app_ctx->img_dma_buf.dma_buf_virt_addr);
#endif
        if (inf_ret != 0)
        {
            printf("inference_yolov8_model fail! frame=%d ret=%d\n", frame_index, inf_ret);
            ret = -1;
            break;
        }

        // LLM 问答服务快照: 必须在画框之前保存干净帧 (检测框会干扰模型空间理解),
        // 检测结果以文本坐标进 prompt; 快照 = 一次 clone (~1ms), 不阻塞检测循环
        if (llm_agent != nullptr)
        {
            llm_agent->update_frame(rgb_frame);
            llm_agent->update_detections(od_results, src_image.width, src_image.height);
        }

        char text[256];
        for (int i = 0; i < od_results.count; i++)
        {
            object_detect_result *det = &(od_results.results[i]);
            int x1 = det->box.left;
            int y1 = det->box.top;
            int x2 = det->box.right;
            int y2 = det->box.bottom;

            if (x1 < 0)
                x1 = 0;
            if (y1 < 0)
                y1 = 0;
            if (x2 > src_image.width)
                x2 = src_image.width;
            if (y2 > src_image.height)
                y2 = src_image.height;

            draw_rectangle(&src_image, x1, y1, x2 - x1, y2 - y1, COLOR_BLUE, 3);
            sprintf(text, "%s %.1f%%", coco_cls_to_name(det->cls_id), det->prop * 100);
            draw_text(&src_image, text, x1, (y1 - 20 > 0 ? y1 - 20 : 0), COLOR_RED, 10);
        }

        if (frame_index % (fps > 0 ? (int)fps : 30) == 0)
        {
            printf("frame %d done, %d objects detected\n", frame_index, od_results.count);
        }

        // 显示 + 保存: RGB→BGR 只做一次, 两处复用 (HEADLESS 模式下 show_window/save_enabled 均为 0, 此块不执行)
#ifndef HEADLESS
        if (show_window || (save_enabled && writer.isOpened()))
        {
            cv::cvtColor(rgb_frame, bgr_for_save, cv::COLOR_RGB2BGR);

            if (show_window)
            {
                bgr_for_show = bgr_for_save; // 浅拷贝 (共享数据, 零开销)
                cv::imshow("yolov8_stream", bgr_for_show);
                int key = cv::waitKey(1);
                if (key == 'q' || key == 'Q' || key == 27 /* ESC */)
                {
                    printf("user quit\n");
                    loop = false;
                }
            }

            if (save_enabled && writer.isOpened())
            {
                writer.write(bgr_for_save);
            }
        }
#endif

        // ==================== 优化: 异步推流 ====================
        // submit() 只 swap 一次 Mat 句柄就返回, 编码发送在独立 sender 线程
        //     submit 走 RGB, 跳过保存时的那次 BGR2RGB 转换
        if (mjpeg_sink != nullptr)
        {
            mjpeg_sink->submit(rgb_frame, 80);
        }
#ifdef HAVE_MPP_RTMP
        if (rtmp_sink != nullptr)
        {
            rtmp_sink->submit(rgb_frame);
        }
#endif

        frame_index++;
    }

    printf("stream done, total frames=%d\n", frame_index);
#ifndef HEADLESS
    if (show_window)
        cv::destroyAllWindows();
    if (save_enabled && writer.isOpened())
        writer.release();
#endif
    return ret;
}

/**
 * @brief 实时摄像头检测程序入口，从 ./llm.conf 读参数、初始化模型和推流、运行检测循环
 * @param argc/argv 未使用（参数已全部收敛到 llm.conf，命令行读取已删除）
 * @return 0成功，-1失败
 */
int main(int argc, char **argv)
{
    // 参数已全部收敛到 ./llm.conf (detect 段), 命令行读取功能按需求删除
    (void)argc;
    (void)argv;

    static const char *CONF = "llm.conf";
    char model_buf[512] = {0}, source_buf[512] = {0};
    char save_buf[512] = {0}, push_buf[512] = {0}, calib_buf[512] = {0};

    // model_path 与 camera 是必填项, 缺失直接报错退出
    if (conf_get_str(CONF, "model_path", model_buf, sizeof(model_buf)) != 0 || model_buf[0] == '\0')
    {
        printf("[main] %s missing 'model_path' (e.g. model_path=model/best.rknn)\n", CONF);
        return -1;
    }
    if (conf_get_str(CONF, "camera", source_buf, sizeof(source_buf)) != 0 || source_buf[0] == '\0')
    {
        printf("[main] %s missing 'camera' (e.g. camera=/dev/video-camera0)\n", CONF);
        return -1;
    }
    const char *model_path = model_buf;
    const char *source = source_buf;

    // 可选项: 0/空 = 使用默认 (v4l2 默认 640x480@30, 不录制, 不推流, 不矫正)
    int req_w = conf_get_int(CONF, "width", 0);
    int req_h = conf_get_int(CONF, "height", 0);
    int req_fps = conf_get_int(CONF, "fps", 0);
    if (req_w < 0) req_w = 0;
    if (req_h < 0) req_h = 0;
    if (req_fps < 0) req_fps = 0;
    conf_get_str(CONF, "save_path", save_buf, sizeof(save_buf));
    conf_get_str(CONF, "push", push_buf, sizeof(push_buf));      // 数字端口=MJPEG, rtmp://=RTMP
    conf_get_str(CONF, "calib", calib_buf, sizeof(calib_buf));   // 相机标定 yaml, 空=不矫正
    const char *save_path = save_buf[0] ? save_buf : NULL;
    const char *push_arg = push_buf[0] ? push_buf : NULL;
    const char *calib_path = calib_buf[0] ? calib_buf : NULL;

    printf("[main] conf: model=%s camera=%s %dx%d@%d push=%s save=%s calib=%s\n",
           model_path, source, req_w, req_h, req_fps,
           push_arg ? push_arg : "(off)", save_path ? save_path : "(off)",
           calib_path ? calib_path : "(off)");

    // 预览窗口: 只要有 DISPLAY 环境变量就打开; 在没有显示的板子上保持关闭
    int show_window = (getenv("DISPLAY") != NULL) ? 1 : 0;
    if (show_window)
    {
        printf("preview enabled (DISPLAY=%s)\n", getenv("DISPLAY"));
    }
    else
    {
        printf("preview disabled (no DISPLAY)\n");
    }

    // Ctrl+C / kill 走优雅退出, 让 push_sink->stop() 能清理监听 socket
    struct sigaction sa;
    memset(&sa, 0, sizeof(sa));
    sa.sa_handler = on_sigint;
    sigemptyset(&sa.sa_mask);
    sa.sa_flags = 0; // 不要 SA_RESTART, 让 accept() 立即被 EINTR 打断
    sigaction(SIGINT, &sa, nullptr);
    sigaction(SIGTERM, &sa, nullptr);
    // SIGPIPE 写到已关闭的 socket 时默认终止进程, 这里忽略避免杀进程
    signal(SIGPIPE, SIG_IGN);

    // 推流: 根据 push_arg 判断是 MJPEG (纯数字端口) 还是 RTMP (rtmp:// URL)
    MjpegHttpSink *mjpeg_sink = nullptr;
#ifdef HAVE_MPP_RTMP
    RtmpSink *rtmp_sink = nullptr;
#else
    void *rtmp_sink = nullptr;
#endif

    if (push_arg != NULL && push_arg[0] != '\0')
    {
        if (strncmp(push_arg, "rtmp://", 7) == 0)
        {
#ifdef HAVE_MPP_RTMP
            // RTMP 推流模式 (H.264 + mediamtx/WebRTC)
            rtmp_sink = new RtmpSink();
            if (rtmp_sink->open(push_arg,
                                req_w > 0 ? req_w : 640,
                                req_h > 0 ? req_h : 480,
                                req_fps > 0 ? req_fps : 30) != 0)
            {
                printf("RTMP open failed: %s\n", push_arg);
                delete rtmp_sink;
                rtmp_sink = nullptr;
            }
#else
            printf("RTMP streaming not available (compiled without MPP/librtmp support)\n");
            printf("Install librockchip-mpp-dev and librtmp-dev, then recompile.\n");
#endif
        }
        else
        {
            // MJPEG-over-HTTP 推流模式 (传统)
            int push_port = atoi(push_arg);
            if (push_port > 0)
            {
                mjpeg_sink = new MjpegHttpSink();
                if (mjpeg_sink->start(push_port) != 0)
                {
                    printf("start MJPEG push server on port %d failed\n", push_port);
                    delete mjpeg_sink;
                    mjpeg_sink = nullptr;
                }
            }
        }
    }

    rknn_app_context_t rknn_app_ctx;
    memset(&rknn_app_ctx, 0, sizeof(rknn_app_context_t));

    init_post_process();

    // LLM 场景感知问答服务: 读 ./llm.conf, 缺失或无 api_key 则禁用 (不影响检测推流)
    LlmAgent llm_agent;
    llm_agent_config_t llm_cfg;
    int llm_enabled = 0;
    if (LlmAgent::load_config("llm.conf", &llm_cfg) == 0 && llm_agent.start(llm_cfg) == 0)
    {
        llm_enabled = 1;
    }
    else
    {
        printf("[llm] disabled (llm.conf missing or start failed)\n");
    }

    int ret = init_yolov8_model(model_path, &rknn_app_ctx);
    if (ret != 0)
    {
        printf("init_yolov8_model fail! ret=%d model_path=%s\n", ret, model_path);
        deinit_post_process();
        if (mjpeg_sink)
        {
            mjpeg_sink->stop();
            delete mjpeg_sink;
        }
#ifdef HAVE_MPP_RTMP
        if (rtmp_sink)
        {
            rtmp_sink->close();
            delete rtmp_sink;
        }
#endif
        return -1;
    }

    ret = run_stream_detect(&rknn_app_ctx, source, req_w, req_h, req_fps,
                            save_path, show_window, mjpeg_sink, rtmp_sink, calib_path,
                            llm_enabled ? &llm_agent : nullptr);

    deinit_post_process();
    release_yolov8_model(&rknn_app_ctx);

    llm_agent.stop();

    if (mjpeg_sink)
    {
        mjpeg_sink->stop();
        delete mjpeg_sink;
    }
#ifdef HAVE_MPP_RTMP
    if (rtmp_sink)
    {
        rtmp_sink->close();
        delete rtmp_sink;
    }
#endif
    return ret;
}
