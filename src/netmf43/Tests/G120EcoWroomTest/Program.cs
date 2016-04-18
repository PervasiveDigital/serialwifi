using System;
using System.Collections;
using System.IO.Ports;
using System.Threading;
using Microsoft.SPOT;
using Microsoft.SPOT.Hardware;
using PervasiveDigital;
using PervasiveDigital.Hardware.ESP8266;
using PervasiveDigital.Net;
using PervasiveDigital.Utilities;

namespace G120EcoWroomTest
{
    public class Program
    {
        private static readonly OutputPort _userLed = new OutputPort(GHI.Pins.FEZCobraII.DebugLed, false);

        public static void Main()
        {
            var port = new SerialPort(GHI.Pins.FEZCobraII.Socket5.SerialPortName, 115200, Parity.None, 8, StopBits.One);
            var wifi = new Esp8266WifiDevice(port, null, null);

            wifi.EnableDebugOutput = false;
            wifi.EnableVerboseOutput = false;

            Debug.Print("Access points:");
            var apList = wifi.GetAccessPoints();
            foreach (var ap in apList)
            {
                Debug.Print("ssid:" + ap.Ssid + "  ecn:" + ap.Ecn);
            }
            Debug.Print("-- end of list -------------");

            wifi.Connect("XXX", "XXX");

            Debug.Print("Station IP address : " + wifi.StationIPAddress.ToString());
            Debug.Print("Station MAC address : " + wifi.StationMacAddress);
            Debug.Print("Station Gateway address : " + wifi.StationGateway.ToString());
            Debug.Print("Station netmask : " + wifi.StationNetmask.ToString());

            var sntp = new SntpClient(wifi, "time1.google.com");
            sntp.SetTime();

            wifi.CreateServer(80, OnServerConnectionOpened);

            var httpClient = new HttpClient(wifi);
            //var request = new HttpRequest(new Uri("http://www.example.com/"));
            var request =
                new HttpRequest(new Uri("http://www.webservicex.net/uszip.asmx/GetInfoByAreaCode?USAreaCode=919"));
            request.ResponseReceived += HttpResponseReceived;
            httpClient.SendAsync(request);

            int iCounter = 0;
            bool state = true;
            while (true)
            {
                _userLed.Write(state);
                state = !state;
                ++iCounter;
                if (iCounter % 10 == 0)
                {
                    Debug.Print("Current UTC time : " + DateTime.UtcNow);
                }
                // Every 15 seconds
                if (iCounter % 30 == 0)
                {
#if CREATE_ACCESS_POINT
                    Debug.Print("Clients connected to this AP");
                    var clientList = wifi.GetConnectedClients();
                    foreach (var client in clientList)
                    {
                        Debug.Print("IP:" + client.IpAddress.ToString() + "  MAC:" + client.MacAddress);
                    }
                    Debug.Print("-- end of list -------------");
#endif
                    iCounter = 0;
                }
                Thread.Sleep(500);
            }

        }

        private static void OnServerConnectionOpened(object sender, WifiSocket socket)
        {
            socket.DataReceived += socket_DataReceived;
            socket.SocketClosed += socket_SocketClosed;
        }

        private static void socket_DataReceived(object sender, SocketReceivedDataEventArgs args)
        {
            var socket = (WifiSocket)sender;
            if (args.Data != null)
            {
                Debug.Print("Data Received : " + args.Data.Length);
                if (args.Data.Length > 0)
                {
                    var body = StringUtilities.ConvertToString(args.Data);
                    //TODO: Parse the request - here we're just going to reply with a 404
                    socket.Send("HTTP/1.1 404 NOT FOUND\r\nConnection: close\r\nContent-Length: 0\r\n\r\n");
                }
            }
        }

        private static void socket_SocketClosed(object sender, EventArgs args)
        {
            Debug.Print("Socket closed: " + ((WifiSocket)sender).Id);
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
