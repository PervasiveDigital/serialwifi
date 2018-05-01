using System;
using System.Collections;
using System.IO.Ports;
using System.Net;
using System.Threading;
using PervasiveDigital.Hardware.SPWF04;
using Microsoft.SPOT;
using Microsoft.SPOT.Hardware;
using PervasiveDigital.Net;
using PervasiveDigital;
using PervasiveDigital.Utilities;
using System.Text;
using PervasiveDigital.Security.ManagedProviders;

namespace SimpleHttpTest
{
    public class Program
    {
        private static readonly OutputPort _userLed = new OutputPort(Cpu.Pin.GPIO_Pin15, false);
        private static bool _wifiIsConnected = false;

        // In order to create a truly robust long-running solution, you must combine the code below
        //   with proper use of a Watchdog Timer and exception handling on the wifi calls.
        // Hardware note: It has been our experience that to work at 115200 with the ESP8266 and NETMF, you need a 1024 byte serial buffer.
        //   Smaller serial buffers may result in portions of incoming TCP traffic being dropped, which can also break the protocol processing
        //   and result in hangs.

        public static void Main()
        {
            uint foo = Debug.GC(true);

            var port = new SerialPort("COM2", 115200, Parity.None, 8, StopBits.One);
            var wifi = new Spwf04WifiDevice(port, null);

            wifi.EnableDebugOutput = true;
            //wifi.EnableVerboseOutput = true;
            wifi.WifiConnectionStateChanged += Wifi_WifiConnectionStateChanged;

            wifi.EnableWifi(false);
            wifi.SetPrivacyMode(2);
            wifi.Connect("Calsynshire-24", "Escal8shun");
            wifi.EnableWifi(true);

            while (!_wifiIsConnected)
            {
                Thread.Sleep(500);
            }

            int code;
            string[] response;
            //wifi.HttpGet(new Uri("http://www.example.com/"), out code, out response);

            wifi.MqttConnect(new Uri("http://ingenuitymicro.azure-devices.net/"), "ingenuitymicro.azure-devices.net/redmond01/api-version=2016-11-14",
                GenerateSasToken("ingenuitymicro.azure-devices.net/devices/redmond01", "mKeKt3FDUOQg7U5RQ7Ucm12glq3o3fNsDOKFROo3lS0=", "device"));

            int iCounter = 0;
            bool state = true;
            while (true)
            {
                _userLed.Write(state);
                state = !state;
                ++iCounter;
//                if (iCounter % 10 == 0)
//                {
//                    Debug.Print("Current UTC time : " + DateTime.UtcNow);
//                }
//                // Every 15 seconds
//                if (iCounter % 30 == 0)
//                {
//#if CREATE_ACCESS_POINT
//                    Debug.Print("Clients connected to this AP");
//                    var clientList = wifi.GetConnectedClients();
//                    foreach (var client in clientList)
//                    {
//                        Debug.Print("IP:" + client.IpAddress.ToString() + "  MAC:" + client.MacAddress);
//                    }
//                    Debug.Print("-- end of list -------------");
//#endif
//                    iCounter = 0;
//                }
                Thread.Sleep(500);
            }
        }

        public static string GenerateSasToken(string resourceUri, string key, string policyName, int expiryInSeconds = 3600)
        {
            TimeSpan fromEpochStart = DateTime.UtcNow - new DateTime(1970, 1, 1);
            double totalSeconds = fromEpochStart.Ticks / 10000000.0;
            int expiry = ((int)totalSeconds + expiryInSeconds);

            string stringToSign = HttpUtility.UrlEncode(resourceUri) + "\n" + expiry;

            HMACSHA256 hmac = new HMACSHA256(Convert.FromBase64String(key));
            string signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign)));

            string token = StringUtilities.Format("SharedAccessSignature sr={0}&sig={1}&se={2}", HttpUtility.UrlEncode(resourceUri), HttpUtility.UrlEncode(signature), expiry.ToString());

            if (!StringUtilities.IsNullOrEmpty(policyName))
            {
                token += "&skn=" + policyName;
            }

            return token;
        }

        private static void Wifi_WifiConnectionStateChanged(object sender, bool connected)
        {
            _wifiIsConnected = connected;
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