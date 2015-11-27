using System;
using System.Text;
using Microsoft.SPOT;

namespace PervasiveDigital.Utilities
{
    public static class ArrayUtilities
    {
        public static UInt32[] StringToUIntArray(string s)
        {
            var utfBytes = Encoding.UTF8.GetBytes(s);
            return BytesToUIntArray(utfBytes);
        }

        public static UInt32[] BytesToUIntArray(byte[] sourceBytes)
        {
            var resultLen = (sourceBytes.Length + 3) / 4;
            var bytes = new byte[resultLen * 4];
            Array.Clear(bytes, 0, bytes.Length);
            Array.Copy(sourceBytes, bytes, sourceBytes.Length);

            var result = new UInt32[resultLen];
            for (int i = 0; i < resultLen; ++i)
            {
                result[i] = (UInt32)(bytes[i << 2] << 24) | (UInt32)(bytes[(i << 2) + 1] << 16) | (UInt32)(bytes[(i << 2) + 2] << 8) | (UInt32)bytes[(i << 2) + 3];
            }
            return result;
        }

        public static byte[] UIntArrayToBytes(UInt32[] source)
        {
            var result = new byte[source.Length * 4];

            int offset = 0;
            foreach (var ui in source)
            {
                result[offset] = (byte)((ui >> 24) & 0xff);
                result[offset + 1] = (byte)((ui >> 16) & 0xff);
                result[offset + 2] = (byte)((ui >> 8) & 0xff);
                result[offset + 3] = (byte)(ui & 0x0ff);
                offset += 4;
            }

            return result;
        }
    }
}
