/*
================================================================================
【文件总说明】
文件：llm_agent.cc
功能：YOLOv8 实时检测 + 外接 DeepSeek 多模态 API 的"场景感知问答"服务实现
平台：RK3588 (香橙派 5 Max)

【整体工作流程】
1. load_config(): 读 llm.conf (key=value), 校验 api_key 非空
2. start(): 创建监听 socket → 启动 accept 线程 + worker 线程
3. accept 线程 accept_loop():
   3.1 poll 等待新连接/poll 已连接客户端可读 (SO_RCVTIMEO 打断)
   3.2 新连接到来: 关闭旧 client_fd_, 只保留最新一个客户端
   3.3 按行读取 JSON 指令, 交给 handle_line()
4. handle_line():
   - {"t":"ask","q":"..."}   → 问题入队, 唤醒 worker
   - {"t":"roi",...}         → 更新常驻 ROI
   - {"t":"state"}           → 回 {"t":"state","roi":..,"busy":..}
5. worker 线程 worker_loop():
   串行取问题 → answer_question():
   5.1 帧快照 clone → turbojpeg 编 JPEG → base64
   5.2 build_detection_text(): 检测结果按 ROI 中心点过滤 → 归一化坐标文本
   5.3 cJSON 组 OpenAI chat/completions 请求 (stream=true)
   5.4 curl_easy_perform + SSE 回调: 逐行剥 "data:", 取 delta.content
       转发 {"t":"token","text":".."} 给客户端; 发送失败中断请求
   5.5 发 {"t":"done"} 或 {"t":"error",...}
6. stop(): 置停止标志, 关 socket 打断 poll, join 两个线程

关键技术点：
- SSE 解析按 OpenAI 兼容格式实现 (data: {...}\n\n, [DONE] 结束标记),
  实际 DeepSeek vision 模型报文以步骤 0 冒烟测试抓取的真实样本为准
- 干净帧上传 + 文本坐标: 检测框不画进上传图, 避免干扰模型空间理解
- ROI 只过滤"哪些目标写进 prompt", 不裁剪图片, 不影响推流画面
- 单客户端 + 串行提问: 推理期间新提问直接回 busy, 不排队堆积
- curl 写回调运行在 worker 线程内, client_fd_ 发送无并发竞争
================================================================================
*/

#include "llm_agent.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>
#include <errno.h>
#include <fcntl.h>
#include <poll.h>
#include <sys/socket.h>
#include <netinet/in.h>
#include <netinet/tcp.h>
#include <arpa/inet.h>

#include <curl/curl.h>
#include "turbojpeg.h"
#include "cJSON.h"

/*-------------------------------------------
                小工具
-------------------------------------------*/

/**
 * @brief 标准 base64 编码 (无换行)
 * @param in 输入二进制数据
 * @param in_len 输入长度
 * @return base64 字符串
 */
static std::string base64_encode(const unsigned char *in, size_t in_len)
{
    static const char tbl[] = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
    std::string out;
    out.reserve(((in_len + 2) / 3) * 4);
    size_t i = 0;
    while (i + 3 <= in_len)
    {
        unsigned v = (in[i] << 16) | (in[i + 1] << 8) | in[i + 2];
        out += tbl[(v >> 18) & 63];
        out += tbl[(v >> 12) & 63];
        out += tbl[(v >> 6) & 63];
        out += tbl[v & 63];
        i += 3;
    }
    if (i < in_len) // 处理尾部 1~2 字节 + padding
    {
        unsigned v = in[i] << 16;
        if (in_len - i == 2)
            v |= in[i + 1] << 8;
        out += tbl[(v >> 18) & 63];
        out += tbl[(v >> 12) & 63];
        out += (in_len - i == 2) ? tbl[(v >> 6) & 63] : '=';
        out += '=';
    }
    return out;
}

/**
 * @brief 去除字符串首尾空白字符
 * @param s 输入串
 * @return 修剪后的串
 */
static std::string str_trim(const std::string &s)
{
    size_t b = s.find_first_not_of(" \t\r\n");
    if (b == std::string::npos)
        return "";
    size_t e = s.find_last_not_of(" \t\r\n");
    return s.substr(b, e - b + 1);
}

