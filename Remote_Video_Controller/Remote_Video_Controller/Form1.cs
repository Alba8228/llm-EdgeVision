

using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace Remote_Video_Controller
{
    public partial class Form1 : Form
    {
        // ---------- UDP 通信 ----------
        private UdpClient udpClient;
        private System.Threading.Timer sendTimer;
        private IPEndPoint carEndPoint;
        private bool isRunning = false;

        // ---------- 键盘状态 ----------
        private bool keyW, keyA, keyS, keyD;

        // ---------- 控制参数 ----------
        private const float AccelValue = 0.25f;
        // steer 语义与 STM32/aruco 对齐：0=直行，正=右转，负=左转，满幅 ±500
        // （STM32: turn=steer/5，左轮=thr+turn 右轮=thr-turn；steer>0 左轮快即右转）
        private const float SteerAngle = 0.5f;
        // 不按任何按键时下发的空闲默认值（steer=0 直行；tilt/pan 车端无云台仅透传）
        private const float DefaultSteerHWA = 0.0f;
        private const float DefaultVrTilt = 5.0f;
        private const float DefaultVrPan = -2.0f;
        private byte gearForward = 1;
        private byte gearReverse = 2;
        private byte emergencyStop = 0;
        private byte drivingMode = 0;

        // ---------- GStreamer 视频 ----------
        private const string RtspUrl = "rtsp://admin:@10.168.1.144/stream1";
        private const string GstLaunchPath = @"C:\Program Files\gstreamer\1.0\mingw_x86_64\bin\gst-launch-1.0.exe";
        private const string GstBinPath = @"C:\Program Files\gstreamer\1.0\mingw_x86_64\bin";
        private Process gstProcess;
        private IntPtr videoWindowHandle = IntPtr.Zero;

        // ---------- 视频源回退链 (GStreamer → WebRTC → MJPEG) ----------
        private Thread mjpegThread;
        private volatile bool mjpegRunning;
        private volatile bool mjpegFirstFrameReceived;
        private volatile bool mjpegFramePending;
        private HttpWebRequest mjpegRequest;

        // ---------- LLM 场景问答 (TCP:12346) ----------
        private LlmClient llmClient;
        private RoiDrawer roiDrawer;
        private readonly StringBuilder tokenBuf = new StringBuilder(); // token 批量刷新缓冲
        private System.Windows.Forms.Timer tokenFlushTimer;            // 60ms 刷一次，防 UI 卡顿
        private bool answerInProgress = false;

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private const int GwlStyle = -16;
        private const int WsCaption = 0x00C00000;
        private const int WsThickFrame = 0x00040000;
        private const uint SwpShowWindow = 0x0040;
        private const uint SwpNoZOrder = 0x0004;

        public Form1()
        {
            InitializeComponent();
            this.KeyPreview = true;
            rbManual.Checked = true;
            panel1.Resize += Panel1_Resize;
            StartRouteListener();

            InitLlmUi();

            // WebView2 抢到焦点时立刻还回窗体，保证 W/A/S/D 键盘控制始终生效
            webView21.GotFocus += (s, e) => this.Focus();
        }

        /// <summary>
        /// @brief 初始化 LLM 问答相关控件：客户端、ROI 绘制器、token 批量刷新定时器
        /// </summary>
        private void InitLlmUi()
        {
            llmClient = new LlmClient();
            llmClient.ConnectedChanged += Llm_ConnectedChanged;
            llmClient.TokenReceived += Llm_TokenReceived;
            llmClient.AnswerDone += Llm_AnswerDone;
            llmClient.ErrorReceived += Llm_ErrorReceived;
            llmClient.StateReceived += Llm_StateReceived;

            // ROI 框选叠加在 MJPEG 画面上（GStreamer/WebRTC 是外部窗口，无法叠加）
            roiDrawer = new RoiDrawer(picMjpeg);
            roiDrawer.RoiCommitted += Roi_RoiCommitted;

            tokenFlushTimer = new System.Windows.Forms.Timer { Interval = 60 };
            tokenFlushTimer.Tick += (s, e) => FlushTokenBuf();

            // 提问框回车即发送
            txtLlmQuestion.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    DoAsk();
                }
            };
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            StopLlm();
            tokenFlushTimer?.Dispose();
            llmClient?.Dispose();
            StopRouteListener();
            base.OnFormClosed(e);
        }

        private void Panel1_Resize(object sender, EventArgs e)
        {
            ApplyVideoWindowBounds();
        }

        private static string BuildGStreamerPipeline(string rtspUrl)
        {
            // 与 PowerShell 测试成功的管线一致，仅将 autovideosink 窗口嵌入 panel1
            return string.Format(
                "rtspsrc location=\"{0}\" latency=0 protocols=udp ! queue ! rtph265depay ! h265parse ! d3d11h265dec ! queue ! d3d11download ! queue ! videoconvert ! autovideosink",
                rtspUrl);
        }

        private void StartGStreamerVideo()
        {
            StopGStreamerVideo();

            if (!System.IO.File.Exists(GstLaunchPath))
            {
                MessageBox.Show(
                    "未找到 GStreamer，请确认已安装到：\n" + GstLaunchPath,
                    "视频启动失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            if (!panel1.IsHandleCreated)
                panel1.CreateControl();

            string pipeline = BuildGStreamerPipeline(RtspUrl);
            var psi = new ProcessStartInfo
            {
                FileName = GstLaunchPath,
                Arguments = pipeline,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = GstBinPath,
            };

            string existingPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            psi.EnvironmentVariables["PATH"] = GstBinPath + ";" + existingPath;

            gstProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };
            gstProcess.Exited += GstProcess_Exited;
            gstProcess.ErrorDataReceived += GstProcess_ErrorDataReceived;
            gstProcess.Start();
            gstProcess.BeginErrorReadLine();

            _ = EmbedVideoWindowAsync();
        }

        private void GstProcess_ErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                Debug.WriteLine("[GStreamer] " + e.Data);
        }

        private void GstProcess_Exited(object sender, EventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => videoWindowHandle = IntPtr.Zero));
                return;
            }

            videoWindowHandle = IntPtr.Zero;
        }

        private async Task EmbedVideoWindowAsync()
        {
            int processId = gstProcess?.Id ?? 0;
            if (processId == 0)
                return;

            for (int i = 0; i < 40; i++)
            {
                await Task.Delay(250);
                if (!isRunning || gstProcess == null || gstProcess.HasExited)
                    return;

                IntPtr hwnd = FindVideoWindow(processId);
                if (hwnd == IntPtr.Zero)
                    continue;

                videoWindowHandle = hwnd;
                StripWindowChrome(hwnd);
                SetParent(hwnd, panel1.Handle);
                ApplyVideoWindowBounds();
                return;
            }
        }

        private IntPtr FindVideoWindow(int processId)
        {
            IntPtr found = IntPtr.Zero;

            EnumWindows((hWnd, lParam) =>
            {
                GetWindowThreadProcessId(hWnd, out uint pid);
                if (pid != (uint)processId || !IsWindowVisible(hWnd))
                    return true;

                if (hWnd == Handle)
                    return true;

                found = hWnd;
                return false;
            }, IntPtr.Zero);

            return found;
        }

        private static void StripWindowChrome(IntPtr hwnd)
        {
            int style = GetWindowLong(hwnd, GwlStyle);
            style &= ~(WsCaption | WsThickFrame);
            SetWindowLong(hwnd, GwlStyle, style);
        }

        private void ApplyVideoWindowBounds()
        {
            if (videoWindowHandle == IntPtr.Zero)
                return;

            SetWindowPos(
                videoWindowHandle,
                IntPtr.Zero,
                0,
                0,
                panel1.ClientSize.Width,
                panel1.ClientSize.Height,
                SwpNoZOrder | SwpShowWindow);
        }

        private void StopGStreamerVideo()
        {
            videoWindowHandle = IntPtr.Zero;

            if (gstProcess == null)
                return;

            try
            {
                gstProcess.Exited -= GstProcess_Exited;
                gstProcess.ErrorDataReceived -= GstProcess_ErrorDataReceived;

                if (!gstProcess.HasExited)
                    gstProcess.Kill();
            }
            catch
            {
                // 忽略退出时的竞态
            }
            finally
            {
                gstProcess.Dispose();
                gstProcess = null;
            }
        }

        private async void StartVideoStream()
        {
            // 视频源优先级: GStreamer(RTSP) → WebRTC(WebView2) → MJPEG(HTTP)
            if (await TryStartGStreamerVideoAsync())
            {
                SetVideoSourceStatus("GStreamer (RTSP)");
                return;
            }

            if (await TryStartWebRtcVideoAsync())
            {
                SetVideoSourceStatus("WebRTC (WebView2)");
                return;
            }

            if (await TryStartMjpegVideoAsync())
            {
                SetVideoSourceStatus("MJPEG (HTTP)");
                return;
            }

            SetVideoSourceStatus("全部视频源启动失败");
        }

        /// <summary>
        /// @brief 尝试用 GStreamer 播放 RTSP 并嵌入窗口（保留原 3 次重试逻辑）
        /// @return true=成功嵌入并已出画面；false=GStreamer 未安装或重试后仍失败
        /// </summary>
        private async Task<bool> TryStartGStreamerVideoAsync()
        {
            if (!System.IO.File.Exists(GstLaunchPath))
                return false;

            const int maxAttempts = 3;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                if (!isRunning)
                    return false;

                StartGStreamerVideo();
                bool embedded = await WaitForVideoEmbedAsync(3000);

                if (embedded && gstProcess != null && !gstProcess.HasExited)
                    return true;

                StopGStreamerVideo();
                if (attempt < maxAttempts)
                    await Task.Delay(800);
            }

            return false;
        }

        /// <summary>
        /// @brief 尝试用 WebView2(嵌入式浏览器) 打开 mediamtx 的 WebRTC 播放页面
        /// @return true=页面导航成功且页面内 video 已有数据(readyState>=2)
        /// </summary>
        private async Task<bool> TryStartWebRtcVideoAsync()
        {
            string url = txtWebRtcUrl.Text.Trim();
            if (string.IsNullOrWhiteSpace(url))
            {
                SetVideoSourceStatus("WebRTC 已跳过 (未配置地址)");
                return false;
            }

            StopGStreamerVideo();
            StopMjpegVideo();

            picMjpeg.Visible = false;
            webView21.Visible = true;

            try
            {
                if (webView21.CoreWebView2 == null)
                {
                    var env = await CoreWebView2Environment.CreateAsync(
                        userDataFolder: Path.Combine(Application.StartupPath, "WebView2Data"));
                    await webView21.EnsureCoreWebView2Async(env);
                }

                var navTcs = new TaskCompletionSource<bool>();
                EventHandler<CoreWebView2NavigationCompletedEventArgs> handler = null;
                handler = (s, e) =>
                {
                    bool ok = e.IsSuccess;
                    navTcs.TrySetResult(ok);
                    webView21.NavigationCompleted -= handler;
                };
                webView21.NavigationCompleted += handler;

                webView21.CoreWebView2.Navigate(url);

                Task<bool> navTask = navTcs.Task;
                Task timeout = Task.Delay(15000);
                bool navOk = await Task.WhenAny(navTask, timeout) == navTask && navTask.Result;
                if (!navOk)
                    return false;

                // 页面导航成功不代表流已建立，继续轮询 video 元素是否已有数据
                return await WaitWebRtcReadyAsync();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// @brief 轮询 WebView2 页面内 video 元素 readyState，确认 WebRTC 流已出画面
        /// @param timeoutMs 最长等待毫秒数(默认 20 秒)
        /// @return true=video 已有当前帧数据
        /// </summary>
        private async Task<bool> WaitWebRtcReadyAsync(int timeoutMs = 20000)
        {
            int waited = 0;
            while (waited < timeoutMs)
            {
                if (!isRunning)
                    return false;

                try
                {
                    if (webView21.CoreWebView2 == null)
                        return false;

                    string rs = await webView21.ExecuteScriptAsync(
                        "(document.querySelector('video')||{}).readyState ?? -1");
                    int state;
                    if (int.TryParse(rs.Trim().Trim('"'), out state) && state >= 2)
                        return true;
                }
                catch
                {
                    // 页面尚未就绪，继续等待
                }

                await Task.Delay(500);
                waited += 500;
            }

            return false;
        }

        /// <summary>
        /// @brief 尝试用 C# 直拉 HTTP MJPEG 流并解码显示（不依赖 GStreamer）
        /// @return true=成功收到并解码第一帧
        /// </summary>
        private async Task<bool> TryStartMjpegVideoAsync()
        {
            string url = txtMjpegUrl.Text.Trim();
            if (string.IsNullOrWhiteSpace(url))
            {
                SetVideoSourceStatus("MJPEG 已跳过 (未配置地址)");
                return false;
            }

            StopGStreamerVideo();
            StopWebRtcVideo();

            webView21.Visible = false;
            picMjpeg.Visible = true;

            mjpegFirstFrameReceived = false;
            mjpegRunning = true;
            mjpegThread = new Thread(() => MjpegReceiveLoop(url));
            mjpegThread.IsBackground = true;
            mjpegThread.Start();

            // 等待第一帧，最多 15 秒；线程提前退出视为失败
            int waited = 0;
            while (waited < 15000)
            {
                if (!isRunning)
                    return false;

                if (mjpegFirstFrameReceived)
                    return true;

                if (mjpegThread == null || !mjpegThread.IsAlive)
                    return false;

                await Task.Delay(200);
                waited += 200;
            }

            return false;
        }

        /// <summary>
        /// @brief MJPEG 拉流线程：解析 multipart/x-mixed-replace 边界，逐帧解码显示
        /// @param url MJPEG 流地址
        /// </summary>
        private void MjpegReceiveLoop(string url)
        {
            try
            {
                mjpegRequest = (HttpWebRequest)WebRequest.Create(url);
                mjpegRequest.Timeout = 8000;
                mjpegRequest.ReadWriteTimeout = 8000;

                using (var response = (HttpWebResponse)mjpegRequest.GetResponse())
                using (var stream = response.GetResponseStream())
                {
                    if (stream == null)
                        return;

                    string boundary = ExtractBoundary(response.ContentType);
                    if (string.IsNullOrEmpty(boundary))
                        return;

                    while (mjpegRunning && isRunning)
                    {
                        // 1. 逐行读到 boundary 分隔行(如 "--frame")
                        string line = ReadLine(stream);
                        if (line == null)
                            break;
                        if (!line.StartsWith(boundary, StringComparison.Ordinal))
                            continue;

                        // 2. 读取该帧的头部，解析 Content-Length
                        int length = -1;
                        while ((line = ReadLine(stream)) != null && line.Length > 0)
                        {
                            if (line.StartsWith("Content-Length", StringComparison.OrdinalIgnoreCase))
                            {
                                int idx = line.IndexOf(':');
                                int.TryParse(line.Substring(idx + 1).Trim(), out length);
                            }
                        }
                        if (line == null)
                            break;
                        if (length <= 0 || length > 20 * 1024 * 1024)
                            continue;

                        // 3. 读取整帧 JPEG 数据并解码
                        byte[] jpeg = new byte[length];
                        ReadFull(stream, jpeg, length);

                        using (var ms = new MemoryStream(jpeg))
                        {
                            Image img = Image.FromStream(ms);
                            ShowMjpegFrame(img);
                        }
                    }
                }
            }
            catch
            {
                // 连接失败/流中断：由上层判定回退或停止
            }
            finally
            {
                mjpegRequest = null;
            }
        }

        /// <summary>
        /// @brief 从响应 Content-Type 头中解析 MJPEG 的 boundary 分隔串
        /// @param contentType 如 "multipart/x-mixed-replace; boundary=--frame"
        /// @return 分隔串(带 "--" 前缀)，解析失败返回 null
        /// </summary>
        private static string ExtractBoundary(string contentType)
        {
            if (string.IsNullOrEmpty(contentType))
                return null;

            int idx = contentType.IndexOf("boundary=", StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                return null;

            string b = contentType.Substring(idx + "boundary=".Length).Trim().Trim('"');
            if (b.Length == 0)
                return null;

            // 流中实际分隔行为 "--" + boundary
            if (!b.StartsWith("--", StringComparison.Ordinal))
                b = "--" + b;

            return b;
        }

        /// <summary>
        /// @brief 从流中读取一行(以 \r\n 或 \n 结尾，不含换行符)
        /// @param stream 输入流
        /// @return 行内容；流结束返回 null
        /// </summary>
        private static string ReadLine(Stream stream)
        {
            var sb = new StringBuilder();
            int b;
            bool prevCr = false;
            while ((b = stream.ReadByte()) != -1)
            {
                if (prevCr && b == '\n')
                    return sb.ToString();
                if (b != '\r')
                {
                    sb.Append((char)b);
                    prevCr = false;
                }
                else
                {
                    prevCr = true;
                }
            }

            return sb.Length > 0 || prevCr ? sb.ToString() : null;
        }

        /// <summary>
        /// @brief 从流中读取恰好 count 字节到 buffer
        /// @param stream 输入流
        /// @param buffer 目标缓冲
        /// @param count 需要读取的字节数
        /// </summary>
        private static void ReadFull(Stream stream, byte[] buffer, int count)
        {
            int read = 0;
            while (read < count)
            {
                int n = stream.Read(buffer, read, count - read);
                if (n <= 0)
                    throw new EndOfStreamException();
                read += n;
            }
        }

        /// <summary>
        /// @brief 在 UI 线程显示 MJPEG 帧；帧率过高时丢弃积压帧防止界面卡顿
        /// @param img 新解码的帧(本方法负责释放)
        /// </summary>
        private void ShowMjpegFrame(Image img)
        {
            if (picMjpeg.IsDisposed || !isRunning)
            {
                img.Dispose();
                return;
            }

            if (picMjpeg.InvokeRequired)
            {
                if (mjpegFramePending)
                {
                    img.Dispose();
                    return;
                }

                mjpegFramePending = true;
                picMjpeg.BeginInvoke(new Action<Image>(i =>
                {
                    mjpegFramePending = false;
                    ShowMjpegFrame(i);
                }), img);
                return;
            }

            Image old = picMjpeg.Image;
            picMjpeg.Image = img;
            if (old != null)
                old.Dispose();

            mjpegFirstFrameReceived = true;
        }

        /// <summary>
        /// @brief 停止 WebRTC(WebView2) 视频源：断开页面并隐藏控件
        /// </summary>
        private void StopWebRtcVideo()
        {
            try
            {
                if (webView21 != null && !webView21.IsDisposed)
                {
                    if (webView21.CoreWebView2 != null)
                        webView21.CoreWebView2.Navigate("about:blank");
                    webView21.Visible = false;
                }
            }
            catch
            {
                // 忽略停止时的竞态
            }
        }

        /// <summary>
        /// @brief 停止 MJPEG 视频源：中止请求、回收线程与画面
        /// </summary>
        private void StopMjpegVideo()
        {
            mjpegRunning = false;

            try { mjpegRequest?.Abort(); } catch { }

            if (mjpegThread != null && mjpegThread.IsAlive)
                mjpegThread.Join(1500);

            mjpegThread = null;

            if (picMjpeg != null && !picMjpeg.IsDisposed)
            {
                picMjpeg.Visible = false;
                Image old = picMjpeg.Image;
                picMjpeg.Image = null;
                if (old != null)
                    old.Dispose();
            }
        }

        /// <summary>
        /// @brief 更新视频区左上角视频源状态显示
        /// @param text 状态文本(不含 "视频源: " 前缀)
        /// </summary>
        private void SetVideoSourceStatus(string text)
        {
            if (lblVideoSource == null || lblVideoSource.IsDisposed)
                return;

            lblVideoSource.Text = "视频源: " + text;
        }

        private async Task<bool> WaitForVideoEmbedAsync(int timeoutMs)
        {
            int waited = 0;
            while (waited < timeoutMs)
            {
                if (!isRunning)
                    return false;

                if (videoWindowHandle != IntPtr.Zero)
                    return true;

                if (gstProcess == null || gstProcess.HasExited)
                    return false;

                await Task.Delay(250);
                waited += 250;
            }

            return videoWindowHandle != IntPtr.Zero;
        }

        // ---------- 启动/停止 ----------
        private void btnStart_Click(object sender, EventArgs e)
        {
            if (!isRunning)
            {
                StartSystem();
                btnStart.Text = "停止";
            }
            else
            {
                StopSystem();
                btnStart.Text = "开始";
            }
        }

        private void StartSystem()
        {
            try
            {
                isRunning = true;
                StartCommunication();
                StartVideoStream();
                StartLlm();
            }
            catch (Exception ex)
            {
                isRunning = false;
                StopCommunication();
                StopGStreamerVideo();
                btnStart.Text = "开始";
                MessageBox.Show(
                    "启动失败: " + ex.Message + "\n\n若提示端口被占用，请先点「停止」，或检查是否有其他程序占用本地端口。",
                    "启动错误",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private void StopSystem()
        {
            isRunning = false;
            StopLlm();
            StopGStreamerVideo();
            StopWebRtcVideo();
            StopMjpegVideo();
            StopCommunication();
            SetVideoSourceStatus("已停止");
        }

        // ---------- UDP 通信 ----------
        private Thread receiveThread;

        // ---------- 路线 UDP 监听 ----------
        private UdpClient routeUdpClient;
        private Thread routeReceiveThread;
        private volatile bool routeListenerRunning;
        private int activeListenPort = -1;
        private bool sharedUdpMode;

        private static UdpClient CreateBoundUdpClient(int localPort)
        {
            var endpoint = new IPEndPoint(IPAddress.Any, localPort);
            var socket = new Socket(endpoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.Bind(endpoint);
            return new UdpClient { Client = socket };
        }

        private void StopCommunication()
        {
            sendTimer?.Dispose();
            sendTimer = null;

            if (sharedUdpMode)
            {
                udpClient = null;
                sharedUdpMode = false;
                return;
            }

            UdpClient clientToClose = udpClient;
            udpClient = null;

            if (clientToClose != null)
            {
                try { clientToClose.Close(); } catch { }
                try { clientToClose.Dispose(); } catch { }
            }

            if (receiveThread != null && receiveThread.IsAlive)
                receiveThread.Join(1000);

            receiveThread = null;
        }

        private void StartCommunication()
        {
            StopCommunication();

            string remoteIP = txtRemoteIP.Text.Trim();
            int remotePort = int.Parse(txtRemotePort.Text.Trim());
            int localPort = int.Parse(txtLocalPort.Text.Trim());
            int routeLocalPort = int.Parse(txtRouteLocalPort.Text.Trim());

            carEndPoint = new IPEndPoint(IPAddress.Parse(remoteIP), remotePort);

            if (localPort == routeLocalPort)
            {
                EnsureRouteListener();
                if (routeUdpClient == null)
                    throw new InvalidOperationException($"UDP 端口 {localPort} 监听未就绪");

                udpClient = routeUdpClient;
                sharedUdpMode = true;
            }
            else
            {
                sharedUdpMode = false;
                udpClient = CreateBoundUdpClient(localPort);

                receiveThread = new Thread(ReceiveFeedback);
                receiveThread.IsBackground = true;
                receiveThread.Start();
            }

            sendTimer = new System.Threading.Timer(SendControlPacket, null, 0, 50);
        }

        private void SendControlPacket(object state)
        {
            if (!isRunning) return;

            float HWA = DefaultSteerHWA;
            float accel = 0;
            float decel = 0;
            byte gear = gearForward;

            if (keyW && keyS)
            {
                accel = 0;
                decel = 0;
            }
            else if (keyW)
            {
                accel = AccelValue;
                decel = 0;
            }
            else if (keyS)
            {
                // 倒车：S 键发 gear=2 + 油门（STM32 motor_apply 的 GEAR_BACK 分支走
                // 左2/右2 反向通道）；原实现把 decel 放进 brake 字段，车只会刹停不会后退
                accel = AccelValue;
                decel = 0;
                gear = gearReverse;
            }

            if (keyA && keyD)
                HWA = 0;
            else if (keyA)
                HWA = -SteerAngle;   // 左转：steer 取负（STM32 steer<0 左轮慢即左转）
            else if (keyD)
                HWA = SteerAngle;    // 右转：steer 取正

            if (emergencyStop == 1)
            {
                accel = 0;
                decel = 0;
            }

            // 调试：键盘状态变化时记录一次（用于定位按键→包内容的链路）
            string keyState = $"W={(keyW?1:0)} A={(keyA?1:0)} S={(keyS?1:0)} D={(keyD?1:0)} HWA={HWA:F2} accel={accel:F2} decel={decel:F2}";
            if (keyState != lastKeyState)
            {
                lastKeyState = keyState;
                LogDebug($"[Send] {keyState} | emerg={emergencyStop} mode={drivingMode}");
            }

            byte[] packet = PackControlData(HWA, accel, decel, gear, drivingMode, emergencyStop, DefaultVrTilt, DefaultVrPan);
            try
            {
                udpClient?.Send(packet, packet.Length, carEndPoint);
            }
            catch { }
        }

        private void ReceiveFeedback()
        {
            while (isRunning && !sharedUdpMode)
            {
                try
                {
                    if (udpClient == null)
                        break;

                    IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = udpClient.Receive(ref remoteEP);
                    this.Invoke((MethodInvoker)delegate
                    {
                        HandleIncomingUdp("控制", remoteEP, data);
                    });
                }
                catch
                {
                    break;
                }
            }
        }

        private void ReceiveRouteFeedback()
        {
            while (routeListenerRunning)
            {
                try
                {
                    if (routeUdpClient == null)
                        break;

                    IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = routeUdpClient.Receive(ref remoteEP);
                    this.Invoke((MethodInvoker)delegate
                    {
                        HandleIncomingUdp("路线", remoteEP, data);
                    });
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (SocketException)
                {
                    break;
                }
            }

            if (InvokeRequired)
                BeginInvoke(new Action(UpdateListenStatusAfterStop));
            else
                UpdateListenStatusAfterStop();
        }

        private void UpdateListenStatusAfterStop()
        {
            if (!routeListenerRunning && routeUdpClient == null)
                lblRouteStatus.Text = "UDP 监听已停止";
        }

        private void HandleIncomingUdp(string channel, IPEndPoint remote, byte[] data)
        {
            AppendFeedbackLog(channel, remote, data);
            TryUpdateVehicleStatusUI(data);
            TryUpdateLegacyFeedbackUI(data);
        }

        private void AppendFeedbackLog(string channel, IPEndPoint remote, byte[] data)
        {
            if (txtFeedbackHex.IsDisposed)
                return;

            if (txtFeedbackHex.InvokeRequired)
            {
                txtFeedbackHex.BeginInvoke(new Action(() => AppendFeedbackLog(channel, remote, data)));
                return;
            }

            string line = string.Format(
                "[{0:HH:mm:ss.fff}] {1} <- {2}:{3}  {4} bytes  {5}",
                DateTime.Now,
                channel,
                remote.Address,
                remote.Port,
                data.Length,
                FormatPacketHex(data));

            txtFeedbackHex.AppendText(line + Environment.NewLine);
            txtFeedbackHex.SelectionStart = txtFeedbackHex.TextLength;
            txtFeedbackHex.ScrollToCaret();
        }

        private void btnClearFeedback_Click(object sender, EventArgs e)
        {
            txtFeedbackHex.Clear();
        }

        private void StartRouteListener()
        {
            StopRouteListener();

            try
            {
                int localPort = int.Parse(txtRouteLocalPort.Text.Trim());
                routeUdpClient = CreateBoundUdpClient(localPort);
                routeListenerRunning = true;
                activeListenPort = localPort;

                routeReceiveThread = new Thread(ReceiveRouteFeedback);
                routeReceiveThread.IsBackground = true;
                routeReceiveThread.Start();

                lblRouteStatus.Text = $"UDP 监听中 :{localPort}";
            }
            catch (Exception ex)
            {
                activeListenPort = -1;
                routeUdpClient = null;
                routeListenerRunning = false;
                lblRouteStatus.Text = "监听失败: " + ex.Message;
            }
        }

        private void StopRouteListener()
        {
            routeListenerRunning = false;

            UdpClient clientToClose = routeUdpClient;
            routeUdpClient = null;

            if (clientToClose != null)
            {
                try { clientToClose.Close(); } catch { }
                try { clientToClose.Dispose(); } catch { }
            }

            if (routeReceiveThread != null && routeReceiveThread.IsAlive)
                routeReceiveThread.Join(1000);

            routeReceiveThread = null;
            activeListenPort = -1;
        }

        private void EnsureRouteListener()
        {
            int port;
            if (!int.TryParse(txtRouteLocalPort.Text.Trim(), out port))
                return;

            if (routeUdpClient == null || !routeListenerRunning || activeListenPort != port)
                StartRouteListener();
        }

        private void txtRouteLocalPort_Leave(object sender, EventArgs e)
        {
            EnsureRouteListener();
        }

        private byte[] PackControlData(float ctrl_HWA, float ctrl_accel, float ctrl_decel,
                                       byte ctrl_gear, byte ctrl_mode, byte ctrl_estop,
                                       float vr_tilt, float vr_pan)
        {
            byte[] tx = new byte[16];
            tx[0] = 0xAA; tx[1] = 0xBB; tx[2] = 0xAA; tx[3] = 0xBB;

            short hwa_scaled = (short)(ctrl_HWA * 1000);
            tx[4] = (byte)(hwa_scaled & 0xFF);
            tx[5] = (byte)((hwa_scaled >> 8) & 0xFF);

            tx[6] = (byte)(ctrl_accel * 100);
            tx[7] = (byte)(ctrl_decel * 100);

            tx[8] = ctrl_gear;
            tx[9] = (byte)((ctrl_estop << 3) | ctrl_mode);

            short tilt_scaled = (short)(vr_tilt * 100);
            tx[10] = (byte)(tilt_scaled & 0xFF);
            tx[11] = (byte)((tilt_scaled >> 8) & 0xFF);

            short pan_scaled = (short)(vr_pan * 100);
            tx[12] = (byte)(pan_scaled & 0xFF);
            tx[13] = (byte)((pan_scaled >> 8) & 0xFF);

            ushort sum = 0;
            for (int i = 0; i < 14; i++)
                sum += tx[i];
            tx[14] = (byte)(sum & 0xFF);
            tx[15] = (byte)((sum >> 8) & 0xFF);

            return tx;
        }

        private void TryUpdateVehicleStatusUI(byte[] data)
        {
            if (!StatusPacketParser.IsStatPacket(data))
                return;

            VehicleStatusInfo status = StatusPacketParser.Parse(data);

            lblVehRoute.Text = $"路线: {status.RouteId}";
            lblVehSpeed.Text = $"速度: {status.SpeedMps:F2} m/s";

            byte soc = status.HasFreespaceStatus ? status.FreespaceSoc : status.Soc;
            lblVehSoc.Text = $"SOC: {soc}%";

            if (status.HasFreespaceStatus)
            {
                lblVehImu.Text = $"IMU: {StatusPacketParser.BoolStatusText(status.ImuOk)} (0x{status.VehicleStatusRaw:X2})";
                lblVehImu.ForeColor = status.ImuOk ? System.Drawing.Color.ForestGreen : System.Drawing.Color.OrangeRed;

                lblVehRtk.Text = $"RTK: {StatusPacketParser.BoolStatusText(status.RtkOk)}";
                lblVehRtk.ForeColor = status.RtkOk ? System.Drawing.Color.ForestGreen : System.Drawing.Color.OrangeRed;

                if (status.Charging)
                {
                    lblVehChr.Text = "充电对准: 已对准 (充电中)";
                    lblVehChr.ForeColor = System.Drawing.Color.ForestGreen;
                }
                else
                {
                    lblVehChr.Text = "充电对准: 未对准";
                    lblVehChr.ForeColor = System.Drawing.Color.Gray;
                }
            }
            else if (status.IsStatPacket)
            {
                lblVehImu.Text = "IMU: --";
                lblVehImu.ForeColor = System.Drawing.SystemColors.ControlText;
                lblVehRtk.Text = "RTK: --";
                lblVehRtk.ForeColor = System.Drawing.SystemColors.ControlText;
                lblVehChr.Text = "充电对准: --";
                lblVehChr.ForeColor = System.Drawing.SystemColors.ControlText;
            }

            if (status.HasPosition)
                lblVehPosition.Text = $"X:{status.PosX:F4}  Y:{status.PosY:F4}  H:{status.PosH:F4}";
            else if (status.IsStatPacket)
                lblVehPosition.Text = "X:--  Y:--  H:--";

            if (status.Valid)
            {
                lblVehParse.Text = StatusPacketParser.ModeCodeToText(status.ModeCode);
            }
            else
            {
                lblVehParse.Text = status.Error ?? "解析失败";
            }

            if (status.HasPath)
            {
                string pathHint = $"{status.PathPoints.Count} 点";
                if (!string.IsNullOrEmpty(status.PathParseError))
                    pathHint += " (" + status.PathParseError + ")";
                navMapPanel.UpdatePath(status.PathPoints, pathHint);
            }

            if (status.HasPosition)
                navMapPanel.UpdateVehicle(status.PosX, status.PosY, status.PosH);
        }

        private void TryUpdateLegacyFeedbackUI(byte[] data)
        {
            if (data.Length < 16) return;
            if (data[0] != 0xBB || data[1] != 0xAA || data[2] != 0xBB || data[3] != 0xAA)
                return;

            short steeringRaw = BitConverter.ToInt16(data, 4);
            float steering = steeringRaw / 1000f;
            lblSteering.Text = $"转向: {steering:F2}°";

            lblGear.Text = $"档位: {data[8]}";
            lblDrivingMode.Text = $"模式: {data[9]}";

            sbyte slopeRaw = (sbyte)data[10];
            float slope = slopeRaw / 2f;

            short yawRaw = BitConverter.ToInt16(data, 12);
            float yawrate = yawRaw / 1000f;
        }

        // ===== 临时调试：键盘事件日志（写到 exe 目录 debug_keyboard.log，定位完根因后清理）=====
        private static readonly object debugLogLock = new object();
        private static readonly string debugLogPath =
            System.IO.Path.Combine(System.Windows.Forms.Application.StartupPath, "debug_keyboard.log");
        private string lastKeyState = null;

        /// @brief 把一行调试信息追加到 debug_keyboard.log（线程安全，UI/Timer 线程都会调）
        /// @param msg 要记录的消息（已含时间戳前缀外的内容）
        private static void LogDebug(string msg)
        {
            try
            {
                lock (debugLogLock)
                {
                    System.IO.File.AppendAllText(debugLogPath,
                        DateTime.Now.ToString("HH:mm:ss.fff") + " " + msg + System.Environment.NewLine);
                }
            }
            catch { }
        }

        /// @brief 焦点在文本输入框时不响应 WASD 车控（否则打字会误发车）
        private bool IsTypingInTextBox()
        {
            return this.ActiveControl is TextBox;
        }

        private void Form1_KeyDown(object sender, KeyEventArgs e)
        {
            if (IsTypingInTextBox())
                return;

            LogDebug($"[KeyDown] keyCode={e.KeyCode} focused={this.ActiveControl?.Name ?? "(null)"}");
            switch (e.KeyCode)
            {
                case Keys.W: keyW = true; e.Handled = true; break;
                case Keys.A: keyA = true; e.Handled = true; break;
                case Keys.S: keyS = true; e.Handled = true; break;
                case Keys.D: keyD = true; e.Handled = true; break;
            }
            UpdateKeyDisplay();
        }

        private void Form1_KeyUp(object sender, KeyEventArgs e)
        {
            LogDebug($"[KeyUp] keyCode={e.KeyCode}");
            switch (e.KeyCode)
            {
                case Keys.W: keyW = false; e.Handled = true; break;
                case Keys.A: keyA = false; e.Handled = true; break;
                case Keys.S: keyS = false; e.Handled = true; break;
                case Keys.D: keyD = false; e.Handled = true; break;
            }
            UpdateKeyDisplay();
        }

        private void UpdateKeyDisplay()
        {
            string keys = "";
            if (keyW) keys += "W ";
            if (keyA) keys += "A ";
            if (keyS) keys += "S ";
            if (keyD) keys += "D ";
            lblKeyStatus.Text = string.IsNullOrEmpty(keys) ? "无按键" : keys.Trim();
        }

        private void rbManual_CheckedChanged(object sender, EventArgs e)
        {
            if (rbManual.Checked) drivingMode = 0;
        }

        private void rbAuto_CheckedChanged(object sender, EventArgs e)
        {
            if (rbAuto.Checked) drivingMode = 1;
        }

        private void rbRes_CheckedChanged(object sender, EventArgs e)
        {
            if (rbRes.Checked) drivingMode = 2;
        }

        // ---------- 路线 MCMD 协议 ----------
        private static byte[] PackRoutePacket(byte routeId)
        {
            byte[] tx = new byte[10];
            tx[0] = 0x4D; // M
            tx[1] = 0x43; // C
            tx[2] = 0x4D; // M
            tx[3] = 0x44; // D
            tx[4] = routeId;
            tx[5] = 0x00;
            tx[6] = 0x00;
            tx[7] = 0x00;

            int sum = 0;
            for (int i = 0; i < 8; i++)
                sum += tx[i];
            sum %= 65536;

            tx[8] = (byte)(sum & 0xFF);
            tx[9] = (byte)((sum >> 8) & 0xFF);
            return tx;
        }

        private static string FormatPacketHex(byte[] packet)
        {
            var sb = new StringBuilder(packet.Length * 3);
            foreach (byte b in packet)
                sb.Append(b.ToString("X2")).Append(' ');
            return sb.ToString().TrimEnd();
        }

        private void SendRouteCommand(byte routeId)
        {
            try
            {
                EnsureRouteListener();
                if (routeUdpClient == null)
                    throw new InvalidOperationException("路线 UDP 监听未就绪");

                string ip = txtRouteIP.Text.Trim();
                int port = int.Parse(txtRoutePort.Text.Trim());
                byte[] packet = PackRoutePacket(routeId);

                routeUdpClient.Send(packet, packet.Length, new IPEndPoint(IPAddress.Parse(ip), port));

                string action = routeId == 0 ? "路线急停" : $"路线 {routeId}";
                lblRouteStatus.Text = $"已发送 {action}，监听 :{txtRouteLocalPort.Text.Trim()}";
            }
            catch (Exception ex)
            {
                lblRouteStatus.Text = "发送失败: " + ex.Message;
                MessageBox.Show(ex.Message, "路线指令发送失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void btnSendRoute_Click(object sender, EventArgs e)
        {
            byte routeId = (byte)numRoute.Value;
            SendRouteCommand(routeId);
        }

        private void btnRouteEStop_Click(object sender, EventArgs e)
        {
            SendRouteCommand(0);
        }

        private void btnESTOP_Click(object sender, EventArgs e)
        {
            if (emergencyStop == 0)
            {
                emergencyStop = 1;
                btnESTOP.Text = "急停已激活";
                btnESTOP.BackColor = Color.Red;
            }
            else
            {
                emergencyStop = 0;
                btnESTOP.Text = "急停";
                btnESTOP.BackColor = SystemColors.Control;
            }
        }

        // ---------- LLM 场景问答 ----------

        /// @brief 启动 LLM 客户端并查询一次状态（恢复板端常驻 ROI 显示）
        private void StartLlm()
        {
            string host = txtLlmHost.Text.Trim();
            int port;
            if (string.IsNullOrEmpty(host) || !int.TryParse(txtLlmPort.Text.Trim(), out port))
            {
                lblLlmStatus.Text = "IP/端口配置错误";
                return;
            }
            llmClient.Connect(host, port);
        }

        private void StopLlm()
        {
            llmClient?.Stop();
        }

        private void Llm_ConnectedChanged(bool ok)
        {
            if (ok)
            {
                lblLlmStatus.Text = "已连接";
                lblLlmStatus.ForeColor = Color.ForestGreen;
                llmClient.QueryState(); // 连上即同步板端 ROI 状态
            }
            else
            {
                lblLlmStatus.Text = "连接断开，重连中…";
                lblLlmStatus.ForeColor = Color.OrangeRed;
                SetAskIdle();
            }
        }

        /// @brief token 到达：只进缓冲，由定时器批量刷 UI（高频时避免卡顿）
        private void Llm_TokenReceived(string text)
        {
            tokenBuf.Append(text);
            if (!tokenFlushTimer.Enabled)
                tokenFlushTimer.Start();
        }

        private void FlushTokenBuf()
        {
            if (tokenBuf.Length == 0)
            {
                tokenFlushTimer.Stop();
                return;
            }
            string s = tokenBuf.ToString();
            tokenBuf.Clear();
            txtLlmAnswer.AppendText(s);
        }

        private void Llm_AnswerDone()
        {
            FlushTokenBuf();
            SetAskIdle();
            txtLlmAnswer.AppendText(Environment.NewLine);
        }

        private void Llm_ErrorReceived(string msg)
        {
            FlushTokenBuf();
            SetAskIdle();
            txtLlmAnswer.AppendText("[错误] " + msg + Environment.NewLine);
        }

        /// @brief state 应答：恢复板端 ROI 显示 + 忙状态
        private void Llm_StateReceived(RoiState roi, bool busy)
        {
            if (roi.Enable)
                roiDrawer.SetRoi(new[] { roi.X1, roi.Y1, roi.X2, roi.Y2 });
            else
                roiDrawer.SetRoi(null);
            picMjpeg.Invalidate();

            if (busy && !answerInProgress)
            {
                answerInProgress = true;
                btnAsk.Enabled = false;
                lblLlmStatus.Text = "回答中…";
            }
        }

        private void SetAskIdle()
        {
            answerInProgress = false;
            btnAsk.Enabled = true;
            if (llmClient.IsConnected)
                lblLlmStatus.Text = "已连接";
        }

        private void btnAsk_Click(object sender, EventArgs e)
        {
            DoAsk();
        }

        /// <summary>
        /// @brief 发送提问：清空回答区、置忙、调 Ask()
        /// </summary>
        private void DoAsk()
        {
            string q = txtLlmQuestion.Text.Trim();
            if (q.Length == 0)
                return;
            if (!llmClient.IsConnected)
            {
                txtLlmAnswer.AppendText("[错误] 未连接板端，请先点「开始」" + Environment.NewLine);
                return;
            }
            if (answerInProgress)
                return;

            answerInProgress = true;
            btnAsk.Enabled = false;
            lblLlmStatus.Text = "回答中…";
            txtLlmAnswer.AppendText("问: " + q + Environment.NewLine + "答: ");
            llmClient.Ask(q);
        }

        private void btnRoiDraw_Click(object sender, EventArgs e)
        {
            if (!picMjpeg.Visible)
            {
                MessageBox.Show("ROI 框选仅在 MJPEG 画面模式下可用", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            // 再点一次 = 取消框选
            if (roiDrawer.InDrawMode)
                roiDrawer.CancelDraw();
            else
                roiDrawer.BeginDraw();
        }

        private void btnRoiClear_Click(object sender, EventArgs e)
        {
            roiDrawer.SetRoi(null);
            picMjpeg.Invalidate();
            if (llmClient.IsConnected)
                llmClient.SetRoi(false, 0, 0, 0, 0);
        }

        /// @brief 用户完成框选/清除：把归一化 ROI 下发板端
        private void Roi_RoiCommitted(bool enable, float x1, float y1, float x2, float y2)
        {
            picMjpeg.Invalidate();
            if (llmClient.IsConnected)
                llmClient.SetRoi(enable, x1, y1, x2, y2);
        }
    }
}
