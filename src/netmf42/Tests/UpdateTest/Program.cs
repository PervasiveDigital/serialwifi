using System;
using System.Collections;
using System.IO.Ports;
using System.Threading;
using PervasiveDigital.Hardware.ESP8266;
using Microsoft.SPOT;
using Microsoft.SPOT.Hardware;
using PervasiveDigital.Net;
using PervasiveDigital;
using PervasiveDigital.Utilities;

namespace UpdateTest
{
    public class Program
    {
        // Change these to match your ESP8266 configuration
        private static readonly OutputPort _rfPower = new OutputPort((Cpu.Pin)19, false);
        private static readonly OutputPort _userLed = new OutputPort(Cpu.Pin.GPIO_Pin13, false);

        public static void Main()
        {
            var port = new SerialPort("COM2", 115200, Parity.None, 8, StopBits.One);
            var wifi = new Esp8266WifiDevice(port, _rfPower, null);

            wifi.EnableDebugOutput = true;
            //wifi.EnableVerboseOutput = true;

            wifi.SetOperatingMode(OperatingMode.Station);
            wifi.Connect("XXX", "XXX");

            Debug.Print("Version before update : ");
            foreach (var line in wifi.Version)
            {
                Debug.Print(line);
            }
            Debug.Print("------------------------");

            wifi.Update((progress) => { Debug.Print("Update progress : " + progress); });

            Debug.Print("Version after update : ");
            foreach (var line in wifi.Version)
            {
                Debug.Print(line);
            }
            Debug.Print("------------------------");

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
                    iCounter = 0;
                }
                Thread.Sleep(500);
            }
        }
    }
}