/*-------------------------------------------
                配置加载
-------------------------------------------*/

/**
 * @brief 从 .conf 文件加载配置 (key=value, # 注释, 空行跳过)
 * @param conf_path 配置文件路径
 * @param out 输出配置
 * @return 0 成功, -1 失败
 */
int LlmAgent::load_config(const char *conf_path, llm_agent_config_t *out)
{
    FILE *fp = fopen(conf_path, "r");
    if (!fp)
    {
        printf("[llm] open conf fail: %s\n", conf_path);
        return -1;
    }

    // 默认值 (model/base_url 为当前 DeepSeek vision 实验模型, 以实测为准)
    out->base_url = "https://api.deepseek.com/chat/completions";
    out->model = "deepseek-v4-flash-vision-exp";
    out->port = 12346;
    out->jpeg_quality = 80;
    out->timeout_s = 60;
    out->max_new_tokens = 512;
    out->system_prompt =
        "你是车载视觉助手。用户会提供摄像头当前画面(干净帧,无叠加框)和YOLO检测结果"
        "(类别 置信度 归一化坐标[x1,y1,x2,y2],原点左上)。请结合图片与检测结果回答,简洁。";

    char line[1024];
    while (fgets(line, sizeof(line), fp))
    {
        std::string s = str_trim(line);
        if (s.empty() || s[0] == '#')
            continue;
        size_t eq = s.find('=');
        if (eq == std::string::npos)
            continue;
        std::string k = str_trim(s.substr(0, eq));
        std::string v = str_trim(s.substr(eq + 1));
        // 剥离行内注释: 值中 " #" 之后视为注释 (与 conf_util.c 行为一致,
        // 防止 model=xxx # 名字 把注释带进字符串导致 API 报错)
        size_t hp = v.find(" #");
        if (hp != std::string::npos)
            v = str_trim(v.substr(0, hp));
        if      (k == "api_key")         out->api_key = v;
        else if (k == "base_url")        out->base_url = v;
        else if (k == "model")           out->model = v;
        else if (k == "port")            out->port = atoi(v.c_str());
        else if (k == "jpeg_quality")    out->jpeg_quality = atoi(v.c_str());
        else if (k == "timeout_s")       out->timeout_s = atoi(v.c_str());
        else if (k == "max_new_tokens")  out->max_new_tokens = atoi(v.c_str());
        else if (k == "system_prompt")   out->system_prompt = v;
    }
    fclose(fp);

    if (out->api_key.empty())
    {
        printf("[llm] conf missing api_key: %s\n", conf_path);
        return -1;
    }
    printf("[llm] conf loaded: model=%s port=%d timeout=%ds\n",
           out->model.c_str(), out->port, out->timeout_s);
    return 0;
}

/*-------------------------------------------
                构造/生命周期
-------------------------------------------*/

LlmAgent::LlmAgent()
{
    memset(&dets_, 0, sizeof(dets_));
    memset(&roi_, 0, sizeof(roi_));
}

LlmAgent::~LlmAgent()
{
    stop();
}

/**
 * @brief 启动 TCP 服务与两个工作线程
 * @param cfg 配置
 * @return 0 成功, -1 失败
 */
int LlmAgent::start(const llm_agent_config_t &cfg)
{
    cfg_ = cfg;
    stop_flag_.store(false);

    listen_fd_ = socket(AF_INET, SOCK_STREAM, 0);
    if (listen_fd_ < 0)
    {
        printf("[llm] socket fail: %s\n", strerror(errno));
        return -1;
    }
    int opt = 1;
    setsockopt(listen_fd_, SOL_SOCKET, SO_REUSEADDR, &opt, sizeof(opt));

    struct sockaddr_in addr;
    memset(&addr, 0, sizeof(addr));
    addr.sin_family = AF_INET;
    addr.sin_addr.s_addr = INADDR_ANY;
    addr.sin_port = htons((uint16_t)cfg_.port);
    if (bind(listen_fd_, (struct sockaddr *)&addr, sizeof(addr)) != 0 ||
        listen(listen_fd_, 4) != 0)
    {
        printf("[llm] bind/listen port %d fail: %s\n", cfg_.port, strerror(errno));
        close(listen_fd_);
        listen_fd_ = -1;
        return -1;
    }

    if (pthread_create(&accept_thread_, NULL, accept_thread_fn, this) != 0)
    {
        printf("[llm] accept thread create fail\n");
        close(listen_fd_);
        listen_fd_ = -1;
        return -1;
    }
    accept_started_ = true;

    if (pthread_create(&worker_thread_, NULL, worker_thread_fn, this) != 0)
    {
        printf("[llm] worker thread create fail\n");
        stop_flag_.store(true);
        if (listen_fd_ >= 0) { close(listen_fd_); listen_fd_ = -1; }
        pthread_join(accept_thread_, NULL);
        return -1;
    }
    worker_started_ = true;

    printf("[llm] service started on TCP port %d\n", cfg_.port);
    return 0;
}

