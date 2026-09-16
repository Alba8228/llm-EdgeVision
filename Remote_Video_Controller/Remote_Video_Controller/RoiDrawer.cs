/*
================================================================================
【文件总说明】
文件：RoiDrawer.cs
功能：视频画面上叠加 ROI 框选交互（挂在 PictureBox 的 Paint/鼠标事件上）
说明：不采用透明覆盖控件（WinForms 普通 Panel 无法真透明遮挡视频），
      而是直接复用显示视频的 picMjpeg：Paint 时在其上绘制 ROI 矩形，
      鼠标事件同样由该 PictureBox 提供，坐标换算基于 Zoom 模式的实际图像矩形。

【整体工作流程】
1. Attach(picMjpeg)：订阅 Paint / MouseDown / MouseMove / MouseUp
2. 用户点「框选ROI」→ BeginDraw() 进入拖拽模式（光标十字）
3. 在画面上按下-拖动-松开：
   3.1 MouseMove 实时刷新橡皮筋矩形
   3.2 MouseUp 把像素矩形换算成归一化坐标（以 Zoom 实际图像区为基准，扣除黑边）
   3.3 触发 RoiCommitted(true, x1,y1,x2,y2)，由 Form1 发给板端
4. 右键：清除 ROI → RoiCommitted(false, ...)
5. SetRoi()/Clear()：从板端 state 应答恢复显示，不触发事件

关键技术点：
- 归一化坐标与板端检测帧对齐：推流帧=检测帧，Zoom 黑边必须扣除，否则 ROI 偏移
- 拖拽面积过小（<10x10 px）视为误触，自动取消
================================================================================
*/

using System;
using System.Drawing;
using System.Windows.Forms;

namespace Remote_Video_Controller
{
    /// <summary>
    /// @brief ROI 框选绘制器：在视频 PictureBox 上绘制/拖拽 ROI 矩形
    /// </summary>
    public class RoiDrawer
    {
        /// <summary>ROI 提交事件：(enable, 归一化x1,y1,x2,y2)</summary>
        public event Action<bool, float, float, float, float> RoiCommitted;

        private readonly PictureBox pb;
        private bool drawMode;          // 是否处于"待框选"模式
        private bool dragging;          // 鼠标按下拖动中
        private Point dragStart, dragCur;

        /// <summary>当前 ROI（归一化），null=未启用</summary>
        public float[] Roi { get; private set; }

        public bool InDrawMode { get { return drawMode; } }

        /// @brief 附加到显示视频的 PictureBox
        public RoiDrawer(PictureBox pictureBox)
        {
            pb = pictureBox;
            pb.Paint += Pb_Paint;
            pb.MouseDown += Pb_MouseDown;
            pb.MouseMove += Pb_MouseMove;
            pb.MouseUp += Pb_MouseUp;
        }

        /// @brief 进入框选模式（光标变十字，等待用户拖拽）
        public void BeginDraw()
        {
            drawMode = true;
            dragging = false;
            pb.Cursor = Cursors.Cross;
        }

        /// @brief 退出框选模式（不改变已有 ROI）
        public void CancelDraw()
        {
            drawMode = false;
            dragging = false;
            pb.Cursor = Cursors.Default;
            pb.Invalidate();
        }

        /// <summary>
        /// @brief 外部同步 ROI 显示（板端 state 应答恢复用），不触发事件
        /// @param box 归一化 [x1,y1,x2,y2]；null=清除
        /// </summary>
        public void SetRoi(float[] box)
        {
            Roi = (box != null && box.Length == 4) ? box : null;
            CancelDraw();
        }

