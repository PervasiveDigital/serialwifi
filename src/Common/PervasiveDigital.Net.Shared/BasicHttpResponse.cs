using System;
using Microsoft.SPOT;

namespace PervasiveDigital.Net
{
    public struct BasicHttpResponse
    {
        public string Body { get; set; }
        public HttpStatusCode StatusCode { get; set; }
    }
}