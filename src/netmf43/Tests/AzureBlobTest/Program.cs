using System;
using Microsoft.SPOT;
using Microsoft.SPOT.Hardware;
using System.IO.Ports;

using PervasiveDigital.Hardware.ESP8266;
using PervasiveDigital.Net;
using PervasiveDigital.Net.Azure.Storage;

namespace AzureBlobTest
{
    public class Program
    {
        public static void Main()
        {
            // For neon
            var wifi = new Esp8266WifiDevice(new SerialPort("COM2", 115200, Parity.None, 8, StopBits.One), new OutputPort((Cpu.Pin)19, false), null);
            wifi.Connect("Xxxxx", "Xxxxx");

            var sntp = new SntpClient(wifi, "time1.google.com");
            sntp.SetTime();

            // You have to change this - the storage account does not exist and the key is not valid
            var account = new CloudStorageAccount("pdazurestoragedemo", "PblOktyRKcKQy+5qMe3doPcxzcUqKU38ZBTGCOs8g+10CamdApbJd1FDw2WTsZ8/k+LoUvCQsc4NPBZ0jY/9DA==");
            var blob = new BlobClient(wifi, account);

            var containerName = "cont3";
            
            // if needed...
            //blob.CreateContainer(containerName);

            var someBytes = new byte[256];
            for (int i = 0; i < someBytes.Length; ++i)
                someBytes[i] = (byte)i;
            blob.PutBlockBlob(containerName, "mybytes", someBytes);
        }
    }
}