/**
 * @brief 停止服务: 关 socket 打断 poll, join 线程, 关闭客户端连接
 */
void LlmAgent::stop()
{
    if (listen_fd_ < 0 && !accept_started_)
        return;

    stop_flag_.store(true);
    if (listen_fd_ >= 0)
    {
        close(listen_fd_); // 打断 accept/poll
        listen_fd_ = -1;
    }
    {
        std::lock_guard<std::mutex> lk(client_mutex_);
        if (client_fd_ >= 0)
        {
            close(client_fd_);
            client_fd_ = -1;
        }
    }
    task_cv_.notify_all(); // 唤醒空闲 worker

    if (accept_started_) { pthread_join(accept_thread_, NULL); accept_started_ = false; }
    if (worker_started_) { pthread_join(worker_thread_, NULL); worker_started_ = false; }
    printf("[llm] service stopped\n");
}

/*-------------------------------------------
                快照更新 (检测线程调用)
-------------------------------------------*/

/**
 * @brief 更新干净帧快照 (画框前调用)
 * @param rgb 最新 RGB 帧
 */
void LlmAgent::update_frame(const cv::Mat &rgb)
{
    // rgb 是 main_stream 预分配复用的缓冲, 像素会被下一帧覆盖 → 必须深拷贝
    cv::Mat deep = rgb.clone();
    std::lock_guard<std::mutex> lk(frame_mutex_);
    std::swap(frame_, deep); // 只换句柄, 旧帧缓冲在 deep 析构时释放
}

/**
 * @brief 更新检测结果快照
 * @param od 当前帧检测结果
 * @param frame_w 检测帧宽度 (用于坐标归一化)
 * @param frame_h 检测帧高度
 */
void LlmAgent::update_detections(const object_detect_result_list &od, int frame_w, int frame_h)
{
    std::lock_guard<std::mutex> lk(det_mutex_);
    dets_ = od;
    det_frame_w_ = frame_w;
    det_frame_h_ = frame_h;
}

/*-------------------------------------------
                accept 线程
-------------------------------------------*/

void *LlmAgent::accept_thread_fn(void *arg)
{
    ((LlmAgent *)arg)->accept_loop();
    return NULL;
}

/**
 * @brief accept 主循环: 单客户端模型, 新连接替换旧连接; 按行分发指令
 *
 * 实现说明: 用 poll 同时等待 listen_fd 和 client_fd, 200ms 超时轮询
 * stop_flag_, 保证 stop() 能及时退出。
 */
