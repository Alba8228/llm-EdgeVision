namespace Remote_Video_Controller
{
    partial class Form1
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                StopGStreamerVideo();
                StopWebRtcVideo();
                StopMjpegVideo();
                StopRouteListener();
                StopCommunication();
                if (webView21 != null)
                {
                    webView21.Dispose();
                    webView21 = null;
                }
                if (components != null)
                {
                    components.Dispose();
                }
            }
            base.Dispose(disposing);
        } 

        #region Windows 窗体设计器生成的代码

        private void InitializeComponent()
        {
            this.btnESTOP = new System.Windows.Forms.Button();
            this.btnStart = new System.Windows.Forms.Button();
            this.lblKeyStatus = new System.Windows.Forms.Label();
            this.rbManual = new System.Windows.Forms.RadioButton();
            this.rbAuto = new System.Windows.Forms.RadioButton();
            this.rbRes = new System.Windows.Forms.RadioButton();
            this.lblSteering = new System.Windows.Forms.Label();
            this.lblGear = new System.Windows.Forms.Label();
            this.lblDrivingMode = new System.Windows.Forms.Label();
            this.txtRemoteIP = new System.Windows.Forms.TextBox();
            this.txtRemotePort = new System.Windows.Forms.TextBox();
            this.txtLocalPort = new System.Windows.Forms.TextBox();
            this.panel1 = new System.Windows.Forms.Panel();
            this.grpRoute = new System.Windows.Forms.GroupBox();
            this.txtRouteLocalPort = new System.Windows.Forms.TextBox();
            this.lblRouteLocalPort = new System.Windows.Forms.Label();
            this.lblRouteStatus = new System.Windows.Forms.Label();
            this.btnRouteEStop = new System.Windows.Forms.Button();
            this.btnSendRoute = new System.Windows.Forms.Button();
            this.numRoute = new System.Windows.Forms.NumericUpDown();
            this.lblRouteNum = new System.Windows.Forms.Label();
            this.txtRoutePort = new System.Windows.Forms.TextBox();
            this.lblRoutePort = new System.Windows.Forms.Label();
            this.txtRouteIP = new System.Windows.Forms.TextBox();
            this.lblRouteIP = new System.Windows.Forms.Label();
            this.grpFeedback = new System.Windows.Forms.GroupBox();
            this.btnClearFeedback = new System.Windows.Forms.Button();
            this.txtFeedbackHex = new System.Windows.Forms.TextBox();
            this.grpVehicleStatus = new System.Windows.Forms.GroupBox();
            this.lblVehChr = new System.Windows.Forms.Label();
            this.lblVehRtk = new System.Windows.Forms.Label();
            this.lblVehImu = new System.Windows.Forms.Label();
            this.lblVehSoc = new System.Windows.Forms.Label();
            this.lblVehParse = new System.Windows.Forms.Label();
            this.lblVehPosition = new System.Windows.Forms.Label();
            this.lblVehSpeed = new System.Windows.Forms.Label();
            this.lblVehRoute = new System.Windows.Forms.Label();
            this.grpNavMap = new System.Windows.Forms.GroupBox();
            this.navMapPanel = new Remote_Video_Controller.NavigationMapPanel();
            this.lblVideoSource = new System.Windows.Forms.Label();
            this.picMjpeg = new System.Windows.Forms.PictureBox();
            this.webView21 = new Microsoft.Web.WebView2.WinForms.WebView2();
            this.lblWebRtcUrl = new System.Windows.Forms.Label();
            this.txtWebRtcUrl = new System.Windows.Forms.TextBox();
            this.lblMjpegUrl = new System.Windows.Forms.Label();
            this.txtMjpegUrl = new System.Windows.Forms.TextBox();
            this.grpLlm = new System.Windows.Forms.GroupBox();
            this.lblLlmHost = new System.Windows.Forms.Label();
            this.txtLlmHost = new System.Windows.Forms.TextBox();
            this.lblLlmPort = new System.Windows.Forms.Label();
            this.txtLlmPort = new System.Windows.Forms.TextBox();
            this.txtLlmAnswer = new System.Windows.Forms.TextBox();
            this.txtLlmQuestion = new System.Windows.Forms.TextBox();
            this.btnAsk = new System.Windows.Forms.Button();
            this.btnRoiDraw = new System.Windows.Forms.Button();
            this.btnRoiClear = new System.Windows.Forms.Button();
            this.lblLlmStatus = new System.Windows.Forms.Label();
            this.grpLlm.SuspendLayout();
            this.grpRoute.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numRoute)).BeginInit();
            this.grpFeedback.SuspendLayout();
            this.grpVehicleStatus.SuspendLayout();
            this.grpNavMap.SuspendLayout();
            this.panel1.SuspendLayout();
            this.SuspendLayout();
            // 
            // btnESTOP
            // 
            this.btnESTOP.Location = new System.Drawing.Point(750, 812);
            this.btnESTOP.Name = "btnESTOP";
            this.btnESTOP.Size = new System.Drawing.Size(130, 40);
            this.btnESTOP.TabIndex = 1;
            this.btnESTOP.Text = "ESTOP";
            this.btnESTOP.UseVisualStyleBackColor = true;
            this.btnESTOP.Click += new System.EventHandler(this.btnESTOP_Click);
            // 
            // btnStart
            // 
            this.btnStart.Location = new System.Drawing.Point(506, 812);
            this.btnStart.Name = "btnStart";
            this.btnStart.Size = new System.Drawing.Size(130, 40);
            this.btnStart.TabIndex = 2;
            this.btnStart.Text = "Start";
            this.btnStart.UseVisualStyleBackColor = true;
            this.btnStart.Click += new System.EventHandler(this.btnStart_Click);
            // 
            // lblKeyStatus
            // 
            this.lblKeyStatus.AutoSize = true;
            this.lblKeyStatus.Location = new System.Drawing.Point(50, 800);
            this.lblKeyStatus.Name = "lblKeyStatus";
            this.lblKeyStatus.Size = new System.Drawing.Size(62, 18);
            this.lblKeyStatus.TabIndex = 3;
            this.lblKeyStatus.Text = "无按键";
            // 
            // rbManual
            // 
            this.rbManual.AutoSize = true;
            this.rbManual.Location = new System.Drawing.Point(351, 520);
            this.rbManual.Name = "rbManual";
            this.rbManual.Size = new System.Drawing.Size(87, 22);
            this.rbManual.TabIndex = 4;
            this.rbManual.TabStop = true;
            this.rbManual.Text = "Manual";
            this.rbManual.UseVisualStyleBackColor = true;
            this.rbManual.CheckedChanged += new System.EventHandler(this.rbManual_CheckedChanged);
            // 
            // rbAuto
            // 
            this.rbAuto.AutoSize = true;
            this.rbAuto.Location = new System.Drawing.Point(351, 559);
            this.rbAuto.Name = "rbAuto";
            this.rbAuto.Size = new System.Drawing.Size(123, 22);
            this.rbAuto.TabIndex = 5;
            this.rbAuto.TabStop = true;
            this.rbAuto.Text = "Autonomous";
            this.rbAuto.UseVisualStyleBackColor = true;
            this.rbAuto.CheckedChanged += new System.EventHandler(this.rbAuto_CheckedChanged);
            // 
            // rbRes
            // 
            this.rbRes.AutoSize = true;
            this.rbRes.Location = new System.Drawing.Point(351, 596);
            this.rbRes.Name = "rbRes";
            this.rbRes.Size = new System.Drawing.Size(60, 22);
            this.rbRes.TabIndex = 6;
            this.rbRes.TabStop = true;
            this.rbRes.Text = "Res";
            this.rbRes.UseVisualStyleBackColor = true;
            this.rbRes.CheckedChanged += new System.EventHandler(this.rbRes_CheckedChanged);
            // 
            // lblSteering
            // 
            this.lblSteering.AutoSize = true;
            this.lblSteering.Location = new System.Drawing.Point(53, 827);
            this.lblSteering.Name = "lblSteering";
            this.lblSteering.Size = new System.Drawing.Size(116, 18);
            this.lblSteering.TabIndex = 7;
            this.lblSteering.Text = "转向: 0.00°";
            // 
            // lblGear
            // 
            this.lblGear.AutoSize = true;
            this.lblGear.Location = new System.Drawing.Point(147, 827);
            this.lblGear.Name = "lblGear";
            this.lblGear.Size = new System.Drawing.Size(71, 18);
            this.lblGear.TabIndex = 8;
            this.lblGear.Text = "档位: 1";
            // 
            // lblDrivingMode
            // 
            this.lblDrivingMode.AutoSize = true;
            this.lblDrivingMode.Location = new System.Drawing.Point(231, 827);
            this.lblDrivingMode.Name = "lblDrivingMode";
            this.lblDrivingMode.Size = new System.Drawing.Size(71, 18);
            this.lblDrivingMode.TabIndex = 9;
            this.lblDrivingMode.Text = "模式: 0";
            // 
            // txtRemoteIP
            // 
            this.txtRemoteIP.Location = new System.Drawing.Point(57, 857);
            this.txtRemoteIP.Name = "txtRemoteIP";
            this.txtRemoteIP.Size = new System.Drawing.Size(237, 28);
            this.txtRemoteIP.TabIndex = 10;
            this.txtRemoteIP.Text = "10.168.1.200";
            // 
            // txtRemotePort
            // 
            this.txtRemotePort.Location = new System.Drawing.Point(57, 891);
            this.txtRemotePort.Name = "txtRemotePort";
            this.txtRemotePort.Size = new System.Drawing.Size(237, 28);
            this.txtRemotePort.TabIndex = 11;
            this.txtRemotePort.Text = "9999";
            // 
            // txtLocalPort
            // 
            this.txtLocalPort.Location = new System.Drawing.Point(56, 924);
            this.txtLocalPort.Name = "txtLocalPort";
            this.txtLocalPort.Size = new System.Drawing.Size(237, 28);
            this.txtLocalPort.TabIndex = 12;
            this.txtLocalPort.Text = "9999";
            // 
            // panel1
            // 
            this.panel1.BackColor = System.Drawing.Color.Black;
            this.panel1.Location = new System.Drawing.Point(506, 12);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(1040, 500);
            this.panel1.TabIndex = 13;
            // 
            // lblVideoSource
            // 
            this.lblVideoSource.AutoSize = true;
            this.lblVideoSource.ForeColor = System.Drawing.Color.White;
            this.lblVideoSource.Location = new System.Drawing.Point(8, 4);
            this.lblVideoSource.Name = "lblVideoSource";
            this.lblVideoSource.Text = "视频源: 未启动";
            // 
            // picMjpeg
            // 
            this.picMjpeg.Dock = System.Windows.Forms.DockStyle.Fill;
            this.picMjpeg.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.picMjpeg.Visible = false;
            // 
            // webView21
            // 
            this.webView21.Dock = System.Windows.Forms.DockStyle.Fill;
            this.webView21.Visible = false;
            // 
            // lblWebRtcUrl
            // 
            this.lblWebRtcUrl.AutoSize = true;
            this.lblWebRtcUrl.Location = new System.Drawing.Point(57, 962);
            this.lblWebRtcUrl.Name = "lblWebRtcUrl";
            this.lblWebRtcUrl.Text = "WebRTC页面:";
            // 
            // txtWebRtcUrl
            // 
            this.txtWebRtcUrl.Location = new System.Drawing.Point(170, 958);
            this.txtWebRtcUrl.Name = "txtWebRtcUrl";
            this.txtWebRtcUrl.Size = new System.Drawing.Size(340, 28);
            this.txtWebRtcUrl.Text = "http://10.168.1.144:8889/live/stream";
            // 
            // lblMjpegUrl
            // 
            this.lblMjpegUrl.AutoSize = true;
            this.lblMjpegUrl.Location = new System.Drawing.Point(57, 996);
            this.lblMjpegUrl.Name = "lblMjpegUrl";
            this.lblMjpegUrl.Text = "MJPEG地址:";
            // 
            // txtMjpegUrl
            // 
            this.txtMjpegUrl.Location = new System.Drawing.Point(170, 992);
            this.txtMjpegUrl.Name = "txtMjpegUrl";
            this.txtMjpegUrl.Size = new System.Drawing.Size(340, 28);
            // 
            // grpLlm
            // 
            this.grpLlm.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.grpLlm.Controls.Add(this.lblLlmHost);
            this.grpLlm.Controls.Add(this.txtLlmHost);
            this.grpLlm.Controls.Add(this.lblLlmPort);
            this.grpLlm.Controls.Add(this.txtLlmPort);
            this.grpLlm.Controls.Add(this.btnRoiDraw);
            this.grpLlm.Controls.Add(this.btnRoiClear);
            this.grpLlm.Controls.Add(this.lblLlmStatus);
            this.grpLlm.Controls.Add(this.txtLlmAnswer);
            this.grpLlm.Controls.Add(this.txtLlmQuestion);
            this.grpLlm.Controls.Add(this.btnAsk);
            this.grpLlm.Location = new System.Drawing.Point(506, 520);
            this.grpLlm.Name = "grpLlm";
            this.grpLlm.Size = new System.Drawing.Size(1040, 280);
            this.grpLlm.TabIndex = 18;
            this.grpLlm.TabStop = false;
            this.grpLlm.Text = "场景问答 (板端 LLM Agent)";
            // 
            // lblLlmHost
            // 
            this.lblLlmHost.AutoSize = true;
            this.lblLlmHost.Location = new System.Drawing.Point(15, 30);
            this.lblLlmHost.Name = "lblLlmHost";
            this.lblLlmHost.Text = "板端IP:";
            // 
            // txtLlmHost
            // 
            this.txtLlmHost.Location = new System.Drawing.Point(85, 27);
            this.txtLlmHost.Name = "txtLlmHost";
            this.txtLlmHost.Size = new System.Drawing.Size(140, 28);
            this.txtLlmHost.TabIndex = 0;
            this.txtLlmHost.Text = "10.168.1.108";
            // 
            // lblLlmPort
            // 
            this.lblLlmPort.AutoSize = true;
            this.lblLlmPort.Location = new System.Drawing.Point(240, 30);
            this.lblLlmPort.Name = "lblLlmPort";
            this.lblLlmPort.Text = "端口:";
            // 
            // txtLlmPort
            // 
            this.txtLlmPort.Location = new System.Drawing.Point(295, 27);
            this.txtLlmPort.Name = "txtLlmPort";
            this.txtLlmPort.Size = new System.Drawing.Size(70, 28);
            this.txtLlmPort.TabIndex = 1;
            this.txtLlmPort.Text = "12346";
            // 
            // btnRoiDraw
            // 
            this.btnRoiDraw.Location = new System.Drawing.Point(380, 25);
            this.btnRoiDraw.Name = "btnRoiDraw";
            this.btnRoiDraw.Size = new System.Drawing.Size(110, 32);
            this.btnRoiDraw.TabIndex = 2;
            this.btnRoiDraw.Text = "框选ROI";
            this.btnRoiDraw.UseVisualStyleBackColor = true;
            this.btnRoiDraw.Click += new System.EventHandler(this.btnRoiDraw_Click);
            // 
            // btnRoiClear
            // 
            this.btnRoiClear.Location = new System.Drawing.Point(500, 25);
            this.btnRoiClear.Name = "btnRoiClear";
            this.btnRoiClear.Size = new System.Drawing.Size(110, 32);
            this.btnRoiClear.TabIndex = 3;
            this.btnRoiClear.Text = "清除ROI";
            this.btnRoiClear.UseVisualStyleBackColor = true;
            this.btnRoiClear.Click += new System.EventHandler(this.btnRoiClear_Click);
            // 
            // lblLlmStatus
            // 
            this.lblLlmStatus.AutoSize = true;
            this.lblLlmStatus.ForeColor = System.Drawing.Color.Gray;
            this.lblLlmStatus.Location = new System.Drawing.Point(625, 30);
            this.lblLlmStatus.Name = "lblLlmStatus";
            this.lblLlmStatus.Text = "未连接";
            // 
            // txtLlmAnswer
            // 
            this.txtLlmAnswer.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtLlmAnswer.BackColor = System.Drawing.Color.White;
            this.txtLlmAnswer.Location = new System.Drawing.Point(15, 65);
            this.txtLlmAnswer.Multiline = true;
            this.txtLlmAnswer.Name = "txtLlmAnswer";
            this.txtLlmAnswer.ReadOnly = true;
            this.txtLlmAnswer.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtLlmAnswer.Size = new System.Drawing.Size(1010, 171);
            this.txtLlmAnswer.TabIndex = 4;
            // 
            // txtLlmQuestion
            // 
            this.txtLlmQuestion.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtLlmQuestion.Location = new System.Drawing.Point(15, 243);
            this.txtLlmQuestion.Name = "txtLlmQuestion";
            this.txtLlmQuestion.Size = new System.Drawing.Size(900, 28);
            this.txtLlmQuestion.TabIndex = 5;
            // 
            // btnAsk
            // 
            this.btnAsk.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnAsk.Location = new System.Drawing.Point(925, 150);
            this.btnAsk.Name = "btnAsk";
            this.btnAsk.Size = new System.Drawing.Size(100, 32);
            this.btnAsk.TabIndex = 6;
            this.btnAsk.Text = "提问";
            this.btnAsk.UseVisualStyleBackColor = true;
            this.btnAsk.Click += new System.EventHandler(this.btnAsk_Click);
            // 
            // grpRoute
            // 
            this.grpRoute.Controls.Add(this.txtRouteLocalPort);
            this.grpRoute.Controls.Add(this.lblRouteLocalPort);
            this.grpRoute.Controls.Add(this.lblRouteStatus);
            this.grpRoute.Controls.Add(this.btnRouteEStop);
            this.grpRoute.Controls.Add(this.btnSendRoute);
            this.grpRoute.Controls.Add(this.numRoute);
            this.grpRoute.Controls.Add(this.lblRouteNum);
            this.grpRoute.Controls.Add(this.txtRoutePort);
            this.grpRoute.Controls.Add(this.lblRoutePort);
            this.grpRoute.Controls.Add(this.txtRouteIP);
            this.grpRoute.Controls.Add(this.lblRouteIP);
            this.grpRoute.Location = new System.Drawing.Point(32, 520);
            this.grpRoute.Name = "grpRoute";
            this.grpRoute.Size = new System.Drawing.Size(297, 225);
            this.grpRoute.TabIndex = 14;
            this.grpRoute.TabStop = false;
            this.grpRoute.Text = "自动驾驶路线 (MCMD)";
            // 
            // txtRouteLocalPort
            // 
            this.txtRouteLocalPort.Location = new System.Drawing.Point(108, 152);
            this.txtRouteLocalPort.Name = "txtRouteLocalPort";
            this.txtRouteLocalPort.Size = new System.Drawing.Size(170, 28);
            this.txtRouteLocalPort.TabIndex = 9;
            this.txtRouteLocalPort.Text = "5001";
            this.txtRouteLocalPort.Leave += new System.EventHandler(this.txtRouteLocalPort_Leave);
            // 
            // lblRouteLocalPort
            // 
            this.lblRouteLocalPort.AutoSize = true;
            this.lblRouteLocalPort.Location = new System.Drawing.Point(15, 155);
            this.lblRouteLocalPort.Name = "lblRouteLocalPort";
            this.lblRouteLocalPort.Size = new System.Drawing.Size(89, 18);
            this.lblRouteLocalPort.TabIndex = 10;
            this.lblRouteLocalPort.Text = "本地端口:";
            // 
            // lblRouteStatus
            // 
            this.lblRouteStatus.AutoSize = true;
            this.lblRouteStatus.Location = new System.Drawing.Point(15, 192);
            this.lblRouteStatus.Name = "lblRouteStatus";
            this.lblRouteStatus.Size = new System.Drawing.Size(80, 18);
            this.lblRouteStatus.TabIndex = 8;
            this.lblRouteStatus.Text = "尚未发送";
            // 
            // btnRouteEStop
            // 
            this.btnRouteEStop.Location = new System.Drawing.Point(158, 118);
            this.btnRouteEStop.Name = "btnRouteEStop";
            this.btnRouteEStop.Size = new System.Drawing.Size(120, 36);
            this.btnRouteEStop.TabIndex = 7;
            this.btnRouteEStop.Text = "路线急停";
            this.btnRouteEStop.UseVisualStyleBackColor = true;
            this.btnRouteEStop.Click += new System.EventHandler(this.btnRouteEStop_Click);
            // 
            // btnSendRoute
            // 
            this.btnSendRoute.Location = new System.Drawing.Point(18, 118);
            this.btnSendRoute.Name = "btnSendRoute";
            this.btnSendRoute.Size = new System.Drawing.Size(120, 36);
            this.btnSendRoute.TabIndex = 6;
            this.btnSendRoute.Text = "发送路线";
            this.btnSendRoute.UseVisualStyleBackColor = true;
            this.btnSendRoute.Click += new System.EventHandler(this.btnSendRoute_Click);
            // 
            // numRoute
            // 
            this.numRoute.Location = new System.Drawing.Point(108, 82);
            this.numRoute.Maximum = new decimal(new int[] {
            10,
            0,
            0,
            0});
            this.numRoute.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.numRoute.Name = "numRoute";
            this.numRoute.Size = new System.Drawing.Size(80, 28);
            this.numRoute.TabIndex = 5;
            this.numRoute.Value = new decimal(new int[] {
            1,
            0,
            0,
            0});
            // 
            // lblRouteNum
            // 
            this.lblRouteNum.AutoSize = true;
            this.lblRouteNum.Location = new System.Drawing.Point(15, 84);
            this.lblRouteNum.Name = "lblRouteNum";
            this.lblRouteNum.Size = new System.Drawing.Size(89, 18);
            this.lblRouteNum.TabIndex = 4;
            this.lblRouteNum.Text = "路线编号:";
            // 
            // txtRoutePort
            // 
            this.txtRoutePort.Location = new System.Drawing.Point(108, 52);
            this.txtRoutePort.Name = "txtRoutePort";
            this.txtRoutePort.Size = new System.Drawing.Size(170, 28);
            this.txtRoutePort.TabIndex = 3;
            this.txtRoutePort.Text = "5001";
            // 
            // lblRoutePort
            // 
            this.lblRoutePort.AutoSize = true;
            this.lblRoutePort.Location = new System.Drawing.Point(15, 55);
            this.lblRoutePort.Name = "lblRoutePort";
            this.lblRoutePort.Size = new System.Drawing.Size(89, 18);
            this.lblRoutePort.TabIndex = 2;
            this.lblRoutePort.Text = "目标端口:";
            // 
            // txtRouteIP
            // 
            this.txtRouteIP.Location = new System.Drawing.Point(108, 22);
            this.txtRouteIP.Name = "txtRouteIP";
            this.txtRouteIP.Size = new System.Drawing.Size(170, 28);
            this.txtRouteIP.TabIndex = 1;
            this.txtRouteIP.Text = "10.168.1.108";
            // 
            // lblRouteIP
            // 
            this.lblRouteIP.AutoSize = true;
            this.lblRouteIP.Location = new System.Drawing.Point(15, 25);
            this.lblRouteIP.Name = "lblRouteIP";
            this.lblRouteIP.Size = new System.Drawing.Size(71, 18);
            this.lblRouteIP.TabIndex = 0;
            this.lblRouteIP.Text = "目标IP:";
            // 
            // grpFeedback
            // 
            this.grpFeedback.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.grpFeedback.Controls.Add(this.btnClearFeedback);
            this.grpFeedback.Controls.Add(this.txtFeedbackHex);
            this.grpFeedback.Location = new System.Drawing.Point(506, 862);
            this.grpFeedback.Name = "grpFeedback";
            this.grpFeedback.Size = new System.Drawing.Size(1040, 145);
            this.grpFeedback.TabIndex = 15;
            this.grpFeedback.TabStop = false;
            this.grpFeedback.Text = "UDP 反馈 (HEX)";
            // 
            // btnClearFeedback
            // 
            this.btnClearFeedback.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnClearFeedback.Location = new System.Drawing.Point(928, 22);
            this.btnClearFeedback.Name = "btnClearFeedback";
            this.btnClearFeedback.Size = new System.Drawing.Size(96, 30);
            this.btnClearFeedback.TabIndex = 1;
            this.btnClearFeedback.Text = "清空";
            this.btnClearFeedback.UseVisualStyleBackColor = true;
            this.btnClearFeedback.Click += new System.EventHandler(this.btnClearFeedback_Click);
            // 
            // txtFeedbackHex
            // 
            this.txtFeedbackHex.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtFeedbackHex.Font = new System.Drawing.Font("Consolas", 9F);
            this.txtFeedbackHex.Location = new System.Drawing.Point(15, 25);
            this.txtFeedbackHex.Multiline = true;
            this.txtFeedbackHex.Name = "txtFeedbackHex";
            this.txtFeedbackHex.ReadOnly = true;
            this.txtFeedbackHex.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtFeedbackHex.Size = new System.Drawing.Size(904, 105);
            this.txtFeedbackHex.TabIndex = 0;
            // 
            // grpVehicleStatus
            // 
            this.grpVehicleStatus.Controls.Add(this.lblVehChr);
            this.grpVehicleStatus.Controls.Add(this.lblVehRtk);
            this.grpVehicleStatus.Controls.Add(this.lblVehImu);
            this.grpVehicleStatus.Controls.Add(this.lblVehSoc);
            this.grpVehicleStatus.Controls.Add(this.lblVehParse);
            this.grpVehicleStatus.Controls.Add(this.lblVehPosition);
            this.grpVehicleStatus.Controls.Add(this.lblVehSpeed);
            this.grpVehicleStatus.Controls.Add(this.lblVehRoute);
            this.grpVehicleStatus.Location = new System.Drawing.Point(32, 285);
            this.grpVehicleStatus.Name = "grpVehicleStatus";
            this.grpVehicleStatus.Size = new System.Drawing.Size(420, 210);
            this.grpVehicleStatus.TabIndex = 16;
            this.grpVehicleStatus.TabStop = false;
            this.grpVehicleStatus.Text = "车端状态";
            // 
            // lblVehChr
            // 
            this.lblVehChr.AutoSize = true;
            this.lblVehChr.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F, System.Drawing.FontStyle.Bold);
            this.lblVehChr.Location = new System.Drawing.Point(10, 120);
            this.lblVehChr.Name = "lblVehChr";
            this.lblVehChr.Size = new System.Drawing.Size(110, 25);
            this.lblVehChr.TabIndex = 7;
            this.lblVehChr.Text = "充电对准: --";
            // 
            // lblVehRtk
            // 
            this.lblVehRtk.AutoSize = true;
            this.lblVehRtk.Location = new System.Drawing.Point(160, 92);
            this.lblVehRtk.Name = "lblVehRtk";
            this.lblVehRtk.Size = new System.Drawing.Size(71, 18);
            this.lblVehRtk.TabIndex = 6;
            this.lblVehRtk.Text = "RTK: --";
            // 
            // lblVehImu
            // 
            this.lblVehImu.AutoSize = true;
            this.lblVehImu.Location = new System.Drawing.Point(10, 92);
            this.lblVehImu.Name = "lblVehImu";
            this.lblVehImu.Size = new System.Drawing.Size(71, 18);
            this.lblVehImu.TabIndex = 5;
            this.lblVehImu.Text = "IMU: --";
            // 
            // lblVehSoc
            // 
            this.lblVehSoc.AutoSize = true;
            this.lblVehSoc.Location = new System.Drawing.Point(160, 64);
            this.lblVehSoc.Name = "lblVehSoc";
            this.lblVehSoc.Size = new System.Drawing.Size(71, 18);
            this.lblVehSoc.TabIndex = 4;
            this.lblVehSoc.Text = "SOC: --";
            // 
            // lblVehParse
            // 
            this.lblVehParse.AutoSize = true;
            this.lblVehParse.Location = new System.Drawing.Point(10, 178);
            this.lblVehParse.Name = "lblVehParse";
            this.lblVehParse.Size = new System.Drawing.Size(80, 18);
            this.lblVehParse.TabIndex = 3;
            this.lblVehParse.Text = "等待数据";
            // 
            // lblVehPosition
            // 
            this.lblVehPosition.AutoSize = true;
            this.lblVehPosition.Location = new System.Drawing.Point(10, 148);
            this.lblVehPosition.Name = "lblVehPosition";
            this.lblVehPosition.Size = new System.Drawing.Size(152, 18);
            this.lblVehPosition.TabIndex = 2;
            this.lblVehPosition.Text = "X:--  Y:--  H:--";
            // 
            // lblVehSpeed
            // 
            this.lblVehSpeed.AutoSize = true;
            this.lblVehSpeed.Location = new System.Drawing.Point(10, 64);
            this.lblVehSpeed.Name = "lblVehSpeed";
            this.lblVehSpeed.Size = new System.Drawing.Size(116, 18);
            this.lblVehSpeed.TabIndex = 1;
            this.lblVehSpeed.Text = "速度: -- m/s";
            // 
            // lblVehRoute
            // 
            this.lblVehRoute.AutoSize = true;
            this.lblVehRoute.Location = new System.Drawing.Point(10, 30);
            this.lblVehRoute.Name = "lblVehRoute";
            this.lblVehRoute.Size = new System.Drawing.Size(80, 18);
            this.lblVehRoute.TabIndex = 0;
            this.lblVehRoute.Text = "路线: --";
            // 
            // grpNavMap
            // 
            this.grpNavMap.Controls.Add(this.navMapPanel);
            this.grpNavMap.Location = new System.Drawing.Point(29, 12);
            this.grpNavMap.Name = "grpNavMap";
            this.grpNavMap.Size = new System.Drawing.Size(426, 267);
            this.grpNavMap.TabIndex = 17;
            this.grpNavMap.TabStop = false;
            this.grpNavMap.Text = "导航地图 (滚轮缩放 / 拖拽平移 / 右键复位)";
            // 
            // navMapPanel
            // 
            this.navMapPanel.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(24)))), ((int)(((byte)(28)))), ((int)(((byte)(32)))));
            this.navMapPanel.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.navMapPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.navMapPanel.Location = new System.Drawing.Point(3, 24);
            this.navMapPanel.Name = "navMapPanel";
            this.navMapPanel.Size = new System.Drawing.Size(420, 240);
            this.navMapPanel.TabIndex = 0;
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 18F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1558, 1040);
            this.Controls.Add(this.grpLlm);
            this.Controls.Add(this.txtMjpegUrl);
            this.Controls.Add(this.lblMjpegUrl);
            this.Controls.Add(this.txtWebRtcUrl);
            this.Controls.Add(this.lblWebRtcUrl);
            this.Controls.Add(this.grpNavMap);
            this.Controls.Add(this.grpVehicleStatus);
            this.Controls.Add(this.grpFeedback);
            this.Controls.Add(this.grpRoute);
            this.Controls.Add(this.panel1);
            this.Controls.Add(this.txtLocalPort);
            this.Controls.Add(this.txtRemotePort);
            this.Controls.Add(this.txtRemoteIP);
            this.Controls.Add(this.rbRes);
            this.Controls.Add(this.rbAuto);
            this.Controls.Add(this.rbManual);
            this.Controls.Add(this.lblDrivingMode);
            this.Controls.Add(this.lblGear);
            this.Controls.Add(this.lblSteering);
            this.Controls.Add(this.lblKeyStatus);
            this.Controls.Add(this.btnStart);
            this.Controls.Add(this.btnESTOP);
            this.panel1.Controls.Add(this.webView21);
            this.panel1.Controls.Add(this.picMjpeg);
            this.panel1.Controls.Add(this.lblVideoSource);
            this.KeyPreview = true;
            this.Name = "Form1";
            this.Text = "Remote Car Controller";
            this.KeyDown += new System.Windows.Forms.KeyEventHandler(this.Form1_KeyDown);
            this.KeyUp += new System.Windows.Forms.KeyEventHandler(this.Form1_KeyUp);
            this.grpLlm.ResumeLayout(false);
            this.grpLlm.PerformLayout();
            this.grpRoute.ResumeLayout(false);
            this.grpRoute.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numRoute)).EndInit();
            this.grpFeedback.ResumeLayout(false);
            this.grpFeedback.PerformLayout();
            this.grpVehicleStatus.ResumeLayout(false);
            this.grpVehicleStatus.PerformLayout();
            this.grpNavMap.ResumeLayout(false);
            this.panel1.ResumeLayout(false);
            this.panel1.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion
        private System.Windows.Forms.Button btnESTOP;
        private System.Windows.Forms.Button btnStart;
        private System.Windows.Forms.Label lblKeyStatus;
        private System.Windows.Forms.RadioButton rbManual;
        private System.Windows.Forms.RadioButton rbAuto;
        private System.Windows.Forms.RadioButton rbRes;
        private System.Windows.Forms.Label lblSteering;
        private System.Windows.Forms.Label lblGear;
        private System.Windows.Forms.Label lblDrivingMode;
        private System.Windows.Forms.TextBox txtRemoteIP;
        private System.Windows.Forms.TextBox txtRemotePort;
        private System.Windows.Forms.TextBox txtLocalPort;
        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.GroupBox grpRoute;
        private System.Windows.Forms.Label lblRouteIP;
        private System.Windows.Forms.TextBox txtRouteIP;
        private System.Windows.Forms.Label lblRoutePort;
        private System.Windows.Forms.TextBox txtRoutePort;
        private System.Windows.Forms.Label lblRouteNum;
        private System.Windows.Forms.NumericUpDown numRoute;
        private System.Windows.Forms.Button btnSendRoute;
        private System.Windows.Forms.Button btnRouteEStop;
        private System.Windows.Forms.Label lblRouteStatus;
        private System.Windows.Forms.Label lblRouteLocalPort;
        private System.Windows.Forms.TextBox txtRouteLocalPort;
        private System.Windows.Forms.GroupBox grpFeedback;
        private System.Windows.Forms.TextBox txtFeedbackHex;
        private System.Windows.Forms.Button btnClearFeedback;
        private System.Windows.Forms.GroupBox grpVehicleStatus;
        private System.Windows.Forms.Label lblVehRoute;
        private System.Windows.Forms.Label lblVehSpeed;
        private System.Windows.Forms.Label lblVehPosition;
        private System.Windows.Forms.Label lblVehParse;
        private System.Windows.Forms.Label lblVehSoc;
        private System.Windows.Forms.Label lblVehImu;
        private System.Windows.Forms.Label lblVehRtk;
        private System.Windows.Forms.Label lblVehChr;
        private System.Windows.Forms.GroupBox grpNavMap;
        private NavigationMapPanel navMapPanel;
        private System.Windows.Forms.Label lblVideoSource;
        private System.Windows.Forms.PictureBox picMjpeg;
        private Microsoft.Web.WebView2.WinForms.WebView2 webView21;
        private System.Windows.Forms.Label lblWebRtcUrl;
        private System.Windows.Forms.TextBox txtWebRtcUrl;
        private System.Windows.Forms.Label lblMjpegUrl;
        private System.Windows.Forms.TextBox txtMjpegUrl;
        private System.Windows.Forms.GroupBox grpLlm;
        private System.Windows.Forms.Label lblLlmHost;
        private System.Windows.Forms.TextBox txtLlmHost;
        private System.Windows.Forms.Label lblLlmPort;
        private System.Windows.Forms.TextBox txtLlmPort;
        private System.Windows.Forms.Button btnRoiDraw;
        private System.Windows.Forms.Button btnRoiClear;
        private System.Windows.Forms.Label lblLlmStatus;
        private System.Windows.Forms.TextBox txtLlmAnswer;
        private System.Windows.Forms.TextBox txtLlmQuestion;
        private System.Windows.Forms.Button btnAsk;
    }
}