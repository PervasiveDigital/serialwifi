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

namespace SimpleHttpTest
{
    public class Program
    {
        private static readonly OutputPort _userLed = new OutputPort(Cpu.Pin.GPIO_Pin15, false);

        // In order to create a truly robust long-running solution, you must combine the code below
        //   with proper use of a Watchdog Timer and exception handling on the wifi calls.
        // Hardware note: It has been our experience that to work at 115200 with the ESP8266 and NETMF, you need a 1024 byte serial buffer.
        //   Smaller serial buffers may result in portions of incoming TCP traffic being dropped, which can also break the protocol processing
        //   and result in hangs.

        public static void Main()
        {
            var port = new SerialPort("COM2", 115200, Parity.None, 8, StopBits.One);
            var wifi = new Spwf04WifiDevice(port, null);

            wifi.EnableDebugOutput = true;
            //wifi.EnableVerboseOutput = true;

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