        /// <summary>
        /// @brief 把 Zoom 模式的实际图像显示矩形算出来（扣除黑边）
        /// @return 图像在 PictureBox 客户区内的像素矩形；无图像时返回客户区
        /// </summary>
        private Rectangle GetImageRect()
        {
            Image img = pb.Image;
            Rectangle cr = pb.ClientRectangle;
            if (img == null || cr.Width <= 0 || cr.Height <= 0)
                return cr;

            double scale = Math.Min((double)cr.Width / img.Width, (double)cr.Height / img.Height);
            int w = (int)Math.Round(img.Width * scale);
            int h = (int)Math.Round(img.Height * scale);
            return new Rectangle((cr.Width - w) / 2, (cr.Height - h) / 2, w, h);
        }

        /// @brief 像素点 → 归一化坐标（以图像显示区为 0~1，限幅）
        private PointF ToNorm(Point p)
        {
            Rectangle r = GetImageRect();
            float nx = (p.X - r.X) / (float)r.Width;
            float ny = (p.Y - r.Y) / (float)r.Height;
            return new PointF(Math.Max(0f, Math.Min(1f, nx)), Math.Max(0f, Math.Min(1f, ny)));
        }

        // ---------- 绘制 ----------
        private void Pb_Paint(object sender, PaintEventArgs e)
        {
            Rectangle rectPx = RoiRectToPixel();
            if (dragging)
                rectPx = Rectangle.FromLTRB(
                    Math.Min(dragStart.X, dragCur.X), Math.Min(dragStart.Y, dragCur.Y),
                    Math.Max(dragStart.X, dragCur.X), Math.Max(dragStart.Y, dragCur.Y));

            if (rectPx.Width <= 1 || rectPx.Height <= 1)
                return;

            // 半透明黄色填充 + 黄色边框
            using (var fill = new SolidBrush(Color.FromArgb(40, 255, 220, 0)))
                e.Graphics.FillRectangle(fill, rectPx);
            using (var pen = new Pen(Color.FromArgb(255, 220, 0), 2))
                e.Graphics.DrawRectangle(pen, rectPx);
        }

        /// @brief 当前 ROI(归一化) → 像素矩形；无 ROI 返回 Empty
        private Rectangle RoiRectToPixel()
        {
            if (Roi == null)
                return Rectangle.Empty;
            Rectangle r = GetImageRect();
            int x1 = r.X + (int)(Roi[0] * r.Width);
            int y1 = r.Y + (int)(Roi[1] * r.Height);
            int x2 = r.X + (int)(Roi[2] * r.Width);
            int y2 = r.Y + (int)(Roi[3] * r.Height);
            return Rectangle.FromLTRB(x1, y1, x2, y2);
        }

        // ---------- 鼠标交互 ----------
        private void Pb_MouseDown(object sender, MouseEventArgs e)
        {
            // 右键：清除 ROI
            if (e.Button == MouseButtons.Right)
            {
                if (Roi != null)
                {
                    Roi = null;
                    RoiCommitted?.Invoke(false, 0, 0, 0, 0);
                    pb.Invalidate();
                }
                CancelDraw();
                return;
            }

            if (!drawMode || e.Button != MouseButtons.Left)
                return;

            dragging = true;
            dragStart = dragCur = e.Location;
        }

        private void Pb_MouseMove(object sender, MouseEventArgs e)
        {
            if (!dragging)
                return;
            dragCur = e.Location;
            pb.Invalidate();
        }

        private void Pb_MouseUp(object sender, MouseEventArgs e)
        {
            if (!dragging)
                return;
            dragging = false;

            int w = Math.Abs(e.X - dragStart.X);
            int h = Math.Abs(e.Y - dragStart.Y);
            if (w < 10 || h < 10)
            {
                // 误触/过小框：取消
                CancelDraw();
                return;
            }

            PointF a = ToNorm(dragStart);
            PointF b = ToNorm(e.Location);
            float x1 = Math.Min(a.X, b.X), x2 = Math.Max(a.X, b.X);
            float y1 = Math.Min(a.Y, b.Y), y2 = Math.Max(a.Y, b.Y);
            Roi = new[] { x1, y1, x2, y2 };

            CancelDraw();
            RoiCommitted?.Invoke(true, x1, y1, x2, y2);
        }
    }
}