void LlmAgent::accept_loop()
{
    std::string rbuf; // 接收行缓冲
    int cur_fd = -1;  // 本线程持有的当前客户端 fd

    while (!stop_flag_.load())
    {
        struct pollfd pfds[2];
        int nfds = 0;
        pfds[nfds].fd = listen_fd_;
        pfds[nfds].events = POLLIN;
        nfds++;
        if (cur_fd >= 0)
        {
            pfds[nfds].fd = cur_fd;
            pfds[nfds].events = POLLIN;
            nfds++;
        }

        int pr = poll(pfds, nfds, 200);
        if (stop_flag_.load())
            break;
        if (pr < 0)
        {
            if (errno == EINTR)
                continue;
            printf("[llm] poll err: %s\n", strerror(errno));
            break;
        }
        if (pr == 0)
            continue;

        // ---- 新连接 ----
        if (pfds[0].revents & POLLIN)
        {
            int fd = accept(listen_fd_, NULL, NULL);
            if (fd >= 0)
            {
                int opt = 1;
                setsockopt(fd, IPPROTO_TCP, TCP_NODELAY, &opt, sizeof(opt));
                std::lock_guard<std::mutex> lk(client_mutex_);
                if (cur_fd >= 0)
                    close(cur_fd); // 踢掉旧客户端
                cur_fd = fd;
                client_fd_ = fd;
                rbuf.clear();
                printf("[llm] client connected (fd=%d)\n", fd);
            }
        }

        // ---- 客户端数据 ----
        if (cur_fd >= 0 && (pfds[1].revents & POLLIN))
        {
            char tmp[2048];
            ssize_t n = recv(cur_fd, tmp, sizeof(tmp), 0);
            if (n <= 0)
            {
                printf("[llm] client closed (n=%zd)\n", n);
                std::lock_guard<std::mutex> lk(client_mutex_);
                close(cur_fd);
                if (client_fd_ == cur_fd)
                    client_fd_ = -1;
                cur_fd = -1;
                rbuf.clear();
                continue;
            }
            rbuf.append(tmp, (size_t)n);
            // 按行切分处理 (可能一行含多条, 逐条弹出)
            size_t pos;
            while ((pos = rbuf.find('\n')) != std::string::npos)
            {
                std::string line = str_trim(rbuf.substr(0, pos));
                rbuf.erase(0, pos + 1);
                if (!line.empty())
                    handle_line(line, cur_fd);
            }
            if (rbuf.size() > 65536)
                rbuf.clear(); // 异常超长行防御
        }
        // 客户端异常 (HUP/ERR)
        if (cur_fd >= 0 && (pfds[1].revents & (POLLHUP | POLLERR)))
        {
            std::lock_guard<std::mutex> lk(client_mutex_);
            close(cur_fd);
            if (client_fd_ == cur_fd)
                client_fd_ = -1;
            cur_fd = -1;
            rbuf.clear();
        }
    }

    if (cur_fd >= 0)
    {
        std::lock_guard<std::mutex> lk(client_mutex_);
        close(cur_fd);
        if (client_fd_ == cur_fd)
            client_fd_ = -1;
    }
}

/**
 * @brief 解析并处理一行客户端 JSON 指令
 * @param line 不含换行的 JSON 文本
 * @param fd 来源连接 (仅用于日志)
 */
void LlmAgent::handle_line(const std::string &line, int fd)
{
    (void)fd;
    cJSON *root = cJSON_Parse(line.c_str());
    if (!root)
    {
        send_line("{\"t\":\"error\",\"msg\":\"bad json\"}");
        return;
    }
    cJSON *t = cJSON_GetObjectItemCaseSensitive(root, "t");
    const char *tv = (cJSON_IsString(t) && t->valuestring) ? t->valuestring : "";

    if (strcmp(tv, "ask") == 0)
    {
        cJSON *q = cJSON_GetObjectItemCaseSensitive(root, "q");
        if (cJSON_IsString(q) && q->valuestring && q->valuestring[0])
        {
            {
                std::lock_guard<std::mutex> lk(task_mutex_);
                // worker 正在回答 (busy_) 或队列已有待处理任务 → 直接回 busy, 不堆积
                if (busy_ || !task_queue_.empty())
                {
                    cJSON_Delete(root);
                    send_line("{\"t\":\"error\",\"msg\":\"busy\"}");
                    return;
                }
                task_queue_.emplace_back(q->valuestring);
            }
            task_cv_.notify_one();
        }
        else
        {
            send_line("{\"t\":\"error\",\"msg\":\"missing q\"}");
        }
    }
    else if (strcmp(tv, "roi") == 0)
    {
        cJSON *en = cJSON_GetObjectItemCaseSensitive(root, "enable");
        std::lock_guard<std::mutex> lk(roi_mutex_);
        roi_.enable = cJSON_IsNumber(en) ? (en->valueint != 0) : 0;
        cJSON *box = cJSON_GetObjectItemCaseSensitive(root, "box");
        if (roi_.enable && cJSON_IsArray(box) && cJSON_GetArraySize(box) == 4)
        {
            roi_.x1 = (float)cJSON_GetArrayItem(box, 0)->valuedouble;
            roi_.y1 = (float)cJSON_GetArrayItem(box, 1)->valuedouble;
            roi_.x2 = (float)cJSON_GetArrayItem(box, 2)->valuedouble;
            roi_.y2 = (float)cJSON_GetArrayItem(box, 3)->valuedouble;
            printf("[llm] ROI set [%.2f,%.2f,%.2f,%.2f]\n", roi_.x1, roi_.y1, roi_.x2, roi_.y2);
        }
        else
        {
            roi_.enable = 0;
            printf("[llm] ROI cleared\n");
        }
    }
    else if (strcmp(tv, "state") == 0)
    {
        bool busy;
        {
            std::lock_guard<std::mutex> lk(task_mutex_);
            busy = busy_ || !task_queue_.empty();
        }
        llm_roi_t r;
        {
            std::lock_guard<std::mutex> lk(roi_mutex_);
            r = roi_;
        }
        char buf[256];
        snprintf(buf, sizeof(buf),
                 "{\"t\":\"state\",\"roi_enable\":%d,\"roi\":[%.4f,%.4f,%.4f,%.4f],\"busy\":%d}",
                 r.enable, r.x1, r.y1, r.x2, r.y2, busy ? 1 : 0);
        send_line(buf);
    }
    else
    {
        send_line("{\"t\":\"error\",\"msg\":\"unknown t\"}");
    }
    cJSON_Delete(root);
}

