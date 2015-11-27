using System;
using System.Collections;
using System.Text;
using Microsoft.SPOT;

namespace PervasiveDigital.Net
{
    public abstract class HttpBase
    {
        private readonly Hashtable _headers = new Hashtable();
        private byte[] _body;

        protected HttpBase()
        {
        }

        public Hashtable Headers { get { return _headers; } }

        public string Accept
        {
            get { return (string)_headers["Accept"]; }
            set { _headers["Accept"] = value; }
        }

        public string AcceptLanguage
        {
            get { return (string)_headers["Accept-Language"]; }
            set { _headers["Accept-Language"] = value; }
        }

        public string UserAgent
        {
            get { return (string)_headers["User-Agent"]; }
            set { _headers["User-Agent"] = value; }
        }

        public string ContentType
        {
            get { return (string)_headers["Content-Type"]; }
            set { _headers["Content-Type"] = value; }
        }

        public byte[] Body
        {
            get
            {
                return _body;
            }
            set
            {
                if (value == null)
                {
                    _body = null;
                }
                else
                {
                    _body = new byte[value.Length];
                    Array.Copy(value, _body, value.Length);
                }
            }
        }

        // This is used internally to reduce the number of allocations and mem-to-mem copies
        protected void SetBody(byte[] body)
        {
            _body = body;
        }

        public string GetBodyAsString()
        {
            if (_body == null || _body.Length == 0)
                return "";
            else
                return new string(Encoding.UTF8.GetChars(_body));
        }
    }
}
