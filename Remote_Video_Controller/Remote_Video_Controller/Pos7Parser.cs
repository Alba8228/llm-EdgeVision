using System;

namespace Remote_Video_Controller
{
    /// <summary>
    /// pos7 / uc1_reply 布局，对齐 pnc_base.m decode_freespace_data2（含 15～34 字节 GNSS 字段）。
    /// </summary>
    internal static class Pos7Parser
    {
        private const int HeaderLen = 8;
        // free_space_num = 179 → 359 个测距点
        private const int FreeSpacePointCount = 359;
        private const int BinLen = FreeSpacePointCount * 2 + HeaderLen;

        public const int MinReplyBytes = 54;

        public const int EkfxOffset = 34;
        public const int EkfyOffset = 38;
        public const int EkfhOffset = 42;
        public const int VehicleStatusOffset = 49;
        public const int SocOffset = 50;

        public static bool TryFindPos7InFreespace(byte[] data, int segmentOffset, int segmentLength, out int pos7Offset)
        {
            pos7Offset = -1;

            if (segmentLength < BinLen + MinReplyBytes)
                return false;

            if (segmentOffset + BinLen + 4 > data.Length)
                return false;

            int candidate = segmentOffset + BinLen;
            if (data[candidate] == (byte)'p' &&
                data[candidate + 1] == (byte)'o' &&
                data[candidate + 2] == (byte)'s' &&
                data[candidate + 3] == (byte)'7')
            {
                pos7Offset = candidate;
                return true;
            }

            return IndexOfPos7(data, segmentOffset, segmentLength, out pos7Offset);
        }

        private static bool IndexOfPos7(byte[] data, int offset, int length, out int pos7Offset)
        {
            pos7Offset = -1;
            int end = offset + length - 4;

            for (int i = offset; i <= end; i++)
            {
                if (data[i] == (byte)'p' &&
                    data[i + 1] == (byte)'o' &&
                    data[i + 2] == (byte)'s' &&
                    data[i + 3] == (byte)'7')
                {
                    pos7Offset = i;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 对齐 MATLAB update_vehicle_status：vehicle_status(k) 对应 int2bit 的第 k 位（k=1 为 LSB）。
        /// </summary>
        public static void DecodeVehicleStatusByte(byte statusByte, out bool imuOk, out bool rtkOk, out bool charging)
        {
            bool Bit(int matlabIndexOneBased)
            {
                return ((statusByte >> (matlabIndexOneBased - 1)) & 1) != 0;
            }

            imuOk = Bit(8);
            charging = Bit(3);
            int rtkStatus = (Bit(6) ? 1 : 0) + (Bit(7) ? 2 : 0);
            rtkOk = rtkStatus > 1;
        }
    }
}
