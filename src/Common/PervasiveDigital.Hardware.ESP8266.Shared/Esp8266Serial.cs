using System;
using System.Collections;
using System.IO.Ports;
using System.Text;
using System.Threading;
using Microsoft.SPOT;
using PervasiveDigital.Utilities;

namespace PervasiveDigital.Hardware.ESP8266
{
    public delegate void HardwareFaultHandler(object sender, int cause);

    internal class Esp8266Serial
    {
        public delegate void DataReceivedHandler(object sender, byte[] stream, int channel);
        public delegate void SocketOpenedHandler(object sender, int channel, out bool fHandled);
        public delegate void SocketClosedHandler(object sender, int channel);

        public const int DefaultCommandTimeout = 10000;
        private readonly SerialPort _port;
        private readonly Queue _responseQueue = new Queue();
        private readonly AutoResetEvent _responseReceived = new AutoResetEvent(false);
        private readonly AutoResetEvent _newDataReceived = new AutoResetEvent(false);
        private readonly Queue _receivedDataQueue = new Queue();

        private readonly object _lockSendExpect = new object();
        private readonly byte[] _ipdSequence;

        // Circular buffers that will grow in 256-byte increments - one for commands and one for received streams
        private readonly CircularBuffer _buffer = new CircularBuffer(512, 1, 256);
        private readonly CircularBuffer _stream = new CircularBuffer(512, 1, 256);

        public event DataReceivedHandler DataReceived;
        public event SocketOpenedHandler SocketOpened;
        public event SocketClosedHandler SocketClosed;
        public event HardwareFaultHandler Fault;

        private int _cbStream = 0;
        private int _receivingOnChannel;
        private object _readLoopMonitor = new object();
        private readonly ManualResetEvent _noStreamRead = new ManualResetEvent(true);
        private bool _enableDebugOutput;
        private bool _enableVerboseOutput;

        public Esp8266Serial(SerialPort port)
        {
            this.CommandTimeout = DefaultCommandTimeout;
            _port = port;
            _ipdSequence = Encoding.UTF8.GetBytes("+IPD");
        }

        public void Start()
        {
            _port.DataReceived += PortOnDataReceived;
            _port.Open();
        }

        public void Stop()
        {
            _port.Close();
            _port.DataReceived -= PortOnDataReceived;
        }

        public int CommandTimeout { get; set; }

        public bool EnableDebugOutput
        {
            get { return _enableDebugOutput; }
            set { _enableDebugOutput = value; }
        }

        public bool EnableVerboseOutput
        {
            get { return _enableVerboseOutput; }
            set { _enableVerboseOutput = value; }
        }

        public void SendCommand(string send)
        {
            lock (_lockSendExpect)
            {
                DiscardBufferedInput();
                WriteCommand(send);
            }
        }

        public void SendAndExpect(string send, string expect)
        {
            SendAndExpect(send, expect, DefaultCommandTimeout);
        }

        public void SendAndExpect(string send, string[] accept, string expect)
        {
            SendAndExpect(send, accept, expect, DefaultCommandTimeout);
        }

        public void SendAndExpect(string send, string expect, int timeout)
        {
            lock (_lockSendExpect)
            {
                DiscardBufferedInput();
                WriteCommand(send);
                Expect(new[] { send, "no change", "link is not valid" }, expect, timeout); // TODO Perhaps add generic acceptable array - ESP AT keeps changing. DAV
            }
        }

        public void SendAndExpect(string send, string[] accept, string expect, int timeout)
        {
            lock (_lockSendExpect)
            {
                DiscardBufferedInput();
                WriteCommand(send);
                var acceptList = new string[accept.Length + 1];
                acceptList[0] = send;
                Array.Copy(accept, 0, acceptList, 1, accept.Length);
                Expect(acceptList, expect, timeout);
            }
        }

        public string[] SendAndReadUntil(string send, string terminator)
        {
            return SendAndReadUntil(send, terminator, DefaultCommandTimeout);
        }

        public void Find(string successString)
        {
            Find(successString, DefaultCommandTimeout);
        }

        public void Find(string successString, int timeout)
        {
            SendAndReadUntil(null, successString, timeout);
        }

        public string[] SendAndReadUntil(string send, string terminator, int timeout)
        {
            return SendAndReadUntil(send, terminator, null, timeout);
        }

