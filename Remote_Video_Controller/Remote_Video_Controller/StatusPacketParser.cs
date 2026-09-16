using System;
using System.Collections.Generic;
using System.Text;

namespace Remote_Video_Controller
{
    internal sealed class VehicleStatusInfo
    {
        public bool Valid { get; set; }
        public bool IsStatPacket { get; set; }
        public string Error { get; set; }
        public byte RouteId { get; set; }
        public byte ModeCode { get; set; }
        public byte Soc { get; set; }
        public double SpeedMps { get; set; }
        public double ObstacleDistM { get; set; }
        public bool HasPosition { get; set; }
        public bool HasEkfPosition { get; set; }
        public double PosX { get; set; }
        public double PosY { get; set; }
        public double PosH { get; set; }
        public ushort Sequence { get; set; }
        public ushort DeclaredLength { get; set; }
        public bool HasPath { get; set; }
        public string PathParseError { get; set; }
        public List<PathPoint> PathPoints { get; set; } = new List<PathPoint>();
        public bool HasFreespaceStatus { get; set; }
        public bool ImuOk { get; set; }
        public bool RtkOk { get; set; }
        public bool Charging { get; set; }
        public byte FreespaceSoc { get; set; }
        public byte VehicleStatusRaw { get; set; }
    }

    internal static class StatusPacketParser
    {
        private static readonly byte[] StatMagic = { 0x53, 0x54, 0x41, 0x54 }; // STAT
        private const int HeaderSize = 18;

        public static bool IsStatPacket(byte[] data)
        {
            return data != null && data.Length >= 4 && MatchMagic(data, 0, StatMagic);
        }

        public static VehicleStatusInfo Parse(byte[] data)
        {
            var info = new VehicleStatusInfo();

            if (data == null || data.Length < 20)
            {
                info.Error = "数据太短";
                return info;
            }

            if (!IsStatPacket(data))
            {
                info.Error = "非 STAT 包";
                return info;
            }

            info.IsStatPacket = true;
            info.DeclaredLength = BitConverter.ToUInt16(data, 6);

            int packetLen = data.Length;
            if (info.DeclaredLength > 0 && info.DeclaredLength <= data.Length)
                packetLen = info.DeclaredLength;

            if (packetLen < 20)
            {
                info.Error = $"长度不足 ({packetLen}B)";
                return info;
            }

            if (!ValidateChecksum(data, packetLen))
            {
                info.Error = $"校验和错误 (len={packetLen})";
                ParseHeader(data, info);
                ParseSegments(data, packetLen, info);
                return info;
            }

            ParseHeader(data, info);
            ParseSegments(data, packetLen, info);
            info.Valid = true;
            return info;
        }

        private static void ParseHeader(byte[] data, VehicleStatusInfo info)
        {
            info.RouteId = data[10];
            info.ModeCode = data[11];
            info.Soc = data[12];
            info.Sequence = BitConverter.ToUInt16(data, 8);

            short speedCmPerSec = BitConverter.ToInt16(data, 13);
            info.SpeedMps = speedCmPerSec / 100.0;

            ushort distCm = BitConverter.ToUInt16(data, 15);
            info.ObstacleDistM = distCm / 100.0;
        }

        private static bool ValidateChecksum(byte[] data, int packetLen)
        {
            int sum = 0;
            for (int i = 0; i < packetLen - 2; i++)
                sum += data[i];

            sum %= 65536;
            ushort recv = BitConverter.ToUInt16(data, packetLen - 2);
            return sum == recv;
        }

        private static void ParseSegments(byte[] data, int packetLen, VehicleStatusInfo info)
        {
            int bodyEnd = packetLen - 2;
            int offset = HeaderSize;

            while (offset + 3 <= bodyEnd)
            {
                byte segType = data[offset];
                ushort segLen = BitConverter.ToUInt16(data, offset + 1);
                offset += 3;

                if (segLen == 0 || offset + segLen > bodyEnd)
                    break;

                if (segType == 0x01)
                    TryParsePathSegment(data, offset, segLen, info);
                else if (segType == 0x02)
                    TryParseFreespaceSegment(data, offset, segLen, info);

                offset += segLen;
            }
        }

        private static void TryParsePathSegment(byte[] data, int offset, int length, VehicleStatusInfo info)
        {
            List<PathPoint> points;
            string error;
            if (Path4Parser.TryParse(data, offset, length, out points, out error))
            {
                info.PathPoints = points;
                info.HasPath = points.Count > 0;
                info.PathParseError = error;
            }
            else
            {
                info.PathParseError = error;
            }

            if (!info.HasEkfPosition && info.HasPath)
            {
                PathPoint first = info.PathPoints[0];
                info.PosX = first.X;
                info.PosY = first.Y;
                info.PosH = first.H;
                info.HasPosition = true;
            }
        }

        private static void TryParseFreespaceSegment(byte[] data, int offset, int length, VehicleStatusInfo info)
        {
            int pos7Offset;
            if (!Pos7Parser.TryFindPos7InFreespace(data, offset, length, out pos7Offset))
                return;

            if (pos7Offset + Pos7Parser.EkfhOffset + 2 <= offset + length)
            {
                info.PosX = BitConverter.ToInt32(data, pos7Offset + Pos7Parser.EkfxOffset) / 100.0;
                info.PosY = BitConverter.ToInt32(data, pos7Offset + Pos7Parser.EkfyOffset) / 100.0;
                info.PosH = BitConverter.ToInt16(data, pos7Offset + Pos7Parser.EkfhOffset) / 10000.0;
                info.HasEkfPosition = true;
                info.HasPosition = true;
            }

            if (pos7Offset + Pos7Parser.SocOffset + 1 <= offset + length)
            {
                byte statusByte = data[pos7Offset + Pos7Parser.VehicleStatusOffset];
                info.VehicleStatusRaw = statusByte;

                bool imuOk;
                bool rtkOk;
                bool charging;
                Pos7Parser.DecodeVehicleStatusByte(statusByte, out imuOk, out rtkOk, out charging);
                info.ImuOk = imuOk;
                info.RtkOk = rtkOk;
                info.Charging = charging;
                info.FreespaceSoc = data[pos7Offset + Pos7Parser.SocOffset];
                info.HasFreespaceStatus = true;
            }
        }

        private static int IndexOfAscii(byte[] data, int offset, int length, string text)
        {
            byte[] pattern = Encoding.ASCII.GetBytes(text);
            int end = offset + length - pattern.Length;

            for (int i = offset; i <= end; i++)
            {
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (data[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                    return i;
            }

            return -1;
        }

        private static bool MatchMagic(byte[] data, int offset, byte[] magic)
        {
            if (data.Length < offset + magic.Length)
                return false;

            for (int i = 0; i < magic.Length; i++)
            {
                if (data[offset + i] != magic[i])
                    return false;
            }

            return true;
        }

        public static string BoolStatusText(bool ok, string okText = "正常", string failText = "异常")
        {
            return ok ? okText : failText;
        }

        public static string ModeCodeToText(byte modeCode)
        {
            switch (modeCode)
            {
                case 0: return "idle";
                case 1: return "running_forward";
                case 2: return "running_backward";
                case 3: return "stop_loc";
                case 4: return "wait_charging";
                case 255: return "unknown";
                default: return $"mode_{modeCode}";
            }
        }
    }
}
