using System;
using System.Collections;
using Microsoft.SPOT;
using Microsoft.SPOT.Hardware;
using System.IO.Ports;
using System.Net;
using System.Threading;

using PervasiveDigital.Net;

namespace PervasiveDigital.Hardware.ESP8266
{
    public delegate void WifiBootedEventHandler(object sender, EventArgs args);
    public delegate void WifiErrorEventHandler(object sender, EventArgs args);
    public delegate void WifiConnectionStateEventHandler(object sender, EventArgs args);

    public class Esp8266WifiDevice : IWifiAdapter, IDisposable
    {
        // The amount of time that we will search for 'OK' in response to joining an AP
        public const int JoinTimeout = 30000;

        public const string AT = "AT";
        public const string OK = "OK";
        public const string EchoOffCommand = "ATE0";
        public const string GetFirmwareVersionCommand = "AT+GMR";
        public const string GetAddressInformationCommand = "AT+CIFSR";
        public const string ListAccessPointsCommand = "AT+CWLAP";
        public const string JoinAccessPointCommand = "AT+CWJAP=";
        public const string QuitAccessPointCommand = "AT+CWQAP";
        public const string SetMuxModeCommand = "AT+CIPMUX=";
        public const string SessionStartCommand = "AT+CIPSTART=";
        public const string SessionEndCommand = "AT+CIPCLOSE=";
        public const string LinkedReply = "Linked";
        public const string SendCommand = "AT+CIPSEND=";
        public const string SendCommandReply = "SEND OK";
        public const string ConnectReply = "CONNECT";
        public const string ErrorReply = "ERROR";

        private readonly ManualResetEvent _isInitializedEvent = new ManualResetEvent(false);
        private readonly WifiSocket[] _sockets = new WifiSocket[4];
        private Esp8266Serial _esp;
        private bool _enableDebugOutput;
        private bool _enableVerboseOutput;

        // operation lock - used to protect any interaction with the esp8266 serial interface
        //   from being trampled on by another operation
        private object _oplock = new object();

        public event WifiBootedEventHandler Booted;
        //public event WifiErrorEventHandler Error;
        //public event WifiConnectionStateEventHandler ConnectionStateChanged;

        private OutputPort _powerPin = null;
        private OutputPort _resetPin = null;

        public Esp8266WifiDevice(SerialPort port, OutputPort powerPin, OutputPort resetPin)
        {
            _powerPin = powerPin;
            _resetPin = resetPin;
            Initialize(port);
        }

        private void Initialize(SerialPort port)
        {
            _esp = new Esp8266Serial(port);
            _esp.DataReceived += OnDataReceived;
            _esp.SocketClosed += OnSocketClosed;
            _esp.Start();
            new Thread(BackgroundInitialize).Start();
        }

        public void Dispose()
        {
        }

        public bool EnableDebugOutput
        {
            get { return _enableDebugOutput; }
            set
            {
                _enableDebugOutput = value;
                _esp.EnableDebugOutput = value;
            }
        }

        public bool EnableVerboseOutput
        {
            get { return _enableVerboseOutput; }
            set
            {
                _enableVerboseOutput = value;
                _esp.EnableVerboseOutput = value;
            }
        }

        // Use this to make sure no one else interrupts your sequence of interactions with the esp hardware block.
        public object OperationLock
        {
            get {  return _oplock; }
        }

        public void Connect(string ssid, string password)
        {
            EnsureInitialized();
            lock (_oplock)
            {
                var info = _esp.SendAndReadUntil(JoinAccessPointCommand + '"' + ssid + "\",\"" + password + '"', OK, JoinTimeout);
                foreach (var line in info)
                {
                    if (line.IndexOf("STAIP") != -1)
                    {
                        var arg = Unquote(line.Substring(line.IndexOf(',') + 1));
                        this.IPAddress = IPAddress.Parse(arg);
                    }
                    else if (line.IndexOf("STAMAC") != -1)
                    {
                        var arg = Unquote(line.Substring(line.IndexOf(',') + 1));
                        this.MacAddress = arg;
                    }
                }

                // Update our IP address
                //GetAddressInformation();
            }
        }