        public string[] SendAndReadUntil(string send, string terminator, string[]badresp, int timeout)
        {
            ArrayList result = new ArrayList();
            if (send != null)
                SendCommand(send);
            do
            {
                var line = GetReplyWithTimeout(timeout);
                if (line != null && line.Length > 0)
                {
                    // in case echo is on
                    if (send != null && line.IndexOf(send) == 0)
                        continue;
                    // read until we see the magic termination string - usually 'OK'
                    if (line.IndexOf(terminator) == 0)
                        break;
                    result.Add(line);
                    if (badresp != null)
                    {
                        if (Array.IndexOf(badresp, line) > -1)
                            break;
                    }
                }
            } while (true);
            return (string[])result.ToArray(typeof(string));
        }

        public string SendCommandAndReadReply(string command)
        {
            return SendCommandAndReadReply(command, DefaultCommandTimeout);
        }

        public string SendCommandAndReadReply(string command, int timeout)
        {
            string response;
            lock (_lockSendExpect)
            {
                DiscardBufferedInput();
                WriteCommand(command);
                do
                {
                    response = GetReplyWithTimeout(timeout);
                } while (response == null || response == "" || response == command);
            }
            return response;
        }

        public void Expect(string expect)
        {
            Expect(null, expect, DefaultCommandTimeout);
        }

        public void Expect(string expect, int timeout)
        {
            Expect(null, expect, timeout);
        }

        public void Expect(string[] accept, string expect)
        {
            Expect(accept, expect, DefaultCommandTimeout);
        }

        public void Expect(string[] accept, string expect, int timeout)
        {
            if (accept == null)
                accept = new[] { "" };

            bool acceptableInputFound;
            string response;
            do
            {
                acceptableInputFound = false;
                response = GetReplyWithTimeout(timeout);

                foreach (var s in accept)
                {
#if MF_FRAMEWORK
                    if (response == "" || string.Equals(response.ToLower(), s.ToLower()))
#else
                    if (response=="" || string.Equals(response, s, StringComparison.OrdinalIgnoreCase))
#endif
                    {
                        acceptableInputFound = true;
                        break;
                    }
                }
            } while (acceptableInputFound);
#if MF_FRAMEWORK
            if (!string.Equals(response.ToLower(), expect.ToLower()))
#else
            if (!string.Equals(response, expect, StringComparison.OrdinalIgnoreCase))
#endif
            {
                throw new FailedExpectException(expect, response);
            }
        }

        public string GetReplyWithTimeout(int timeout)
        {
            string response = null;
            bool haveNewData;
            do
            {
                lock (_responseQueue.SyncRoot)
                {
                    if (_responseQueue.Count > 0)
                    {
                        response = (string)_responseQueue.Dequeue();
                    }
                    else
                    {
                        _responseReceived.Reset();
                    }
                }

                // If nothing was waiting in the queue, then wait for new data to arrive
                haveNewData = false;
                if (response == null)
                    haveNewData = _responseReceived.WaitOne(timeout, false);

            } while (response == null && haveNewData);

            // We have received no data, and the WaitOne timed out
            if (response == null && !haveNewData)
            {
                throw new CommandTimeoutException();
            }

            if (_enableDebugOutput)
                Debug.Print("Consumed: " + response);

            return response;
        }

        private byte[] ReadExistingBinary()
        {
            int arraySize = _port.BytesToRead;

            byte[] received = new byte[arraySize];

            _port.Read(received, 0, arraySize);
            if (_enableVerboseOutput)
                Dump("RECV:", received);

            return received;
        }

        public void DiscardBufferedInput()
        {
            // you cannot discard input if a stream read is in progress
            if(!_noStreamRead.WaitOne(10000,false))   //TODO DAV - Review. Had to add timeout as we are getting deadlocked here
                Debug.Print("noStreamRead never released");
            Monitor.Enter(_readLoopMonitor);
            try
            {
                lock (_responseQueue.SyncRoot)
                {
                    _responseQueue.Clear();
                    _responseReceived.Reset();
                    _buffer.Clear();
                    _port.DiscardInBuffer();
                    _stream.Clear();
                }
                if (_enableVerboseOutput)
                    Debug.Print("BUFFER CLEARED");
            }
            finally
            {
                Monitor.Exit(_readLoopMonitor);
            }
        }

