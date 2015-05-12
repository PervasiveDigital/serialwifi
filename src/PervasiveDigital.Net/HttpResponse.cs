using System;
using System.Text;
using PervasiveDigital.Utilities;
using Microsoft.SPOT;

namespace PervasiveDigital.Net
{
    public class HttpResponse : HttpBase
    {
        public enum HttpParsingState
        {
            Empty, ResultCode, Headers, Body, Complete, Error
        }
        private HttpParsingState _state = HttpParsingState.Empty;
        private readonly CircularBuffer _buffer = new CircularBuffer(512, 1, 256);
        private object _lock = new object();

        internal HttpResponse()
        {
            this.Body = "";
            this.StatusCode = -1;
            this.Reason = "";
        }

        public int StatusCode { get; private set; }

        public string Reason { get; private set; }

        public HttpParsingState ParsingState
        {
            get {  return _state; }
        }

        internal bool ProcessResponse(byte[] data)
        {
            lock (_lock)
            {
                _buffer.Put(data);

                switch (_state)
                {
                    case HttpParsingState.Empty:
                    case HttpParsingState.ResultCode:
                        _state = HttpParsingState.ResultCode;
                        ProcessResultCode();
                        break;
                    case HttpParsingState.Headers:
                        ProcessHeaders();
                        break;
                    case HttpParsingState.Body:
                        ProcessBody();
                        break;
                    default:  // shouldn't happen, but just in case we get here with Complete or Error state
                        return true;
                }
            }

            // return true for completed parsing, false if we need more data
            return _state == HttpParsingState.Complete || _state == HttpParsingState.Error;
        }

        private void ProcessResultCode()
        {
            // find the first newline
            var idxNewline = _buffer.IndexOf(0x0a);
            if (idxNewline == -1)
                return; // need more data

            // pull out the result line
            var data = _buffer.Get(idxNewline + 1);
            var line = new string(Encoding.UTF8.GetChars(data));

            // parse it
            var tokens = line.Trim().Split(' ');
            if (tokens.Length > 1)
                this.StatusCode = int.Parse(tokens[1]);
            if (tokens.Length > 2)
                this.Reason = tokens[2];

            _state = HttpParsingState.Headers;

            // Fall through to process whatever headers we might already have in the buffer
            ProcessHeaders();
        }

        private void ProcessHeaders()
        {
            do
            {
                var idxNewline = _buffer.IndexOf(0x0a);
                if (idxNewline == -1)
                    return;

                var data = _buffer.Get(idxNewline + 1);
                var line = new string(Encoding.UTF8.GetChars(data)).Trim();

                if (line == "")
                {
                    _state = HttpParsingState.Body;
                    ProcessBody();
                    break;
                }
                else
                    ProcessHeader(line);
            } while (true);

        }

        private void ProcessBody()
        {
            var contentLength = -1;
            if (this.Headers.Contains("Content-Length"))
                contentLength = int.Parse((string)this.Headers["Content-Length"]);

            if (contentLength == -1)
            {
                ProcessTextBody();
            }
            else
            {
                ProcessCountedBody(contentLength);
            }
        }

        private void ProcessHeader(string line)
        {
            try
            {
                var idxColon = line.IndexOf(':');
                var key = line.Substring(0, idxColon).Trim();
                var value = line.Substring(idxColon + 1).Trim();
                this.Headers.Add(key, value);
            }
            catch (Exception)
            {
                // ignore failures - the header just doesn't get added
                Debug.Print("Failed to parse header : " + line);
            }
        }

        private void ProcessTextBody()
        {
            // read until the first blank line
            do
            {
                var idxNewline = _buffer.IndexOf(0x0a);
                if (idxNewline == -1)
                    return; // need more data

                var data = _buffer.Get(idxNewline + 1);
                var line = new string(Encoding.UTF8.GetChars(data)).Trim();
                if (line == "")
                {
                    _state = HttpParsingState.Complete;
                    return;
                }
                // append the line to the body
                this.Body += line;
            } while (true);
        }

        private void ProcessCountedBody(int contentLength)
        {
            if (_buffer.Size < contentLength)
                return; // need more data

            try
            {
                this.Body = new string(Encoding.UTF8.GetChars(_buffer.Get(_buffer.Size)));
                _state = HttpParsingState.Complete;
            }
            catch (Exception)
            {
                _state = HttpParsingState.Error;
            }
        }
    }
}
