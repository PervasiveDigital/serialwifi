using System;
using Microsoft.SPOT;

namespace PervasiveDigital.Net
{
    public delegate void SocketReceivedDataEventHandler(object sender, SocketReceivedDataEventArgs args);
    public delegate void SocketClosedEventHandler(object sender, EventArgs args);

    public class SocketReceivedDataEventArgs : EventArgs
    {
        public SocketReceivedDataEventArgs(byte[] data)
        {
            this.Data = data;
        }

        public byte[] Data { get; private set; }
    }

    public interface ISocket : IDisposable
    {
        void Open();
        void Close();
        void Send(string payload);
        void Send(byte[] payload);

        event SocketReceivedDataEventHandler DataReceived;
        event SocketClosedEventHandler SocketClosed;
    }
}
