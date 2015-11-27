using System;
using System.Collections;
using System.Reflection;
using System.Text;
using Microsoft.SPOT;

namespace PervasiveDigital.Net
{
    public delegate void HttpResponseReceivedEventHandler(object sender, HttpResponse args);

    public class HttpRequest : HttpBase
    {
        private Uri _uri;
        private HttpResponse _response;
        private bool _failed = false;

        public event HttpResponseReceivedEventHandler ResponseReceived;

        public HttpRequest()
        {
            this.Method = HttpMethod.Get;
        }

        public HttpRequest(Uri uri)
        {
            this.Uri = uri;
            this.Method = HttpMethod.Get;
        }

        public HttpRequest(string method, Uri uri)
        {
            this.Uri = uri;
            this.Method = method;
        }

        /// <summary>
        /// Reset the state of the request so that the request can be re-sent.
        /// You can change the body and headers after reset and before resending.
        /// </summary>
        public void Reset()
        {
            // Prepare for a re-send of the same request
            _failed = false;
            _response = null;
        }

        internal bool OnResponseReceived(SocketReceivedDataEventArgs args)
        {
            // We got an unparsable reply, so ignore subsequent data packets
            if (_failed)
                return true;

            if (_response==null)
                _response = new HttpResponse();

            bool isComplete = false;
            try
            {
                isComplete = _response.ProcessResponse(args.Data);
            }
            catch (Exception)
            {
                _response = null;
                _failed = true;
                isComplete = true;
            }

            if (isComplete)
            {
                if (this.ResponseReceived != null)
                    this.ResponseReceived(this, _response);
                this.Reset();
            }

            return isComplete;
        }

        public string Username { get; set; }

        public string Password { get; set; }

        public string Method { get; set; }

        internal HttpResponse Response { get {  return _response; } }

        public Uri Uri
        {
            get { return _uri; }
            set
            {
                _uri = value;
                this.Headers["Host"] = _uri.Host;
            }
        }

        public int ContentLength
        {
            get
            {
                if (this.Headers.Contains("Content-Length"))
                    return (int)this.Headers["Content-Length"];
                else
                    return 0;
            }
            set
            {
                this.Headers["Content-Length"] = value;
            }
        }

        public string Expect
        {
            get
            {
                if (this.Headers.Contains("Expect"))
                    return (string)this.Headers["Expect"];
                else
                    return null;
            }
            set
            {
                this.Headers["Expect"] = value;
            }
        }

        internal void AppendMethod(StringBuilder buffer)
        {
            buffer.AppendLine(this.Method + " " + _uri.PathAndQuery + " HTTP/1.1");
        }

        internal void AppendHeaders(StringBuilder buffer)
        {
            foreach (var item in this.Headers)
            {
                var key = ((DictionaryEntry) item).Key;
                var val = ((DictionaryEntry) item).Value;

                //TODO: Dates and other types and well-known header keys may need special formatting
                buffer.AppendLine(key + ": " + val);
            }
            if (this.Body != null && this.Body.Length > 0 && !this.Headers.Contains("Content-Length"))
            {
                buffer.AppendLine("Content-Length: " + this.Body.Length);
            }
            // terminate headers with a blank line
            buffer.Append("\r\n");
        }
    }
}
