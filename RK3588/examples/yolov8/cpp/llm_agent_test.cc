/*
================================================================================
【文件总说明】
文件：llm_agent_test.cc
功能：LlmAgent 独立链路测试程序（不依赖摄像头/NPU 推理）
用途：开发期验证 "TCP 协议 → 帧快照 → JPEG → API 调用 → SSE 流式回传" 全链路
用法：./llm_agent_test <llm.conf> <图片路径>
  然后用 nc 或上位机连 TCP 端口发 {"t":"ask","q":"..."} 观察回包
工作流程：
  1. load_config + start：起 TCP 服务
  2. imread 一张图作为"最新帧"持续喂 update_frame（模拟检测线程）
  3. 构造 2 个假检测目标喂 update_detections（模拟 YOLO 输出）
  4. Ctrl+C 退出
================================================================================
*/

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>
#include <signal.h>
#include <atomic>

#include <opencv2/core.hpp>

#include "turbojpeg.h"
#include "llm_agent.h"
#include "yolov8.h"   // 提供 object_detect_result_list 及 init_post_process/coco_cls_to_name

static std::atomic<int> g_run{1};

/**
 * @brief SIGINT 处理: 置退出标志
 * @param s 信号编号 (未用)
 */
static void on_sigint(int s) { (void)s; g_run.store(0); }

int main(int argc, char **argv)
{
    const char *conf = (argc > 1) ? argv[1] : "llm.conf";
    const char *img_path = (argc > 2) ? argv[2] : "model/bus.jpg";

    llm_agent_config_t cfg;
    if (LlmAgent::load_config(conf, &cfg) != 0)
        return -1;

    LlmAgent agent;
    if (agent.start(cfg) != 0)
        return -1;

    // 类别名表 (coco_cls_to_name 依赖, 标签文件路径写死在 postprocess.h LABEL_NALE_TXT_PATH)
    init_post_process();

    // 用 turbojpeg 解码 JPEG → RGB Mat (不用 cv::imread: 静态 libturbojpeg.a
    // 与 OpenCV imgcodecs 内置 libjpeg 符号冲突会导致解码失败, 与主程序同策略)
    cv::Mat img;
    {
        FILE *fp = fopen(img_path, "rb");
        if (!fp) { printf("[test] open fail: %s\n", img_path); agent.stop(); return -1; }
        fseek(fp, 0, SEEK_END); long fsz = ftell(fp); fseek(fp, 0, SEEK_SET);
        std::vector<unsigned char> fbuf((size_t)fsz);
        size_t rd = fread(fbuf.data(), 1, (size_t)fsz, fp);
        fclose(fp);
        if ((long)rd != fsz) { printf("[test] read fail\n"); agent.stop(); return -1; }

        tjhandle tj = tjInitDecompress();
        int w, h, subsamp, cs;
        if (tj && tjDecompressHeader3(tj, fbuf.data(), (unsigned long)fsz, &w, &h, &subsamp, &cs) == 0)
        {
            img.create(h, w, CV_8UC3);
            if (tjDecompress2(tj, fbuf.data(), (unsigned long)fsz, img.data, w, 0, h,
                              TJPF_RGB, TJFLAG_FASTDCT) != 0)
                img.release();
            tjDestroy(tj);
        }
        if (img.empty()) { printf("[test] jpeg decode fail: %s\n", img_path); agent.stop(); return -1; }
    }
    printf("[test] frame %dx%d loaded, feeding fake detections\n", img.cols, img.rows);

    // 构造 2 个假检测目标 (像素坐标, 类别 0/1)
    object_detect_result_list od;
    memset(&od, 0, sizeof(od));
    od.count = 2;
    od.results[0].box.left = (int)(img.cols * 0.15); od.results[0].box.top = (int)(img.rows * 0.2);
    od.results[0].box.right = (int)(img.cols * 0.45); od.results[0].box.bottom = (int)(img.rows * 0.8);
    od.results[0].prop = 0.92f; od.results[0].cls_id = 0;
    od.results[1].box.left = (int)(img.cols * 0.6); od.results[1].box.top = (int)(img.rows * 0.4);
    od.results[1].box.right = (int)(img.cols * 0.85); od.results[1].box.bottom = (int)(img.rows * 0.65);
    od.results[1].prop = 0.77f; od.results[1].cls_id = 1;

    signal(SIGINT, on_sigint);
    while (g_run.load())
    {
        agent.update_frame(img);
        agent.update_detections(od, img.cols, img.rows);
        usleep(100 * 1000); // 10Hz 模拟检测刷新
    }

    agent.stop();
    deinit_post_process();
    printf("[test] exited\n");
    return 0;
}
