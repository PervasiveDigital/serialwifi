using System;
using Microsoft.SPOT;
using System.Net;

namespace PervasiveDigital.Hardware.ESP8266
{
    public class AccessPointClient
    {
        internal AccessPointClient(IPAddress address, string macAddress)
        {
            this.IpAddress = address;
            this.MacAddress = macAddress;
        }

        public IPAddress IpAddress { get; private set; }

        public string MacAddress { get; private set; }
    }
}