/*-------------------------------------------
                worker 线程
-------------------------------------------*/

void *LlmAgent::worker_thread_fn(void *arg)
{
    ((LlmAgent *)arg)->worker_loop();
    return NULL;
}

/**
 * @brief worker 主循环: 串行取出问题并调用 API 回答
 */
void LlmAgent::worker_loop()
{
    while (!stop_flag_.load())
    {
        std::string question;
        {
            std::unique_lock<std::mutex> lk(task_mutex_);
            task_cv_.wait_for(lk, std::chrono::milliseconds(200),
                              [this] { return !task_queue_.empty() || stop_flag_.load(); });
            if (stop_flag_.load())
                break;
            if (task_queue_.empty())
                continue;
            question = task_queue_.front();
            task_queue_.erase(task_queue_.begin());
            busy_ = true; // 取到任务即置忙, 期间新 ask 直接回 busy
        }
        answer_question(question);
        {
            std::lock_guard<std::mutex> lk(task_mutex_);
            busy_ = false;
        }
    }
}

/**
 * @brief 向当前客户端发送一行 JSON (自动补 \n)
 * @param json 不含换行的 JSON 文本
 * @return 0 成功, -1 失败 (无连接或对端断开)
 */
int LlmAgent::send_line(const std::string &json)
{
    std::lock_guard<std::mutex> lk(client_mutex_);
    if (client_fd_ < 0)
        return -1;
    std::string s = json + "\n";
    size_t off = 0;
    while (off < s.size())
    {
        ssize_t n = send(client_fd_, s.data() + off, s.size() - off, MSG_NOSIGNAL);
        if (n <= 0)
            return -1;
        off += (size_t)n;
    }
    return 0;
}

/**
 * @brief 仅当活动客户端仍为 fd 时发送 (防旧 worker 串扰新连接)
 * @param fd answer_question 开始时快照的客户端 fd
 * @param json 不含换行的 JSON 文本
 * @return 0 成功, -1 连接已切换/断开 (调用方应中断本轮)
 */
int LlmAgent::send_line_fd(int fd, const std::string &json)
{
    std::lock_guard<std::mutex> lk(client_mutex_);
    if (client_fd_ != fd || client_fd_ < 0)
        return -1; // 客户端已断开或被新连接替换
    std::string s = json + "\n";
    size_t off = 0;
    while (off < s.size())
    {
        ssize_t n = send(client_fd_, s.data() + off, s.size() - off, MSG_NOSIGNAL);
        if (n <= 0)
            return -1;
        off += (size_t)n;
    }
    return 0;
}

/**
 * @brief 把最新检测结果按 ROI 过滤后拼成文本 (归一化坐标)
 * @return 检测结果描述文本; 无目标时返回"当前画面未检测到目标"
 *
 * ROI 过滤规则: 目标框中心点落在 ROI 内才保留 (仅影响 prompt 文本,
 * 不裁剪图片, 不影响推流画面)。
 */