        public void Disconnect()
        {
            EnsureInitialized();
            lock (_oplock)
            {
                _esp.SendAndExpect(QuitAccessPointCommand, OK);
            }
        }

        public ISocket OpenSocket(string hostNameOrAddress, int portNumber, bool useTcp)
        {
            EnsureInitialized();
            // We lock on sockets here - we will claim oplock in OpenSocket(int socket)
            lock (_sockets)
            {
                int iSocket = -1;
                for (int i = 0; i < _sockets.Length; ++i)
                {
                    if (_sockets[i] == null)
                    {
                        iSocket = i;
                        break;
                    }
                }
                if (iSocket < 0)
                {
                    throw new Exception("Too many sockets open - you must close one first.");
                }

                var result = new WifiSocket(this, iSocket, hostNameOrAddress, portNumber, useTcp);
                _sockets[iSocket] = result;

                return OpenSocket(iSocket);
            }
        }

        internal WifiSocket OpenSocket(int socket)
        {
            EnsureInitialized();
            lock (_oplock)
            {
                // We should get back "n,CONNECT" where n is the socket number
                var sock = _sockets[socket];
                int retries = 3;
                string reply;
                bool success = true;
                do
                {
                    success = true;
                    reply =
                        _esp.SendCommandAndReadReply(SessionStartCommand + socket + ',' +
                                                     (sock.UseTcp ? "\"TCP\",\"" : "\"UDP\",\"") + sock.Hostname + "\"," +
                                                     sock.Port);
                    if (reply.ToLower().IndexOf("dns fail") != -1)
                        success = false; // a retriable failure
                    else if (reply.IndexOf(ConnectReply) == -1) // Some other unexpected response
                        throw new FailedExpectException(SessionStartCommand, ConnectReply, reply);
                    if (!success)
                        Thread.Sleep(500);
                } while (--retries > 0 && !success);
                if (retries == 0 && !success)
                {
                    if (reply.IndexOf(ConnectReply) == -1)
                        throw new DnsLookupFailedException(sock.Hostname);
                    throw new FailedExpectException(SessionStartCommand, ConnectReply, reply);
                }
                reply = reply.Substring(0, reply.IndexOf(','));
                if (int.Parse(reply) != socket)
                    throw new Exception("Unexpected socket response");
                return sock;
            }
        }

        internal void DeleteSocket(int socket)
        {
            EnsureInitialized();
            lock (_sockets)
            {
                if (socket >= 0 && socket <= _sockets.Length)
                {
                    _sockets[socket] = null;
                }
            }
        }

        internal void CloseSocket(int socket)
        {
            EnsureInitialized();
            lock (_oplock)
            {
                if (socket >= 0 && socket <= _sockets.Length)
                {
                    _esp.SendAndExpect(SessionEndCommand + socket, OK);
                }
            }
        }

        internal void SendPayload(int iSocket, byte[] payload)
        {
            EnsureInitialized();
            lock (_oplock)
            {
                _esp.SendAndExpect(SendCommand + iSocket + ',' + payload.Length, OK);
                _esp.Write(payload);
                _esp.Find(SendCommandReply);
            }
        }

        public void SetPower(bool state)
        {
            _powerPin.Write(state);
        }

        private IPAddress _address = IPAddress.Parse("0.0.0.0");

        public IPAddress IPAddress
        {
            get {  return _address; }
            private set { _address = value; }
        }

        public ManualResetEvent IsInitializedEvent { get {  return _isInitializedEvent; } }

        public string[] Version { get; private set; }

        public string MacAddress { get; private set; }

