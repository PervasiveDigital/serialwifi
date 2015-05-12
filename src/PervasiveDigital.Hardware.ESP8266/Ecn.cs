using System;
using Microsoft.SPOT;

namespace PervasiveDigital.Net
{
    public enum Ecn : byte
    {
        Unknown = 0xff,
        Open = 0x00,
        WEP = 0x01,
        WPA_PSK = 0x02,
        WPA2_PSK = 0x03,
        WPA_WPA2_PSK = 0x04
    }
}