std::string LlmAgent::build_detection_text()
{
    object_detect_result_list od;
    int fw, fh;
    {
        std::lock_guard<std::mutex> lk(det_mutex_);
        od = dets_;
        fw = det_frame_w_;
        fh = det_frame_h_;
    }
    llm_roi_t roi;
    {
        std::lock_guard<std::mutex> lk(roi_mutex_);
        roi = roi_;
    }
    if (fw <= 0 || fh <= 0)
        return "检测帧尺寸未知。";

    std::string txt;
    int kept = 0;
    char buf[160];
    for (int i = 0; i < od.count; i++)
    {
        const object_detect_result *det = &od.results[i];
        float cx = (det->box.left + det->box.right) / 2.0f / fw;
        float cy = (det->box.top + det->box.bottom) / 2.0f / fh;
        if (roi.enable)
        {
            // 中心点不在 ROI 内的目标不写进 prompt
            if (cx < roi.x1 || cx > roi.x2 || cy < roi.y1 || cy > roi.y2)
                continue;
        }
        snprintf(buf, sizeof(buf), "%s %.0f%% [%.2f,%.2f,%.2f,%.2f]",
                 coco_cls_to_name(det->cls_id), det->prop * 100,
                 det->box.left / (float)fw, det->box.top / (float)fh,
                 det->box.right / (float)fw, det->box.bottom / (float)fh);
        if (!txt.empty())
            txt += "; ";
        txt += buf;
        kept++;
    }

    char head[128];
    snprintf(head, sizeof(head), "YOLO检测结果(归一化坐标[x1,y1,x2,y2],共%d个目标%s): ",
             kept, roi.enable ? ",已按ROI区域过滤" : "");
    if (kept == 0)
    {
        // ROI 框内/全画面无检测目标: 引导模型直接看图回答, 不要因"无目标"就拒答
        return "YOLO未检测到目标(ROI区域内无目标)。"
               "请直接根据摄像头画面内容回答用户问题。";
    }
    return std::string(head) + txt;
}

/*-------------------------------------------
                SSE 回调
-------------------------------------------*/

/**
 * @brief curl 写回调上下文
 */
typedef struct
{
    LlmAgent *self;
    int       fd;        // 本轮绑定的客户端 fd (发送前校验防串扰)
    std::string sbuf;    // 行缓冲
    bool aborted;        // 客户端断开标志
} sse_ctx_t;

/**
 * @brief 处理一条完整 SSE data 行: 解析 delta.content 并转发
 * @param ctx 回调上下文
 * @param data_line "data:" 之后的载荷
 */
static void sse_handle_data(sse_ctx_t *ctx, const std::string &data_line)
{
    std::string d = str_trim(data_line);
    if (d.empty())
        return;
    if (d == "[DONE]")
        return; // OpenAI 标准结束标记
    cJSON *root = cJSON_Parse(d.c_str());
    if (!root)
        return; // 心跳/非 JSON 行忽略
    cJSON *choices = cJSON_GetObjectItemCaseSensitive(root, "choices");
    if (cJSON_IsArray(choices) && cJSON_GetArraySize(choices) > 0)
    {
        cJSON *c0 = cJSON_GetArrayItem(choices, 0);
        cJSON *delta = cJSON_GetObjectItemCaseSensitive(c0, "delta");
        cJSON *content = delta ? cJSON_GetObjectItemCaseSensitive(delta, "content") : NULL;
        if (cJSON_IsString(content) && content->valuestring && content->valuestring[0])
        {
            // 组 {"t":"token","text":"..."} 转发 (用 cJSON 转义特殊字符)
            cJSON *o = cJSON_CreateObject();
            cJSON_AddStringToObject(o, "t", "token");
            cJSON_AddStringToObject(o, "text", content->valuestring);
            char *s = cJSON_PrintUnformatted(o);
            cJSON_Delete(o);
            if (s)
            {
                // 发送失败 = 客户端已断开/被新连接替换 → 置位中断标志, curl 返回 0 终止请求
                if (ctx->self->send_line_fd(ctx->fd, s) != 0)
                    ctx->aborted = true;
                free(s);
            }
        }
    }
    cJSON_Delete(root);
}

