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

        public HttpClient(INetworkAdapter adapter)
        {
            _adapter = adapter;
        }

        public void Dispose()
        {
            if (_socket != null)
            {
                _socket.Dispose();
                _socket = null;
            }
        }

        public HttpResponse Send(HttpRequest req)
        {
            if (_activeRequest != null)
                throw new InvalidOperationException("A request is already outstanding");

            _activeRequest = req;
            _requestCompletedEvent.Reset();

            using (var socket = GetSocket(req.Uri.Host, req.Uri.Port))
            {
                try
                {
                    StringBuilder buffer = new StringBuilder();

                    req.AppendMethod(buffer);
                    req.AppendHeaders(buffer);

                    socket.Send(buffer.ToString());
                    if (req.Body != null && req.Body.Length > 0)
                        socket.Send(req.Body);

                    _requestCompletedEvent.WaitOne();
                }
                finally
                {
                    socket.SocketClosed -= SocketOnSocketClosed;
                    socket.DataReceived -= SocketOnDataReceived;
                }
            }
            return _lastResponse;
        }

        public void SendAsync(HttpRequest req)
        {
            if (_activeRequest != null)
                throw new InvalidOperationException("A request is already outstanding");

            _activeRequest = req;

            _socket = GetSocket(req.Uri.Host, req.Uri.Port);

            StringBuilder buffer = new StringBuilder();

            req.AppendMethod(buffer);
            req.AppendHeaders(buffer);

            _socket.Send(buffer.ToString());
            if (req.Body != null && req.Body.Length > 0)
                _socket.Send(req.Body);
        }

        private ISocket GetSocket(string host, int port)
        {
            var socket = _adapter.OpenSocket(host, port, true);
            socket.SocketClosed += SocketOnSocketClosed;
            socket.DataReceived += SocketOnDataReceived;
            return socket;
        }

        private void SocketOnDataReceived(object sender, SocketReceivedDataEventArgs args)
        {
            if (_activeRequest == null)
                return;

            ISocket socket = (ISocket)sender;

            if (_activeRequest.OnResponseReceived(args))
            {
                _lastResponse = _activeRequest.Response;
                _activeRequest = null;
                // If this was an async send, we saved a ref to the socket.  Clean that up now.
                if (_socket!=null && _socket == socket)
                {
                    _socket.SocketClosed -= SocketOnSocketClosed;
                    _socket.DataReceived -= SocketOnDataReceived;
                    _socket.Dispose();
                    _socket = null;
                }
                _requestCompletedEvent.Set();
            }
        }

        private void SocketOnSocketClosed(object sender, EventArgs args)
        {
            // nothing to do here, for now
        }
    }
}
