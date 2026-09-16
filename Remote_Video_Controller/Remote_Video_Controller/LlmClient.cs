/*
================================================================================
【文件总说明】
文件：LlmClient.cs
功能：上位机 → 板端 LlmAgent (TCP:12346) 的 JSON 行协议客户端
协议：每行一个 JSON。
  发送: {"t":"ask","q":".."}  {"t":"roi","enable":1,"box":[x1,y1,x2,y2]}  {"t":"state"}
  接收: {"t":"token","text":".."}  {"t":"done"}  {"t":"error","msg":".."}  {"t":"state",...}

【整体工作流程】
1. Connect(): 建立 TCP 连接 + 启动接收线程，断线自动重连（1s 间隔）
2. Ask()/SetRoi()/QueryState(): 组 JSON 行写入网络流（加锁防并发写交错）
3. ReceiveLoop(): 按行读取 → ParseLine() 解析 t 字段 → 触发对应事件
4. 事件回调都通过 SynchronizationContext 投递回 UI 线程（构造时捕获）

关键技术点：
- 不引入第三方 JSON 库，用极简手写解析（协议字段固定、值均为简单类型）
- token 事件高频触发，UI 侧应做批量刷新（本类只负责透传）
- 发送失败/断线时事件线程仍存活，重连成功后 Connected 事件再次触发
================================================================================
*/