/**
 * @brief curl WRITEFUNCTION: 按行切分 SSE 流
 * @return 返回 size 表示继续; 返回 0 会中断 curl 请求 (客户端断开时)
 */
static size_t curl_write_cb(char *ptr, size_t size, size_t nmemb, void *userdata)
{
    sse_ctx_t *ctx = (sse_ctx_t *)userdata;
    size_t total = size * nmemb;
    if (ctx->aborted)
        return 0;
    ctx->sbuf.append(ptr, total);
    size_t pos;
    while ((pos = ctx->sbuf.find('\n')) != std::string::npos)
    {
        std::string line = ctx->sbuf.substr(0, pos);
        ctx->sbuf.erase(0, pos + 1);
        // SSE 行: "data: {...}" / "event: xxx" / 空行
        if (line.compare(0, 5, "data:") == 0)
            sse_handle_data(ctx, line.substr(5));
    }
    if (ctx->sbuf.size() > 1u << 20)
        ctx->sbuf.clear(); // 防御: 无换行的异常流
    return total;
}

/*-------------------------------------------
                提问 → API 调用
-------------------------------------------*/

/**
 * @brief 执行一次完整问答: 快照→编码→组请求→流式调用→回传
 * @param question 用户问题文本
 */
void LlmAgent::answer_question(const std::string &question)
{
    // 绑定本轮客户端 fd: 之后所有回发都校验该 fd, 防止断开重连后 token 串扰新客户端
    int cfd;
    {
        std::lock_guard<std::mutex> lk(client_mutex_);
        cfd = client_fd_;
    }

    // ---------- 1. 帧快照 → JPEG → base64 ----------
    cv::Mat frame_copy;
    {
        std::lock_guard<std::mutex> lk(frame_mutex_);
        if (frame_.empty())
        {
            send_line_fd(cfd, "{\"t\":\"error\",\"msg\":\"no frame yet\"}");
            return;
        }
        frame_copy = frame_.clone();
    }

    tjhandle tj = tjInitCompress();
    if (!tj)
    {
        send_line_fd(cfd, "{\"t\":\"error\",\"msg\":\"tjInitCompress fail\"}");
        return;
    }
    unsigned char *jpeg_buf = NULL;
    unsigned long jpeg_size = 0;
    // TJPF_RGB 与 V4L2 直出格式对齐; 预分配 W*H*2 兜底
    unsigned long out_size = (unsigned long)frame_copy.cols * frame_copy.rows * 2;
    jpeg_buf = (unsigned char *)malloc(out_size);
    int r = tjCompress2(tj, frame_copy.data, frame_copy.cols, frame_copy.step[0],
                        frame_copy.rows, TJPF_RGB, &jpeg_buf, &jpeg_size,
                        TJSAMP_422, cfg_.jpeg_quality, TJFLAG_NOREALLOC);
    tjDestroy(tj);
    if (r != 0)
    {
        free(jpeg_buf);
        send_line_fd(cfd, "{\"t\":\"error\",\"msg\":\"jpeg encode fail\"}");
        return;
    }
    std::string b64 = base64_encode(jpeg_buf, (size_t)jpeg_size);
    free(jpeg_buf);

    // ---------- 2. 检测结果文本 (ROI 过滤) ----------
    std::string det_text = build_detection_text();

    // ---------- 3. 组 OpenAI 兼容请求 ----------
    cJSON *req = cJSON_CreateObject();
    cJSON_AddStringToObject(req, "model", cfg_.model.c_str());
    cJSON_AddBoolToObject(req, "stream", 1);
    cJSON_AddNumberToObject(req, "max_tokens", cfg_.max_new_tokens);
    // deepseek-flash 默认开思考, 会吃掉 max_tokens 导致回答截断 (实测),
    // 必须显式禁用 (curl 验证: thinking={"type":"disabled"} 后 content 完整)
    cJSON *thinking = cJSON_AddObjectToObject(req, "thinking");
    cJSON_AddStringToObject(thinking, "type", "disabled");

    cJSON *msgs = cJSON_AddArrayToObject(req, "messages");
    // system
    cJSON *sysm = cJSON_CreateObject();
    cJSON_AddStringToObject(sysm, "role", "system");
    cJSON_AddStringToObject(sysm, "content", cfg_.system_prompt.c_str());
    cJSON_AddItemToArray(msgs, sysm);
    // user: content 数组 = [text, image_url]
    cJSON *usr = cJSON_CreateObject();
    cJSON_AddStringToObject(usr, "role", "user");
    cJSON *content = cJSON_CreateArray();
    std::string usertext = det_text + "\n问题: " + question;
    cJSON *c_text = cJSON_CreateObject();
    cJSON_AddStringToObject(c_text, "type", "text");
    cJSON_AddStringToObject(c_text, "text", usertext.c_str());
    cJSON_AddItemToArray(content, c_text);
    cJSON *c_img = cJSON_CreateObject();
    cJSON_AddStringToObject(c_img, "type", "image_url");
    cJSON *imgurl = cJSON_AddObjectToObject(c_img, "image_url");
    std::string datauri = "data:image/jpeg;base64," + b64;
    cJSON_AddStringToObject(imgurl, "url", datauri.c_str());
    cJSON_AddItemToArray(content, c_img);
    cJSON_AddItemToObject(usr, "content", content);
    cJSON_AddItemToArray(msgs, usr);

    char *payload = cJSON_PrintUnformatted(req);
    cJSON_Delete(req);
    if (!payload)
    {
        send_line_fd(cfd, "{\"t\":\"error\",\"msg\":\"json build fail\"}");
        return;
    }

    // ---------- 4. libcurl HTTPS 流式调用 ----------
    sse_ctx_t ctx;
    ctx.self = this;
    ctx.fd = cfd;
    ctx.aborted = false;

    CURL *curl = curl_easy_init();
    if (!curl)
    {
        free(payload);
        send_line_fd(cfd, "{\"t\":\"error\",\"msg\":\"curl init fail\"}");
        return;
    }
    struct curl_slist *hdrs = NULL;
    char auth[256];
    snprintf(auth, sizeof(auth), "Authorization: Bearer %s", cfg_.api_key.c_str());
    hdrs = curl_slist_append(hdrs, auth);
    hdrs = curl_slist_append(hdrs, "Content-Type: application/json");

    curl_easy_setopt(curl, CURLOPT_URL, cfg_.base_url.c_str());
    curl_easy_setopt(curl, CURLOPT_HTTPHEADER, hdrs);
    curl_easy_setopt(curl, CURLOPT_POST, 1L);
    curl_easy_setopt(curl, CURLOPT_POSTFIELDS, payload);
    curl_easy_setopt(curl, CURLOPT_WRITEFUNCTION, curl_write_cb);
    curl_easy_setopt(curl, CURLOPT_WRITEDATA, &ctx);
    curl_easy_setopt(curl, CURLOPT_TIMEOUT, (long)cfg_.timeout_s);
    curl_easy_setopt(curl, CURLOPT_CONNECTTIMEOUT, 10L);
    curl_easy_setopt(curl, CURLOPT_NOSIGNAL, 1L); // 多线程安全
    curl_easy_setopt(curl, CURLOPT_SSL_VERIFYPEER, 1L);
    curl_easy_setopt(curl, CURLOPT_SSL_VERIFYHOST, 2L);

    CURLcode cres = curl_easy_perform(curl);
    long http_code = 0;
    curl_easy_getinfo(curl, CURLINFO_RESPONSE_CODE, &http_code);

    curl_easy_cleanup(curl);
    curl_slist_free_all(hdrs);
    free(payload);

    // ---------- 5. 结束帧 ----------
    if (ctx.aborted)
    {
        printf("[llm] ask aborted (client gone)\n");
    }
    else if (cres == CURLE_OK && http_code == 200)
    {
        send_line_fd(cfd, "{\"t\":\"done\"}");
    }
    else
    {
        char err[256];
        snprintf(err, sizeof(err),
                 "{\"t\":\"error\",\"msg\":\"%s (http %ld)\"}",
                 cres != CURLE_OK ? curl_easy_strerror(cres) : "api http error",
                 http_code);
        send_line_fd(cfd, err);
        printf("[llm] ask fail: curl=%d http=%ld\n", (int)cres, http_code);
    }
}
