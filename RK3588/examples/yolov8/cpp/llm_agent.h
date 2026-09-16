/*
================================================================================
【文件总说明】
文件：llm_agent.h
功能：YOLOv8 实时检测 + 外接 DeepSeek 多模态 API 的"场景感知问答"服务头文件
平台：RK3588 (香橙派 5 Max)

【整体工作流程】
1. main_stream 每帧推理后调用 update_frame()/update_detections() 写入快照
   （各自加锁，检测线程只多一次 memcpy，不阻塞）
2. LlmAgent::start() 启动：
   - accept 线程：监听 TCP 端口，接收上位机 JSON 行指令
   - worker 线程：串行处理提问（一次只跑一个 API 调用，忙时回 busy）
3. 上位机提问 {"t":"ask","q":"..."} 时，worker：
   3.1 取最新干净帧快照 → turbojpeg 编 JPEG → base64
   3.2 取最新检测结果快照 → 按常驻 ROI 过滤 → 拼成文本坐标
   3.3 组 OpenAI 兼容 chat/completions 请求 (stream=true)
   3.4 libcurl HTTPS 调用，SSE 回调里逐 token 转发给上位机
   3.5 发 {"t":"done"} 结束本轮
4. ROI 指令 {"t":"roi","enable":1,"box":[..]} 更新常驻锁定区域（归一化 0~1）

关键技术点：
- 干净帧上传：画框前的 RGB 快照，避免检测框干扰模型空间理解；坐标以文本进 prompt
- ROI 仅过滤检测结果文本，不裁剪图片、不影响推流画面（推流照旧全画）
- 按需调用：只有 ask 才发起 HTTPS，平时零外网流量
- 线程安全：帧/结果/ROI/客户端 fd 各自独立 mutex
================================================================================
*/

#ifndef LLM_AGENT_H
#define LLM_AGENT_H

#include <string>
#include <vector>
#include <mutex>
#include <atomic>
#include <condition_variable>
#include <pthread.h>
#include <opencv2/core.hpp>

#include "yolov8.h"   // 提供 rknn_app_context_t 及 object_detect_result_list (postprocess.h)

/**
 * @brief LLM 问答服务配置参数（从 llm.conf 加载）
 */
typedef struct
{
    std::string api_key;        // DeepSeek API Key
    std::string base_url;       // 例 https://api.deepseek.com/chat/completions
    std::string model;          // 例 deepseek-v4-flash-vision-exp
    int         port = 12346;   // TCP 监听端口
    int         jpeg_quality = 80;   // 上传帧 JPEG 质量
    int         timeout_s = 60;      // 单次 API 调用超时
    int         max_new_tokens = 512;// 回答最大 token
    std::string system_prompt;       // 系统提示词
} llm_agent_config_t;

/**
 * @brief 常驻 ROI 状态（归一化坐标 0~1，enable=0 表示不过滤）
 */
typedef struct
{
    int   enable;
    float x1, y1, x2, y2;
} llm_roi_t;

/**
 * @class LlmAgent
 * @brief 场景感知问答服务：TCP 接入 + 帧/结果快照 + DeepSeek vision API 调用
 */
class LlmAgent
{
public:
    LlmAgent();
    ~LlmAgent();

    /**
     * @brief 从 .conf 文件加载配置（key=value 极简格式）
     * @param conf_path 配置文件路径
     * @param out 输出的配置结构
     * @return 0 成功，-1 失败（文件不存在或 api_key 缺失）
     */
    static int load_config(const char *conf_path, llm_agent_config_t *out);

    /**
     * @brief 启动服务（accept 线程 + worker 线程）
     * @param cfg 配置
     * @return 0 成功，-1 失败（端口占用等）
     */
    int start(const llm_agent_config_t &cfg);

    /**
     * @brief 停止服务，回收线程与 socket
     */
    void stop();

    /**
     * @brief 检测线程每帧调用：更新干净帧快照（画框前的 RGB）
     * @param rgb 最新一帧 RGB 图像（cv::Mat，会被 clone 保存）
     */
    void update_frame(const cv::Mat &rgb);

    /**
     * @brief 检测线程每帧调用：更新检测结果快照
     * @param od 当前帧检测结果列表
     * @param frame_w 检测帧宽度（坐标归一化用）
     * @param frame_h 检测帧高度
     */
    void update_detections(const object_detect_result_list &od, int frame_w, int frame_h);

    // 向当前客户端发送一行 JSON（带换行）；返回 0 成功，-1 失败（无连接/对端断开）
    // public: SSE 解析回调（llm_agent.cc 文件级函数）需要回发 token
    int send_line(const std::string &json);

    // 仅当当前活动客户端仍是 fd 时才发送（防止旧 worker 把 token 串扰到新客户端）
    // 返回 0 成功；-1 表示连接已切换/断开，调用方应中断本轮请求
    int send_line_fd(int fd, const std::string &json);

private:
    llm_agent_config_t cfg_;

    // ==================== 共享快照 ====================
    std::mutex frame_mutex_;
    cv::Mat    frame_;                 // 最新干净帧 (RGB)

    std::mutex det_mutex_;
    object_detect_result_list dets_;   // 最新检测结果
    int        det_frame_w_ = 0;       // 检测结果对应帧宽（用于归一化坐标）
    int        det_frame_h_ = 0;

    std::mutex roi_mutex_;
    llm_roi_t  roi_;                   // 常驻 ROI

    // ==================== 网络与线程 ====================
    int listen_fd_ = -1;
    std::atomic<bool> stop_flag_{false};
    pthread_t accept_thread_;
    pthread_t worker_thread_;
    bool      accept_started_ = false;
    bool      worker_started_ = false;

    // 当前活动客户端 fd（单客户端模型：最新连接替换旧的）
    std::mutex client_mutex_;
    int        client_fd_ = -1;

    // 提问任务队列（worker 串行消费）
    std::mutex              task_mutex_;
    std::condition_variable task_cv_;
    std::vector<std::string> task_queue_;  // 每项为已解析的 question 文本
    bool                    busy_ = false; // worker 正在回答中（ask 到来直接回 busy）

    static void *accept_thread_fn(void *arg);
    static void *worker_thread_fn(void *arg);
    void accept_loop();
    void worker_loop();

    // 处理一行客户端指令
    void handle_line(const std::string &line, int fd);
    // 执行一次提问：组请求→调 API→流式回传
    void answer_question(const std::string &question);
    // 把检测结果按 ROI 过滤后拼成文本（归一化坐标）
    std::string build_detection_text();
};

#endif // LLM_AGENT_H