        public AccessPoint[] GetAccessPoints()
        {
            ArrayList result = new ArrayList();

            EnsureInitialized();
            lock (_oplock)
            {
                var response = _esp.SendAndReadUntil(ListAccessPointsCommand, OK);
                foreach (var line in response)
                {
                    var info = Unquote(line.Substring(line.IndexOf(':') + 1));
                    var tokens = info.Split(',');
                    if (tokens.Length >= 4)
                    {
                        var ecn = (Ecn) byte.Parse(tokens[0]);
                        var ssid = tokens[1];
                        var rssi = int.Parse(tokens[2]);
                        var mac = tokens[3];
                        bool mode = false;
                        if (tokens.Length >= 5)
                            mode = int.Parse(tokens[4]) != 0;
                        result.Add(new AccessPoint(ecn, ssid, rssi, mac, mode));
                    }
                }
            }
            return (AccessPoint[])result.ToArray(typeof(AccessPoint));
        }

        private void OnDataReceived(object sender, byte[] stream, int channel)
        {
            if (_sockets[channel] != null)
            {
                // this comes in on a thread-pool thread or private thread, so no need to background it here
                _sockets[channel].ReceivedData(stream);
            }
        }

        private void OnSocketClosed(object sender, int channel)
        {
            if (_sockets[channel] != null)
            {
                //REVIEW: would be more efficient to use the AtProtocolClient's event queue and not spin up new threads
                new Thread(() =>
                {
                    _sockets[channel].SocketClosedByPeer();
                }).Start();
            }
        }

        private void BackgroundInitialize()
        {
            lock (_oplock)
            {
                bool success = false;
                do
                {
                    while (true)
                    {
                        if (!_powerPin.Read())
                        {
                            Thread.Sleep(2000);
                            SetPower(true);
                            Thread.Sleep(2000);
                        }

                        bool pingSuccess = false;
                        int pingRetries = 10;
                        do
                        {
                            try
                            {
                                _esp.SendAndExpect(AT, OK, 1000);
                                pingSuccess = true;
                            }
                            catch (FailedExpectException)
                            {
                                Thread.Sleep(1000);
                            }
                            catch (Exception)
                            {
                            }
                        } while (--pingRetries > 0 && !pingSuccess);
                        // if after 10 retries, we're getting nowhere, then cycle the power
                        if (pingSuccess)
                            break;
                        SetPower(false);
                    }

                    success = false;
                    try
                    {
                        _esp.SendAndExpect(EchoOffCommand, OK, 2000);

                        SetMuxMode(true);

                        // Get the firmware version information
                        this.Version = _esp.SendAndReadUntil(GetFirmwareVersionCommand, OK);

                        // Collect the current IP address information
                        GetAddressInformation();

                        _isInitializedEvent.Set();
                        success = true;
                        if (this.Booted != null)
                            this.Booted(this, new EventArgs());

                    }
                    catch (Exception)
                    {
                        success = false;
                    }
                } while (!success);
            }
        }

        private void EnsureInitialized()
        {
            _isInitializedEvent.WaitOne();
        }

        private void GetAddressInformation()
        {
            // private - no oplock required - always called from within oplock
            var info = _esp.SendAndReadUntil("AT+CIFSR", OK);
            foreach (var line in info)
            {
                if (line.IndexOf("STAIP") != -1)
                {
                    var arg = Unquote(line.Substring(line.IndexOf(',')+1));
                    this.IPAddress = IPAddress.Parse(arg);
                }
                else if (line.IndexOf("STAMAC") != -1)
                {
                    var arg = Unquote(line.Substring(line.IndexOf(',')+1));
                    this.MacAddress = arg;
                }
            }
        }

        private void SetMuxMode(bool enableMux)
        {
            lock (_oplock)
            {
                _esp.SendAndExpect(SetMuxModeCommand + (enableMux ? '1' : '0'), OK);
            }
        }

        private string Unquote(string quotedString)
        {
            quotedString = quotedString.Trim();
            var quoteChar = quotedString[0];
            if (quoteChar != '\'' && quoteChar != '"' && quoteChar != '(')
                return quotedString;
            if (quoteChar == '(')
                quoteChar = ')';
            if (quotedString.LastIndexOf(quoteChar) != quotedString.Length - 1)
                return quotedString;
            quotedString = quotedString.Substring(1);
            quotedString = quotedString.Substring(0, quotedString.Length - 1);
            return /* the now unquoted */ quotedString;
        }
    }
}