        public void Write(string txt)
        {
            this.Write(Encoding.UTF8.GetBytes(txt));
        }

        public void Write(byte[] payload)
        {
            if (_enableVerboseOutput)
                Dump("SEND:", payload);
            _port.Write(payload, 0, payload.Length);
        }

        private void WriteCommand(string txt)
        {
            if (_enableDebugOutput)
                Log("Sent command : " + txt);
            this.Write(txt + "\r\n");
        }

        private void PortOnDataReceived(object sender, SerialDataReceivedEventArgs serialDataReceivedEventArgs)
        {
            if (serialDataReceivedEventArgs.EventType == SerialData.Chars)
            {
                // Keep doing this while there are bytes to read - don't rely on just event notification
                // The ESP8266 is very timing sensitive and subject to buffer overrun - keep the loop tight.
                var newInput = ReadExistingBinary();
                if (newInput != null && newInput.Length > 0)
                {
                    _buffer.Put(newInput);
                }

                ProcessBufferedInput();
            }
        }

        private void ProcessBufferedInput()
        {
            do
            {
                Monitor.Enter(_readLoopMonitor);
                try
                {
                    // if _cbstream is non-zero, then we are reading a counted stream of bytes, not crlf-delimited input
                    if (_cbStream != 0)
                    {
                        // If we are capturing an input stream, then copy characters from the serial port
                        //   until the count of desired characters == 0
                        while (_cbStream > 0 && _buffer.Size > 0)
                        {
                            var eat = _cbStream;
                            if (_buffer.Size < _cbStream)
                                eat = _buffer.Size;
                            _stream.Put(_buffer.Get(eat));
                            _cbStream -= eat;
                            if (_enableDebugOutput)
                            {
                                Debug.Print("STREAM: Copied " + eat + " characters to stream. Buffer contains:" +
                                            _buffer.Size + " Stream contains : " + _stream.Size + " Still need:" +
                                            _cbStream);
                            }
                        }
                        // If we have fulfilled the stream request, then dispatch the received data to the datareceived handler
                        if (_cbStream == 0)
                        {
                            if (DataReceived != null)
                            {
                                try
                                {
                                    var channel = _receivingOnChannel;
                                    var data = _stream.Get(_stream.Size);
                                    _noStreamRead.Set();
                                    // Run this in the background so as not to slow down the read loop
                                    ThreadPool.QueueUserWorkItem(DataReceivedThunk ,new object[] { data, channel });
                                    //new Thread(() => { DataReceived(this, data, channel); }).Start();
                                }
                                catch (Exception)
                                {
                                    // mask exceptions in the callback so that they don't kill our read loop
                                }
                            }
                            _receivingOnChannel = -1;
                            _stream.Clear();
                        }
                    }

                    if (_cbStream == 0)
                    {
                        // process whatever is left in the buffer (after fulfilling any stream requests)
                        var idxNewline = _buffer.IndexOf(0x0A);
                        var idxIPD = _buffer.IndexOf(_ipdSequence);

                        while ((idxNewline != -1 || idxIPD != -1) && _cbStream == 0)
                        {
                            string line = "";
                            if ((idxIPD == -1 && idxNewline!=-1) || (idxNewline != -1 && idxNewline < idxIPD))
                            {
                                if (idxNewline == 0)
                                    line = "";
                                else
                                    line = StringUtilities.ConvertToString(_buffer.Get(idxNewline)).Trim();
                                // eat the newline too
                                _buffer.Skip(1);
                                if (!StringUtilities.IsNullOrEmpty(line))
                                {
                                    if (_enableDebugOutput)
                                        Log("Received : " + line);

                                    if (line.StartsWith("ets "))
                                    {
                                        // we're rebooting
                                        var idxCause = line.IndexOf("rst cause:");
                                        int iCause = -1;
                                        if (idxCause != -1)
                                        {
                                            var start = idxCause + 10;
                                            var idxComma = line.Substring(start).IndexOf(',');
                                            iCause = int.Parse(line.Substring(start, idxComma));
                                        }
                                        if (this.Fault != null)
                                            this.Fault(this, iCause);
                                    }
                                    if (line.StartsWith("wdt ") ||
                                        line.StartsWith("load ") ||
                                        line.StartsWith("tail ") ||
                                        line.StartsWith("chksum ") ||
                                        line.StartsWith("csum "))
                                        return;

                                        // Handle async notifications and command responses
                                        var idxClosed = line.IndexOf(",CLOSED");
                                    if (idxClosed != -1)
                                    {
                                        // Handle socket-closed notification
                                        var channel = int.Parse(line.Substring(0, idxClosed));
                                        if (this.SocketClosed != null)
                                            this.SocketClosed(this, channel);
                                    }
                                    else
                                    {
                                        var idxConnect = line.IndexOf(",CONNECT");
                                        if (idxConnect != -1)
                                        {
                                            // Handle socket-opened notification
                                            bool fHandled = false;
                                            var channel = int.Parse(line.Substring(0, idxConnect));
                                            if (this.SocketOpened != null)
                                                this.SocketOpened(this, channel, out fHandled);
                                            if (!fHandled)
                                                EnqueueLine(line);
                                        }
                                        else
                                            EnqueueLine(line);
                                    }
                                }
                            }
                            else if (idxIPD!=-1) // idxIPD found before newline
                            {
                                // find the colon which ends the data-stream introducer
                                var idxColon = _buffer.IndexOf(0x3A);
                                // we did not get the full introducer - we have to wait for more chars to come in
                                if (idxColon == -1)
                                    break;
                                // Convert the introducer
                                _buffer.Skip(idxIPD);
                                line = StringUtilities.ConvertToString(_buffer.Get(idxColon - idxIPD)).Trim();
                                _buffer.Skip(1); // eat the colon

                                if (line != null && line.Length > 0)
                                {
                                    var tokens = line.Split(',');
                                    _receivingOnChannel = int.Parse(tokens[1]);
                                    _cbStream = int.Parse(tokens[2]);
                                    // block anything that would interfere with the stream read - this is used in the DiscardBufferedInput call that preceeds the sending of every command
                                    _noStreamRead.Reset();
                                    if (_enableDebugOutput)
                                        Log("Reading a stream of " + _cbStream + " bytes for channel " +
                                            _receivingOnChannel);
                                }
                            }
                            // What next?
                            idxNewline = _buffer.IndexOf(0x0A);
                            idxIPD = _buffer.IndexOf(_ipdSequence);
                        }
                    }
                }
                catch (Exception exc)
                {
                    // Ignore exceptions - this loop needs to keep running
                    Debug.Print("Exception in Esp8266.ReadLoop() : " + exc);
                }
                finally
                {
                    Monitor.Exit(_readLoopMonitor);
                }
            } while (_cbStream > 0 && _buffer.Size > 0);
        }

