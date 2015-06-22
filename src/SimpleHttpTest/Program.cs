using System;
using System.Collections;
using System.IO.Ports;
using System.Threading;
using PervasiveDigital.Hardware.ESP8266;
using Microsoft.SPOT;
using Microsoft.SPOT.Hardware;
using PervasiveDigital.Net;

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
            
            wifi.Connect("MySsid", "soopersecret");

            wifi.EnableDebugOutput = true;

            var sntp = new SntpClient(wifi, "time1.google.com");
            sntp.Start();

            var httpClient = new HttpClient(wifi, "www.example.com");
            var request = new HttpRequest();
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