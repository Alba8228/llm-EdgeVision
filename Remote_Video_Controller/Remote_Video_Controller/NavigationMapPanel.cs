using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Remote_Video_Controller
{
    internal sealed class NavigationMapPanel : Panel
    {
        private readonly List<PathPoint> pathPoints = new List<PathPoint>();
        private bool hasVehicle;
        private double vehicleX;
        private double vehicleY;
        private double vehicleH;

        private double viewCenterX;
        private double viewCenterY;
        private double metersPerPixel = 0.05;
        private bool followVehicle = true;
        private bool panning;
        private Point lastPanScreen;
        private string pathHint;

        private const double MinMetersPerPixel = 0.005;
        private const double MaxMetersPerPixel = 2.0;
        private const double DefaultWindowMeters = 20.0;

        public NavigationMapPanel()
        {
            DoubleBuffered = true;
            BackColor = Color.FromArgb(24, 28, 32);
            BorderStyle = BorderStyle.FixedSingle;
        }

        public void UpdateVehicle(double x, double y, double headingRad)
        {
            hasVehicle = true;
            vehicleX = x;
            vehicleY = y;
            vehicleH = headingRad;

            if (followVehicle)
            {
                viewCenterX = x;
                viewCenterY = y;
            }

            Invalidate();
        }

        public void UpdatePath(IReadOnlyList<PathPoint> points, string parseHint = null)
        {
            pathPoints.Clear();
            if (points != null && points.Count > 0)
                pathPoints.AddRange(points);

            pathHint = parseHint;
            Invalidate();
        }

        public void ResetView()
        {
            followVehicle = true;
            metersPerPixel = DefaultWindowMeters / Math.Max(Width, Height);
            if (hasVehicle)
            {
                viewCenterX = vehicleX;
                viewCenterY = vehicleY;
            }

            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);

            if (Width <= 0 || Height <= 0)
                return;

            double zoomFactor = e.Delta > 0 ? 0.85 : 1.0 / 0.85;
            double newMpp = metersPerPixel * zoomFactor;
            newMpp = Math.Max(MinMetersPerPixel, Math.Min(MaxMetersPerPixel, newMpp));

            PointF worldBefore = ScreenToWorld(e.Location);
            metersPerPixel = newMpp;
            PointF worldAfter = ScreenToWorld(e.Location);

            viewCenterX += worldBefore.X - worldAfter.X;
            viewCenterY += worldBefore.Y - worldAfter.Y;
            followVehicle = false;

            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Middle)
            {
                panning = true;
                lastPanScreen = e.Location;
                followVehicle = false;
                Capture = true;
            }
            else if (e.Button == MouseButtons.Right)
            {
                ResetView();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (!panning)
                return;

            double dxScreen = e.X - lastPanScreen.X;
            double dyScreen = e.Y - lastPanScreen.Y;
            viewCenterX -= dxScreen * metersPerPixel;
            viewCenterY += dyScreen * metersPerPixel;
            lastPanScreen = e.Location;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            if (panning && (e.Button == MouseButtons.Left || e.Button == MouseButtons.Middle))
            {
                panning = false;
                Capture = false;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            if (Width <= 1 || Height <= 1)
                return;

            DrawGrid(g);

            if (pathPoints.Count >= 2)
            {
                using (var pathPen = new Pen(Color.FromArgb(80, 200, 120), 2f))
                using (var pathBrush = new SolidBrush(Color.FromArgb(120, 220, 140)))
                {
                    PointF? prev = null;
                    foreach (PathPoint pt in pathPoints)
                    {
                        PointF screen = WorldToScreen(pt.X, pt.Y);
                        if (prev.HasValue)
                            g.DrawLine(pathPen, prev.Value, screen);

                        float r = 2.5f;
                        g.FillEllipse(pathBrush, screen.X - r, screen.Y - r, r * 2, r * 2);
                        prev = screen;
                    }
                }
            }
            else if (pathPoints.Count == 1)
            {
                PointF p = WorldToScreen(pathPoints[0].X, pathPoints[0].Y);
                using (var brush = new SolidBrush(Color.FromArgb(120, 220, 140)))
                {
                    g.FillEllipse(brush, p.X - 3, p.Y - 3, 6, 6);
                }
            }

            if (hasVehicle)
                DrawVehicle(g);

            DrawOverlay(g);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);

            if (followVehicle && hasVehicle && Width > 0 && Height > 0 && pathPoints.Count == 0)
                metersPerPixel = DefaultWindowMeters / Math.Max(Width, Height);
        }

        private void DrawGrid(Graphics g)
        {
            double gridSpacing = NiceGridSpacing(metersPerPixel * 80);
            double halfWidthM = Width * metersPerPixel * 0.5;
            double halfHeightM = Height * metersPerPixel * 0.5;

            double startX = Math.Floor((viewCenterX - halfWidthM) / gridSpacing) * gridSpacing;
            double endX = viewCenterX + halfWidthM;
            double startY = Math.Floor((viewCenterY - halfHeightM) / gridSpacing) * gridSpacing;
            double endY = viewCenterY + halfHeightM;

            using (var pen = new Pen(Color.FromArgb(40, 255, 255, 255), 1f))
            {
                for (double x = startX; x <= endX; x += gridSpacing)
                {
                    PointF top = WorldToScreen(x, viewCenterY - halfHeightM);
                    PointF bottom = WorldToScreen(x, viewCenterY + halfHeightM);
                    g.DrawLine(pen, top, bottom);
                }

                for (double y = startY; y <= endY; y += gridSpacing)
                {
                    PointF left = WorldToScreen(viewCenterX - halfWidthM, y);
                    PointF right = WorldToScreen(viewCenterX + halfWidthM, y);
                    g.DrawLine(pen, left, right);
                }
            }
        }

        private void DrawVehicle(Graphics g)
        {
            PointF center = WorldToScreen(vehicleX, vehicleY);
            float lengthPx = (float)Math.Max(14, 2.8 / metersPerPixel);
            float widthPx = lengthPx * 0.45f;

            var corners = new[]
            {
                new PointF( lengthPx * 0.5f,  0),
                new PointF(-lengthPx * 0.35f,  widthPx * 0.5f),
                new PointF(-lengthPx * 0.35f, -widthPx * 0.5f),
            };

            float cos = (float)Math.Cos(vehicleH);
            float sin = (float)Math.Sin(vehicleH);
            var screenCorners = new PointF[3];
            for (int i = 0; i < corners.Length; i++)
            {
                float lx = corners[i].X;
                float ly = corners[i].Y;
                screenCorners[i] = new PointF(
                    center.X + lx * cos - ly * sin,
                    center.Y - (lx * sin + ly * cos));
            }

            using (var fill = new SolidBrush(Color.FromArgb(255, 220, 80)))
            using (var outline = new Pen(Color.Black, 1.5f))
            {
                g.FillPolygon(fill, screenCorners);
                g.DrawPolygon(outline, screenCorners);
            }

            g.FillEllipse(Brushes.White, center.X - 2, center.Y - 2, 4, 4);
        }

        private void DrawOverlay(Graphics g)
        {
            string hint;
            if (hasVehicle)
                hint = $"滚轮缩放 | 拖拽平移 | 右键复位 | {metersPerPixel * 1000:F0} mm/px";
            else
                hint = "等待车辆位姿...";

            if (!string.IsNullOrEmpty(pathHint))
                hint += " | " + pathHint;
            else if (pathPoints.Count >= 2)
                hint += $" | 路径 {pathPoints.Count} 点";

            using (var font = new Font(Font.FontFamily, 8f))
            using (var brush = new SolidBrush(Color.FromArgb(180, 220, 220, 220)))
            {
                g.DrawString(hint, font, brush, 6, 4);
            }
        }

        private PointF WorldToScreen(double worldX, double worldY)
        {
            float sx = (float)((worldX - viewCenterX) / metersPerPixel + Width * 0.5);
            float sy = (float)(Height * 0.5 - (worldY - viewCenterY) / metersPerPixel);
            return new PointF(sx, sy);
        }

        private PointF ScreenToWorld(Point screen)
        {
            double wx = (screen.X - Width * 0.5) * metersPerPixel + viewCenterX;
            double wy = (Height * 0.5 - screen.Y) * metersPerPixel + viewCenterY;
            return new PointF((float)wx, (float)wy);
        }

        private static double NiceGridSpacing(double target)
        {
            double[] steps = { 0.5, 1, 2, 5, 10, 20, 50, 100 };
            foreach (double step in steps)
            {
                if (step >= target)
                    return step;
            }

            return 100;
        }
    }
}
