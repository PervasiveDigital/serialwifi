using System;
using System.Collections;
using System.Text;
using System.Threading;
using Microsoft.SPOT;

namespace PervasiveDigital.Net
{
    public class HttpClient : IDisposable
    {
        private readonly INetworkAdapter _adapter;
        private ISocket _socket;
        private HttpRequest _activeRequest;
        private HttpResponse _lastResponse;
        private AutoResetEvent _requestCompletedEvent = new AutoResetEvent(false);

        public HttpClient(INetworkAdapter adapter) : this(adapter, null, 80)
        {
        }

        public HttpClient(INetworkAdapter adapter, string host) : this(adapter, host, 80)
        {
        }

        public HttpClient(INetworkAdapter adapter, string host, int port)
        {
            _adapter = adapter;
            this.Host = host;
            this.Port = port;
        }

        public void Dispose()
        {
            if (_socket != null)
            {
                _socket.Dispose();
                _socket = null;                
            }
        }

        public string Host { get; private set; }

        public int Port { get; private set; }

        public HttpResponse Send(HttpRequest req)
        {
            if (_activeRequest != null)
                throw new InvalidOperationException("A request is already outstanding");

            _activeRequest = req;
            _requestCompletedEvent.Reset();

            EnsureSocketOpen();

            StringBuilder buffer = new StringBuilder();

            req.AppendMethod(buffer);
            req.AppendHeaders(buffer);

            _socket.Send(buffer.ToString());
            if (req.Body!=null && req.Body.Length>0)
                _socket.Send(req.Body);

            _requestCompletedEvent.WaitOne();

            return _lastResponse;
        }

        public void SendAsync(HttpRequest req)
        {
            if (_activeRequest != null)
                throw new InvalidOperationException("A request is already outstanding");
            _activeRequest = req;

            EnsureSocketOpen();

            StringBuilder buffer = new StringBuilder();

            req.AppendMethod(buffer);
            req.AppendHeaders(buffer);

            _socket.Send(buffer.ToString());
            if (req.Body != null && req.Body.Length > 0)
                _socket.Send(req.Body);
        }

        private void EnsureSocketOpen()
        {
            try
            {
                if (_socket != null)
                    _socket.Open();
            }
            catch (Exception)
            {
                _socket = null;
            }
            if (_socket == null)
            {
                _socket = _adapter.OpenSocket(this.Host, this.Port, true);
                _socket.SocketClosed += SocketOnSocketClosed;
                _socket.DataReceived += SocketOnDataReceived;
            }
        }

        private void SocketOnDataReceived(object sender, SocketReceivedDataEventArgs args)
        {
            if (_activeRequest == null)
                return;

            if (_activeRequest.OnResponseReceived(args))
            {
                _lastResponse = _activeRequest.Response;
                _activeRequest = null;
                _requestCompletedEvent.Set();
            }
        }

        private void SocketOnSocketClosed(object sender, EventArgs args)
        {
            // Nothing really to do here
        }
    }
}
