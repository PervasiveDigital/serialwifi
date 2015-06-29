#define ACCESS_POINT
using System;
using System.Collections;
using System.IO.Ports;
using System.Threading;
using PervasiveDigital.Hardware.ESP8266;
using Microsoft.SPOT;
using Microsoft.SPOT.Hardware;
using PervasiveDigital.Net;
using PervasiveDigital;

namespace SimpleHttpTest
{
    public class Program
    {
        private static readonly OutputPort _rfPower = new OutputPort((Cpu.Pin)19, false);
        private static readonly OutputPort _userLed = new OutputPort(Cpu.Pin.GPIO_Pin13, false);

        public static void Main()
        {
            var port = new SerialPort("COM2", 115200, Parity.None, 8, StopBits.One);
            var wifi = new Esp8266WifiDevice(port, _rfPower, null);
            // on Oxygen+Neon, you can use use new NeonWifiDevice() without providing a port
            //wifi.EnableDebugOutput = true;
            //wifi.EnableVerboseOutput = true;

#if ACCESS_POINT
            wifi.Mode = OperatingMode.Both;
            wifi.ConfigureAccessPoint("serwifitest", "24681234", 5, Ecn.WPA2_PSK);
            wifi.EnableDhcp(OperatingMode.AccessPoint, false);
#else
            wifi.Mode = OperatingMode.Station;
#endif
            wifi.Connect("XXX", "XXX");

            Debug.Print("Station IP address : " + wifi.StationIPAddress.ToString());
            Debug.Print("Station MAC address : " + wifi.StationMacAddress);
            Debug.Print("Station Gateway address : " + wifi.StationGateway.ToString());
            Debug.Print("Station netmask : " + wifi.StationNetmask.ToString());

            Debug.Print("AP SSID : " + wifi.AccessPointSsid);
            Debug.Print("AP Password : " + wifi.AccessPointPassword);
            Debug.Print("AP Channel : " + wifi.AccessPointChannel);
            Debug.Print("AP ECN : " + wifi.AccessPointEcn);

            Debug.Print("AP IP address : " + wifi.AccessPointIPAddress.ToString());
            Debug.Print("AP MAC address : " + wifi.AccessPointMacAddress);
            Debug.Print("AP Gateway address : " + wifi.AccessPointGateway.ToString());
            Debug.Print("AP netmask : " + wifi.AccessPointNetmask.ToString());

            var sntp = new SntpClient(wifi, "time1.google.com");
            sntp.Start();

            var httpClient = new HttpClient(wifi, "www.example.com");
            var request = new HttpRequest(new Uri("http://www.example.com/"));
            request.ResponseReceived += HttpResponseReceived;
            httpClient.SendAsync(request);

            int iCounter = 0;
            bool state = true;
            while (true)
            {
                _userLed.Write(state);
                state = !state;
                if (++iCounter == 10)
                {
                    Debug.Print("Current UTC time : " + DateTime.UtcNow);
                    iCounter = 0;
                }
                Thread.Sleep(500);
            }
        }

        private static void HttpResponseReceived(object sender, HttpResponse resp)
        {
            if (resp == null)
            {
                Debug.Print("Failed to parse response");
                return;
            }
            Debug.Print("==== Response received ================================");
            Debug.Print("Status : " + resp.StatusCode);
            Debug.Print("Reason : " + resp.Reason);
            foreach (var item in resp.Headers)
            {
                var key = ((DictionaryEntry)item).Key;
                var val = ((DictionaryEntry)item).Value;
                Debug.Print(key + " : " + val);
            }
            if (resp.Body != null && resp.Body.Length > 0)
            {
                Debug.Print("Body:");
                Debug.Print(resp.GetBodyAsString());
            }
        }

    }
}