        private void DataReceivedThunk(object state)
        {
            var args = (object[])state;
            if (DataReceived != null)
            {
                try
                {
                    DataReceived(this, (byte[])args[0], (int)args[1]);
                }
                catch
                {
                }
            }
        }

        private void EnqueueLine(string line)
        {
            lock (_responseQueue.SyncRoot)
            {
                _responseQueue.Enqueue(line);
                _responseReceived.Set();
            }
        }

        private static void Log(string msg)
        {
            Debug.Print(msg);
        }

        private static void Dump(string tag, byte[] data)
        {
            StringBuilder sbLeft = new StringBuilder();
            StringBuilder sbRight = new StringBuilder();
            sbLeft.Append(tag);

            // round up the length to make for pretty output
            var length = (data.Length + 15) / 16 * 16;
            var actualLen = data.Length;
            for (int i = 0 ; i < length ; ++i)
            {
                if (i < actualLen)
                {
                    var b = data[i];
                    sbLeft.Append(b.ToHex() + ' ');
                    if (b > 32 && b < 127)
                        sbRight.Append((char) b);
                    else
                        sbRight.Append('.');
                }
                else
                {
                    sbLeft.Append("   ");
                    sbRight.Append(' ');
                }
                if ((i + 1) % 8 == 0)
                    sbLeft.Append("  ");
                if ((i + 1) % 16 == 0)
                {
                    sbLeft.Append(sbRight);
                    Debug.Print(sbLeft.ToString());
                    sbLeft.Clear();
                    sbRight.Clear();
                    for (var j = 0; j < tag.Length; ++j)
                        sbLeft.Append(' ');
                }
            }
            if (sbRight.Length > 0)
            {
                sbLeft.Append(sbRight + "  ");
                Debug.Print(sbLeft.ToString());
            }
        }
    }
}
