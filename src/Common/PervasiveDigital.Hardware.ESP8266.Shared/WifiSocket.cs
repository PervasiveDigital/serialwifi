using System;
using Microsoft.SPOT;
using System.Text;
using PervasiveDigital.Net;

namespace PervasiveDigital.Hardware.ESP8266
{
    public class WifiSocket : ISocket, IDisposable
    {
        private readonly Esp8266WifiDevice _parent;
        private readonly string _hostname;
        private readonly int _port;
        private readonly bool _fTcp;
        private int _iSocket;
        private bool _bConnected;

        public event SocketReceivedDataEventHandler DataReceived;
        public event SocketClosedEventHandler SocketClosed;

        internal WifiSocket(Esp8266WifiDevice device, int iSocket, string hostname, int port, bool fTcp)
        {
            _parent = device;
            _iSocket = iSocket;
            _hostname = hostname;
            _port = port;
            _fTcp = fTcp;
            _bConnected = false;
        }

        internal WifiSocket(Esp8266WifiDevice device, int iSocket, int port)
        {
            _parent = device;
            _iSocket = iSocket;
            _port = port;
            _hostname = null;
            _fTcp = true; // TEMP CODE
            _bConnected = false;
        }

        public int Id { get { return _iSocket; } }

        public string Hostname { get { return _hostname; } }
        
        public int Port { get {  return _port; } }
        
        public bool UseTcp { get { return _fTcp; } }

        public bool Connected
        {
            get { return _bConnected; }
            set { _bConnected = value; }
        }

        public void Dispose()
        {
            try
            {
                this.Close();
            }
            catch (Exception)
            {
                // ignore exceptions
            }
            if (_iSocket != -1)
            {
                _parent.DeleteSocket(_iSocket);
                _iSocket = -1;
            }
        }

        public void Open()
        {
            if (_iSocket!=-1)
                _parent.OpenSocket(_iSocket);
        }

        public void Send(string payload)
        {
            Send(Encoding.UTF8.GetBytes(payload));
        }

        public void Send(byte[] payload)
        {
            if (_iSocket!=-1)
                _parent.SendPayload(_iSocket, payload);
        }

        public void Close()
        {
            if (_iSocket != -1)
            {
                if (_bConnected)
                {
                    _parent.CloseSocket(_iSocket);
                    _bConnected = false;
                }
            }
        }

        internal void ReceivedData(byte[] data)
        {
            if (this.DataReceived != null)
                this.DataReceived(this, new SocketReceivedDataEventArgs(data));
        }

        internal void SocketClosedByPeer()
        {
            if (SocketClosed != null)
            {
                SocketClosed(this, EventArgs.Empty);
            }
            _bConnected = false;
        }
    }
}