using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Remote_Video_Controller
{
    /// <summary>
    /// @brief ROI 状态（归一化 0~1），与板端 llm_roi_t 对应
    /// </summary>
    public struct RoiState
    {
        public bool Enable;
        public float X1, Y1, X2, Y2;
    }

    /// <summary>
    /// @brief 板端 LLM 问答服务 TCP 客户端（单连接、自动重连、事件在 UI 线程回调）
    /// </summary>
    public class LlmClient : IDisposable
    {
        // ---------- 对外事件（均已投递到 UI 线程） ----------
        public event Action<bool> ConnectedChanged;      // 连接状态变化
        public event Action<string> TokenReceived;       // 收到一个回答 token
        public event Action AnswerDone;                  // 本轮回答结束
        public event Action<string> ErrorReceived;       // 板端报错（含 busy）
        public event Action<RoiState, bool> StateReceived; // state 应答（roi, busy）

        private readonly SynchronizationContext uiCtx;
        private TcpClient tcp;
        private StreamWriter writer;
        private Thread recvThread;
        private volatile bool running;
        private volatile bool connected;
        private readonly object sendLock = new object();

        public bool IsConnected { get { return connected; } }

        /// @brief 构造时捕获当前（UI）线程的同步上下文，事件回调据此切回 UI 线程
        public LlmClient()
        {
            uiCtx = SynchronizationContext.Current;
        }

        /// <summary>
        /// @brief 启动连接与接收线程（失败不抛异常，后台持续重连）
        /// @param host 板端 IP
        /// @param port 板端 TCP 端口（默认 12346）
        /// </summary>
        public void Connect(string host, int port)
        {
            if (running)
                Stop();

            running = true;
            recvThread = new Thread(() => ReceiveLoop(host, port))
            {
                IsBackground = true
            };
            recvThread.Start();
        }

        /// <summary>
        /// @brief 停止客户端：断开 socket 打断阻塞读，等待线程退出
        /// </summary>
        public void Stop()
        {
            running = false;
            CloseSocket();
            if (recvThread != null && recvThread.IsAlive)
                recvThread.Join(1500);
            recvThread = null;
        }

        public void Dispose()
        {
            Stop();
        }

        // ---------- 发送指令 ----------

        /// @brief 发送提问 {"t":"ask","q":..}
        public void Ask(string question)
        {
            SendLine("{\"t\":\"ask\",\"q\":\"" + JsonEscape(question) + "\"}");
        }

        /// @brief 设置/清除常驻 ROI（归一化坐标），enable=false 时板端忽略 box
        public void SetRoi(bool enable, float x1, float y1, float x2, float y2)
        {
            if (!enable)
            {
                SendLine("{\"t\":\"roi\",\"enable\":0}");
                return;
            }
            SendLine(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{{\"t\":\"roi\",\"enable\":1,\"box\":[{0:F4},{1:F4},{2:F4},{3:F4}]}}",
                x1, y1, x2, y2));
        }

        /// @brief 查询板端状态 {"t":"state"}，应答走 StateReceived 事件
        public void QueryState()
        {
            SendLine("{\"t\":\"state\"}");
        }

        /// <summary>
        /// @brief 写一行 JSON（加锁串行化；未连接时静默丢弃，由重连恢复）
        /// </summary>
        private void SendLine(string json)
        {
            lock (sendLock)
            {
                if (!connected || writer == null)
                    return;
                try
                {
                    writer.WriteLine(json);
                    writer.Flush();
                }
                catch
                {
                    // 写失败=对端断开，接收线程会感知并进入重连
                }
            }
        }

        // ---------- 接收线程 ----------

        /// <summary>
        /// @brief 接收主循环：连接→逐行读取→解析；断开后 1s 重连
        /// </summary>
        private void ReceiveLoop(string host, int port)
        {
            while (running)
            {
                try
                {
                    tcp = new TcpClient();
                    tcp.NoDelay = true;
                    tcp.Connect(host, port);

                    var stream = tcp.GetStream();
                    writer = new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\n" };
                    SetConnected(true);

                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        string line;
                        while (running && (line = reader.ReadLine()) != null)
                            ParseLine(line);
                    }
                }
                catch
                {
                    // 连接失败/读中断：走下方清理+重连
                }
                finally
                {
                    CloseSocket();
                    SetConnected(false);
                }

                // 断线后 1s 重试（sleep 分片，便于 Stop() 快速退出）
                for (int i = 0; i < 10 && running; i++)
                    Thread.Sleep(100);
            }
        }

        private void CloseSocket()
        {
            lock (sendLock)
            {
                try { writer?.Dispose(); } catch { }
                writer = null;
                try { tcp?.Close(); } catch { }
                tcp = null;
            }
        }

        private void SetConnected(bool v)
        {
            if (connected == v)
                return;
            connected = v;
            Post(() => ConnectedChanged?.Invoke(v));
        }

        /// @brief 把回调投递到 UI 线程（无上下文时直接执行，避免丢事件）
        private void Post(Action act)
        {
            if (act == null)
                return;
            if (uiCtx != null)
                uiCtx.Post(_ => act(), null);
            else
                act();
        }

        // ---------- 极简 JSON 行解析 ----------

        /// <summary>
        /// @brief 解析板端一行 JSON，按 t 字段分发事件
        /// @param line 形如 {"t":"token","text":"你"} 的报文
        ///
        /// 协议字段固定且值类型简单，手写解析避免引入第三方库；
        /// 解析失败的行直接忽略（防御异常报文）。
        /// </summary>
        private void ParseLine(string line)
        {
            line = line.Trim();
            if (line.Length < 5 || line[0] != '{')
                return;

            string t = JsonGetString(line, "t");
            if (t == null)
                return;

            switch (t)
            {
                case "token":
                {
                    string text = JsonGetString(line, "text");
                    if (text != null)
                        Post(() => TokenReceived?.Invoke(text));
                    break;
                }
                case "done":
                    Post(() => AnswerDone?.Invoke());
                    break;
                case "error":
                {
                    string msg = JsonGetString(line, "msg") ?? "unknown";
                    Post(() => ErrorReceived?.Invoke(msg));
                    break;
                }
                case "state":
                {
                    var roi = new RoiState();
                    roi.Enable = JsonGetInt(line, "roi_enable") != 0;
                    float[] box = JsonGetNumberArray(line, "roi");
                    if (box != null && box.Length == 4)
                    {
                        roi.X1 = box[0]; roi.Y1 = box[1];
                        roi.X2 = box[2]; roi.Y2 = box[3];
                    }
                    bool busy = JsonGetInt(line, "busy") != 0;
                    Post(() => StateReceived?.Invoke(roi, busy));
                    break;
                }
            }
        }

        /// <summary>
        /// @brief 提取字符串字段值（处理 \" \\ \n \uXXXX 转义）
        /// @return 字段值；不存在或格式错误返回 null
        /// </summary>
        public static string JsonGetString(string json, string key)
        {
            string pat = "\"" + key + "\"";
            int i = json.IndexOf(pat, StringComparison.Ordinal);
            if (i < 0)
                return null;
            i = json.IndexOf(':', i + pat.Length);
            if (i < 0)
                return null;
            i++;
            while (i < json.Length && char.IsWhiteSpace(json[i]))
                i++;
            if (i >= json.Length || json[i] != '"')
                return null;
            i++;

            var sb = new StringBuilder();
            while (i < json.Length)
            {
                char c = json[i];
                if (c == '"')
                    return sb.ToString();
                if (c == '\\' && i + 1 < json.Length)
                {
                    char n = json[i + 1];
                    switch (n)
                    {
                        case '"': sb.Append('"'); i += 2; break;
                        case '\\': sb.Append('\\'); i += 2; break;
                        case '/': sb.Append('/'); i += 2; break;
                        case 'n': sb.Append('\n'); i += 2; break;
                        case 'r': sb.Append('\r'); i += 2; break;
                        case 't': sb.Append('\t'); i += 2; break;
                        case 'u':
                            if (i + 5 < json.Length &&
                                ushort.TryParse(json.Substring(i + 2, 4),
                                    System.Globalization.NumberStyles.HexNumber,
                                    System.Globalization.CultureInfo.InvariantCulture,
                                    out ushort code))
                            {
                                sb.Append((char)code);
                                i += 6;
                            }
                            else i += 2;
                            break;
                        default: sb.Append(n); i += 2; break;
                    }
                }
                else
                {
                    sb.Append(c);
                    i++;
                }
            }
            return null; // 字符串未闭合
        }

        /// <summary>
        /// @brief 提取整数字段值（true/false 也识别为 1/0）
        /// @return 数值；不存在返回 0
        /// </summary>
        private static int JsonGetInt(string json, string key)
        {
            string pat = "\"" + key + "\"";
            int i = json.IndexOf(pat, StringComparison.Ordinal);
            if (i < 0)
                return 0;
            i = json.IndexOf(':', i + pat.Length);
            if (i < 0)
                return 0;
            i++;
            while (i < json.Length && char.IsWhiteSpace(json[i]))
                i++;
            int start = i;
            while (i < json.Length && (char.IsDigit(json[i]) || json[i] == '-'))
                i++;
            if (i > start && int.TryParse(json.Substring(start, i - start), out int v))
                return v;
            if (i + 4 <= json.Length && json.Substring(i, 4) == "true")
                return 1;
            return 0;
        }

        /// <summary>
        /// @brief 提取数字数组字段值，如 "roi":[0.1,0.2,0.3,0.4]
        /// @return 浮点数组；不存在或格式错误返回 null
        /// </summary>
        private static float[] JsonGetNumberArray(string json, string key)
        {
            string pat = "\"" + key + "\"";
            int i = json.IndexOf(pat, StringComparison.Ordinal);
            if (i < 0)
                return null;
            i = json.IndexOf(':', i + pat.Length);
            if (i < 0)
                return null;
            int lb = json.IndexOf('[', i);
            int rb = json.IndexOf(']', lb);
            if (lb < 0 || rb < lb)
                return null;

            string inner = json.Substring(lb + 1, rb - lb - 1);
            string[] parts = inner.Split(',');
            var arr = new float[parts.Length];
            for (int p = 0; p < parts.Length; p++)
            {
                if (!float.TryParse(parts[p].Trim(),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out arr[p]))
                    return null;
            }
            return arr;
        }

        /// <summary>
        /// @brief 字符串转 JSON 值（控制字符与引号/反斜杠转义）
        /// </summary>
        private static string JsonEscape(string s)
        {
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else
                            sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
