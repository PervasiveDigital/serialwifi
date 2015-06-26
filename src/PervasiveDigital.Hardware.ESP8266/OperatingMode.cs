using System;
using Microsoft.SPOT;

namespace PervasiveDigital.Hardware.ESP8266
{
    public enum OperatingMode
    {
        Unknown = -1,
        Station = 0,
        AccessPoint = 1,
        Both = 2
    }
}
