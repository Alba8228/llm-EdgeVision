using System;
using System.Collections.Generic;

namespace Remote_Video_Controller
{
    internal struct PathPoint
    {
        public double X;
        public double Y;
        public double H;
    }

    internal static class Path4Parser
    {
        private const int BlockSize = 11;
        // 首点占字节 11～25（MATLAB 1-based），共 15 字节；此前为 10 字节头
        private const int HeaderBytes = 10;
        private const int FirstPointBytes = 15;
        private const int PathOverhead = HeaderBytes + FirstPointBytes + 2; // 25 + 2 checksum

        public static bool TryParse(byte[] data, int offset, int length, out List<PathPoint> points, out string error)
        {
            points = null;
            error = null;

            int pathStart = IndexOfPath4(data, offset, length);
            if (pathStart < 0)
            {
                error = "未找到 PATH4";
                return false;
            }

            int pathEnd = offset + length;
            int available = pathEnd - pathStart;
            if (available < PathOverhead)
            {
                error = "PATH4 数据太短";
                return false;
            }

            int payloadBytes = available - 2;
            int numDeltas = (payloadBytes - HeaderBytes - FirstPointBytes) / BlockSize;
            int remainder = (payloadBytes - HeaderBytes - FirstPointBytes) % BlockSize;
            if (numDeltas < 0)
            {
                error = "PATH4 路点数据不完整";
                return false;
            }

            if (remainder != 0)
            {
                error = $"PATH4 长度不对齐 ({available}B, 余 {remainder})";
                return false;
            }

            int pathLen = HeaderBytes + FirstPointBytes + numDeltas * BlockSize + 2;
            if (pathStart + pathLen > pathEnd)
            {
                error = $"PATH4 长度不足 (需要 {pathLen}B, 可用 {available}B)";
                return false;
            }

            var pathBytes = new byte[pathLen];
            Array.Copy(data, pathStart, pathBytes, 0, pathLen);

            bool checksumOk = ValidateChecksum(pathBytes);
            if (!checksumOk)
                error = "PATH4 校验和错误";

            int numPoints = 1 + numDeltas;
            points = new List<PathPoint>(numPoints);

            double x = ReadInt32(pathBytes, 10) * 0.001;
            double y = ReadInt32(pathBytes, 14) * 0.001;
            double h = ReadInt16(pathBytes, 18) * 0.0001;
            points.Add(new PathPoint { X = x, Y = y, H = h });

            for (int k = 2; k <= numPoints; k++)
            {
                int baseIndex = HeaderBytes + FirstPointBytes + (k - 2) * BlockSize;
                double dx = ReadInt16(pathBytes, baseIndex) * 0.001;
                double dy = ReadInt16(pathBytes, baseIndex + 2) * 0.001;
                double dh = ReadInt16(pathBytes, baseIndex + 4) * 0.0001;

                MotEst(points[k - 2].X, points[k - 2].Y, points[k - 2].H, dx, dy, dh, out x, out y, out h);
                points.Add(new PathPoint { X = x, Y = y, H = h });
            }

            if (points.Count == 0)
            {
                error = "未解析出路点";
                return false;
            }

            return true;
        }

        private static int IndexOfPath4(byte[] data, int offset, int length)
        {
            int end = offset + length - 5;
            for (int i = offset; i <= end; i++)
            {
                if (data[i] == (byte)'P' &&
                    data[i + 1] == (byte)'A' &&
                    data[i + 2] == (byte)'T' &&
                    data[i + 3] == (byte)'H' &&
                    data[i + 4] == (byte)'4')
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool ValidateChecksum(byte[] pathBytes)
        {
            int sum = 0;
            for (int i = 0; i < pathBytes.Length - 2; i++)
                sum += pathBytes[i];

            sum %= 65536;
            ushort recv = BitConverter.ToUInt16(pathBytes, pathBytes.Length - 2);
            return sum == recv;
        }

        private static void MotEst(double st1x, double st1y, double st1h, double dx, double dy, double dh,
            out double st2x, out double st2y, out double st2h)
        {
            double cosH = Math.Cos(st1h);
            double sinH = Math.Sin(st1h);
            double t1 = dx * cosH - dy * sinH;
            double t2 = dx * sinH + dy * cosH;
            st2x = st1x + t1;
            st2y = st1y + t2;
            st2h = st1h + dh;

            if (st2h > Math.PI)
                st2h -= 2 * Math.PI;
            else if (st2h < -Math.PI)
                st2h += 2 * Math.PI;
        }

        private static int ReadInt32(byte[] data, int index)
        {
            return BitConverter.ToInt32(data, index);
        }

        private static int ReadInt16(byte[] data, int index)
        {
            return BitConverter.ToInt16(data, index);
        }
    }
}
