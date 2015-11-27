using System;
using System.Collections;
using Microsoft.SPOT;
using Microsoft.SPOT.Hardware;
using System.IO.Ports;
using System.Net;
using System.Threading;

using PervasiveDigital.Net;
using PervasiveDigital.Utilities;

namespace PervasiveDigital.Hardware.ESP8266
{
    public class Esp8266WifiDevice : IWifiAdapter, IDisposable
    {
        // The amount of time that we will search for 'OK' in response to joining an AP
        public const int JoinTimeout = 30000;

        public const string AT = "AT";
        public const string OK = "OK";
        public const string EchoOffCommand = "ATE0";
        public const string ResetCommand = "AT+RST";
        public const string GetFirmwareVersionCommand = "AT+GMR";
        public const string SetOperatingModeCommand = "AT+CWMODE=";
        public const string GetOperatingModeCommand = "AT+CWMODE?";
        public const string GetOperatingModeResponse = "+CWMODE:";
        public const string SetDhcpMode = "AT+CWDHCP=";
        public const string SetAccessPointModeCommand = "AT+CWSAP=";
        public const string GetAccessPointModeCommand = "AT+CWSAP?";
        public const string GetAccessPointModeResponse = "+CWSAP:";
        public const string GetAddressInformationCommand = "AT+CIFSR";
        public const string SetStationAddressCommand = "AT+CIPSTA=";
        public const string GetStationAddressCommand = "AT+CIPSTA?";
        public const string GetStationAddressResponse = "+CIPSTA:";
        public const string SetApAddressCommand = "AT+CIPAP=";
        public const string GetApAddressCommand = "AT+CIPAP?";
        public const string GetApAddressResponse = "+CIPAP:";
        public const string GetStationMacAddress = "AT+CIPSTAMAC?";
        public const string GetStationMacAddressResponse = "+CIPSTAMAC:";
        public const string SetStationMacAddress = "AT+CIPSTAMAC=";
        public const string GetApMacAddress = "AT+CIPAPMAC?";
        public const string GetApMacAddressResponse = "+CIPAPMAC:";
        public const string SetApMacAddress = "AT+CIPAPMAC=";
        public const string ListAccessPointsCommand = "AT+CWLAP";
        public const string JoinAccessPointCommand = "AT+CWJAP=";
        public const string QuitAccessPointCommand = "AT+CWQAP";
        public const string ListConnectedClientsCommand = "AT+CWLIF";
        public const string SleepCommand = "AT+GSLP=";
        public const string SetMuxModeCommand = "AT+CIPMUX=";
        public const string SessionStartCommand = "AT+CIPSTART=";
        public const string SessionEndCommand = "AT+CIPCLOSE=";
        public const string ServerCommand = "AT+CIPSERVER=";
        public const string UpdateCommand = "AT+CIUPDATE";
        public const string LinkedReply = "Linked";
        public const string SendCommand = "AT+CIPSEND=";
        public const string SendCommandReply = "SEND OK";
        public const string ConnectReply = "CONNECT";
        public const string ErrorReply = "ERROR";

        public delegate void WifiBootedEventHandler(object sender, EventArgs args);
        public delegate void WifiErrorEventHandler(object sender, EventArgs args);
        public delegate void WifiConnectionStateEventHandler(object sender, EventArgs args);
        public delegate void ServerConnectionOpenedHandler(object sender, WifiSocket socket);
        public delegate void ProgressCallback(string progress);

        private readonly ManualResetEvent _isInitializedEvent = new ManualResetEvent(false);
        private readonly WifiSocket[] _sockets = new WifiSocket[4];
        private ServerConnectionOpenedHandler _onServerConnectionOpenedHandler;
        private int _inboundPort = -1;
        private Esp8266Serial _esp;
        private int _lastSocketUsed = 0;
        private bool _enableDebugOutput;
        private bool _enableVerboseOutput;

        private IPAddress _stationAddress = IPAddress.Parse("0.0.0.0");
        private IPAddress _stationGateway = IPAddress.Parse("0.0.0.0");
        private IPAddress _stationNetmask = IPAddress.Parse("0.0.0.0");
        private IPAddress _apAddress = IPAddress.Parse("0.0.0.0");
        private IPAddress _apGateway = IPAddress.Parse("0.0.0.0");
        private IPAddress _apNetmask = IPAddress.Parse("0.0.0.0");
        private string _stationMacAddress = "";
        private string _apMacAddress = "";

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
            _esp.SocketOpened += _esp_SocketOpened;
            _esp.Start();
            ThreadPool.QueueUserWorkItem(BackgroundInitialize);
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

        public void Reset(bool force)
        {
            if (!force)
                EnsureInitialized();
            lock (_oplock)
            {
                int retries = 0;
                while (true)
                {
                    _esp.SendAndReadUntil(ResetCommand, OK);
                    try
                    {
                        _esp.Find("ready", 20000);
                        break;
                    }
                    catch
                    {
                        if (++retries > 3)
                            throw;
                    }
                }
                BackgroundInitialize(null);
            }
        }

        /// <summary>
        /// Perform an over-the-air update.  You must bridge ESP8266 GPIO0 to ground (after boot-up and before calling this fn).
        /// You also need at least 8Mb of memory on your ESP8266.
        /// </summary>
        /// <param name="callback"></param>
        public void Update(ProgressCallback callback)
        {
            EnsureInitialized();
            lock(_oplock)
            {
                _esp.SendCommand(UpdateCommand);
                while (true)
                {
                    // Use a very long timeout - this can take a while - currently, a five minute timeout, 
                    //   but you really don't want to time-out this call and maybe cause the user to do 
                    //   something silly like power down the chip while it is updating.
                    var reply = _esp.GetReplyWithTimeout(300000);
                    if (reply == OK)
                        break;
                    else
                    {
                        if (callback!=null)
                            callback(reply);
                        if (reply == ErrorReply)
                        {
                            throw new ErrorException(UpdateCommand);
                        }
                    }
                }
                Reset(false);
                BackgroundInitialize(null);
            }
        }

        /// <summary>
        /// Connect to an access point
        /// </summary>
        /// <param name="ssid">The SSID of the access point that you wish to connect to</param>
        /// <param name="password">The password for the access point that you wish to connect to</param>
        public void Connect(string ssid, string password)
        {
            EnsureInitialized();
            lock (_oplock)
            {
                var info = _esp.SendAndReadUntil(JoinAccessPointCommand + '"' + ssid + "\",\"" + password + '"', OK, JoinTimeout);
                // We are going to ignore the returned address data (which varies for different firmware) and request address data from the chip in the property accessors
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

        public void CreateServer(int port, ServerConnectionOpenedHandler onServerConnectionOpenedHandler)
        {
            EnsureInitialized();
            lock (_oplock)
            {
                _esp.SendAndExpect(ServerCommand + "1," + port, OK);
                _inboundPort = port;
                _onServerConnectionOpenedHandler = onServerConnectionOpenedHandler;
            }
        }

        public void DeleteServer()
        {
            EnsureInitialized();

            Reset(false);
        }

        /// <summary>
        /// Enter power-saving deep-sleep mode for <paramref name="timeInMs"/> milliseconds.
        /// Note that for wake-up to work, your hardware has to support deep-sleep wake up 
        /// by connecting XPD_DCDC to EXT_RSTB with a zero-ohm resistor.
        /// </summary>
        /// <param name="timeInMs"></param>
        public void Sleep(int timeInMs)
        {
            EnsureInitialized();
            lock (_oplock)
            {
                _esp.SendAndExpect(SleepCommand + timeInMs.ToString(), OK);
            }
        }

        public OperatingMode Mode
        {
            get
            {
                EnsureInitialized();
                OperatingMode result = OperatingMode.Unknown;
                lock (_oplock)
                {
                    var info = _esp.SendAndReadUntil(GetOperatingModeCommand, OK);
                    foreach (var line in info)
                    {
                        if (line.IndexOf(GetOperatingModeResponse)==0)
                        {
                            var arg = Unquote(line.Substring(line.IndexOf(':') + 1));
                            switch (arg.Trim())
                            {
                                case "1":
                                    result = OperatingMode.Station;
                                    break;
                                case "2":
                                    result = OperatingMode.AccessPoint;
                                    break;
                                case "3":
                                    result = OperatingMode.Both;
                                    break;
                            }
                        }
                    }
                }
                return result;
            }
            set
            {
                if (value == OperatingMode.Unknown)
                    throw new ArgumentException("Invalid value");
                EnsureInitialized();
                lock (_oplock)
                {
                    int arg = -1;
                    switch (value)
                    {
                        case OperatingMode.Station:
                            arg = 1;
                            break;
                        case OperatingMode.AccessPoint:
                            arg = 2;
                            break;
                        case OperatingMode.Both:
                            arg = 3;
                            break;
                    }
                    _esp.SendAndExpect(SetOperatingModeCommand + arg, OK);
                    // Reset the chip
                    Reset(false);
                }
            }
        }

        /// <summary>
        /// When you want to use your ESP8266 as an access point (softAP mode) then use
        /// this method to set the ssid and password that your access point will use
        /// when authenticating new clients.
        /// </summary>
        /// <param name="ssid">The ssid that will be advertised to potential clients</param>
        /// <param name="password">The password that a new client must provide. NOTE: MUST BE NUMERIC</param>
        /// <param name="channel">The radio channel that your access point will use. Avoid conflicts with other nearby access points</param>
        /// <param name="ecn">The security mode(s) supported by your access point.  If you use Open, then no password will be required for new clients.</param>
        public void ConfigureAccessPoint(string ssid, string password, int channel, Ecn ecn)
        {
            if (ecn == Ecn.Unknown || ecn == Ecn.WEP)
                throw new ArgumentException("Invalid value", "ecn");
            EnsureInitialized();
            lock (_oplock)
            {
                _esp.SendAndExpect(SetAccessPointModeCommand + 
                    '"' + ssid + "\"," +
                    '"' + password + "\"," + 
                    channel + "," + 
                    (int)ecn, OK);
            }
        }

        public string AccessPointSsid
        {
            get
            {
                EnsureInitialized();
                lock (_oplock)
                {
                    var info = _esp.SendAndReadUntil(GetAccessPointModeCommand, OK);
                    foreach (var line in info)
                    {
                        if (line.IndexOf(GetAccessPointModeResponse) == 0)
                        {
                            var tokens = line.Substring(line.IndexOf(':')).Split(',');
                            return Unquote(tokens[0]);
                        }
                    }
                }
                return null;
            }
        }

        public string AccessPointPassword
        {
            get
            {
                EnsureInitialized();
                lock (_oplock)
                {
                    var info = _esp.SendAndReadUntil(GetAccessPointModeCommand, OK);
                    foreach (var line in info)
                    {
                        if (line.IndexOf(GetAccessPointModeResponse) == 0)
                        {
                            var tokens = line.Substring(line.IndexOf(':')).Split(',');
                            return Unquote(tokens[1]);
                        }
                    }
                }
                return null;
            }
        }

        public int AccessPointChannel
        {
            get
            {
                EnsureInitialized();
                lock (_oplock)
                {
                    var info = _esp.SendAndReadUntil(GetAccessPointModeCommand, OK);
                    foreach (var line in info)
                    {
                        if (line.IndexOf(GetAccessPointModeResponse) == 0)
                        {
                            var tokens = line.Substring(line.IndexOf(':')).Split(',');
                            return int.Parse(tokens[2]);
                        }
                    }
                }
                return -1;
            }
        }

        public Ecn AccessPointEcn
        {
            get
            {
                EnsureInitialized();
                lock (_oplock)
                {
                    var info = _esp.SendAndReadUntil(GetAccessPointModeCommand, OK);
                    foreach (var line in info)
                    {
                        if (line.IndexOf(GetAccessPointModeResponse) == 0)
                        {
                            var tokens = line.Substring(line.IndexOf(':')).Split(',');
                            return (Ecn)int.Parse(tokens[3]);
                        }
                    }
                }
                return Ecn.Unknown;
            }
        }

        public void EnableDhcp(OperatingMode mode, bool enable)
        {
            if (mode == OperatingMode.Unknown)
                throw new ArgumentException("Invalid value","mode");
            EnsureInitialized();
            lock (_oplock)
            {
                int arg = -1;
                switch (mode)
                {
                    case OperatingMode.Station:
                        arg = 1;
                        break;
                    case OperatingMode.AccessPoint:
                        arg = 0;
                        break;
                    case OperatingMode.Both:
                        arg = 2;
                        break;

                }
                _esp.SendAndExpect(SetDhcpMode + arg + ',' + (enable ? '1' : '0'), OK);
            }
        }

        public ISocket OpenSocket(string hostNameOrAddress, int portNumber, bool useTcp)
        {
            EnsureInitialized();
            lock (_oplock)
            {
                int iSocket = -1;
                // lastSocketUsed is used to make sure that we don't reuse a just-released socket too quickly
                // It can still happen, but this reduces the probability of it happening if you are using less than five sockets in quick succession.
                // The chip seems to get upset if we reuse a socket immediately after closing it.
                for (int i = _lastSocketUsed ; i < _sockets.Length; ++i)
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
                _lastSocketUsed = iSocket;

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
                    var command = SessionStartCommand + socket + ',' +
                                                     (sock.UseTcp ? "\"TCP\",\"" : "\"UDP\",\"") + sock.Hostname + "\"," +
                                                     sock.Port;
                    reply = _esp.SendCommandAndReadReply(command);
                    if (reply.ToLower().IndexOf("dns fail") != -1)
                    {
                        success = false; // a retriable failure
                    }
                    else if (reply.IndexOf(ConnectReply) == -1) // Some other unexpected response
                    {
                        if (reply.IndexOf(ErrorReply) == 0)
                            throw new ErrorException(command);
                        else
                            throw new FailedExpectException(SessionStartCommand, ConnectReply, reply);
                    }
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
            if (socket >= 0 && socket <= _sockets.Length)
            {
                _sockets[socket] = null;
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
            if (_powerPin == null)
                return;

            lock (_oplock)
            {
                _powerPin.Write(state);
                // if the power just came back on, we need to re-init
                if (state)
                {
                    Thread.Sleep(500);
                    _isInitializedEvent.Reset();
                    BackgroundInitialize(null);
                }
            }
        }

        public IPAddress StationIPAddress
        {
            get 
            {
                EnsureInitialized();
                lock (_oplock)
                {
                    GetStationAddressInfo();
                }
                return _stationAddress; 
            }
            set
            {
                EnsureInitialized();
                lock (_oplock)
                {
                    _esp.SendAndExpect(SetStationAddressCommand + '"' + value.ToString() + '"', OK);
                }
            }
        }

        public IPAddress StationGateway
        {
            get { return _stationGateway; }
        }

        public IPAddress StationNetmask
        {
            get { return _stationNetmask; }
        }

        public IPAddress AccessPointIPAddress
        {
            get 
            {
                EnsureInitialized();
                lock (_oplock)
                {
                    GetApAddressInfo();
                }
                return _apAddress; 
            }
            set
            {
                EnsureInitialized();
                lock (_oplock)
                {
                    _esp.SendAndExpect(SetApAddressCommand + '"' + value.ToString() + '"', OK);
                }
            }
        }

        public IPAddress AccessPointGateway
        {
            get { return _apGateway; }
        }

        public IPAddress AccessPointNetmask
        {
            get { return _apNetmask; }
        }


        public string[] Version { get; private set; }

        public string AtProtocolVersion
        {
            get
            {
                foreach (var line in this.Version)
                {
                    if (line.StartsWith("AT version:"))
                    {
                        return line.Substring(line.IndexOf(':'));
                    }
                }
                return null;
            }
        }

        public string SdkVersion
        {
            get
            {
                foreach (var line in this.Version)
                {
                    if (line.StartsWith("SDK version:"))
                    {
                        return line.Substring(line.IndexOf(':'));
                    }
                }
                return null;
            }
        }

        public string CompileTime
        {
            get
            {
                foreach (var line in this.Version)
                {
                    if (line.StartsWith("compile time:"))
                    {
                        return line.Substring(line.IndexOf(':'));
                    }
                }
                return null;
            }
        }

        public string StationMacAddress
        {
            get
            {
                EnsureInitialized();
                lock (_oplock)
                {
                    var info = _esp.SendAndReadUntil(GetStationMacAddress, OK);
                    foreach (var line in info)
                    {
                        ParseAddressInfo(line);
                    }
                }
                return _stationMacAddress;
            }
            set
            {
                EnsureInitialized();
                lock (_oplock)
                {
                    _esp.SendAndExpect(SetStationMacAddress + '"' + value + '"', OK);
                }
            }
        }

        public string AccessPointMacAddress
        {
            get
            {
                EnsureInitialized();
                lock (_oplock)
                {
                    var info = _esp.SendAndReadUntil(GetApMacAddress, OK);
                    foreach (var line in info)
                    {
                        ParseAddressInfo(line);
                    }
                }
                return _apMacAddress;
            }
            set
            {
                EnsureInitialized();
                lock (_oplock)
                {
                    _esp.SendAndExpect(SetApMacAddress + '"' + value + '"', OK);
                }
            }
        }

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

        public AccessPointClient[] GetConnectedClients()
        {
            ArrayList result = new ArrayList();

            EnsureInitialized();
            lock (_oplock)
            {
                var response = _esp.SendAndReadUntil(ListConnectedClientsCommand, OK);
                foreach (var line in response)
                {
                    if (line != null && line.Length > 0)
                    {
                        result.Add(line);
                        var tokens = line.Split(',');
                        if (tokens.Length > 1)
                        {
                            var addr = IPAddress.Parse(tokens[0]);
                            var mac = tokens[1].Trim();
                            result.Add(new AccessPointClient(addr, mac));
                        }
                    }
                }
            }
            return (AccessPointClient[])result.ToArray(typeof(AccessPointClient));
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
                ThreadPool.QueueUserWorkItem((state) =>
                    {
                        ((WifiSocket)state).SocketClosedByPeer();
                    }, _sockets[channel]);
            }
        }

        void _esp_SocketOpened(object sender, int channel, out bool fHandled)
        {
            // This could be the result of an outgoing or incoming socket connection

            if (_onServerConnectionOpenedHandler==null || _sockets[channel]!=null)
            {
                fHandled = false;
                return;
            }

            // Create a socket object - TODO: get tcp and port information
            var socket = new WifiSocket(this, channel, _inboundPort);
            _sockets[channel] = socket;

            // Fire connection-received event so that the app knows there is a new socket to service
            if (_onServerConnectionOpenedHandler != null)
            {
                ThreadPool.QueueUserWorkItem((state) =>
                {
                    _onServerConnectionOpenedHandler(this, ((WifiSocket)state));
                }, socket);
            }

            fHandled = false;
        }

        private void BackgroundInitialize(object unused)
        {
            lock (_oplock)
            {
                bool success = false;
                do
                {
                    while (true)
                    {
                        if (_powerPin!=null && !_powerPin.Read())
                        {
                            Thread.Sleep(2000);
                            // Don't use SetPower - it will trigger a recursion into BackgroundInitialize
                            _powerPin.Write(true);
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
                        if (_powerPin!=null)
                            _powerPin.Write(false);
                    }

                    success = false;
                    try
                    {
                        _esp.SendAndExpect(EchoOffCommand, OK, 2000);

                        SetMuxMode(true);

                        // Get the firmware version information
                        this.Version = _esp.SendAndReadUntil(GetFirmwareVersionCommand, OK);

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

        private void GetStationAddressInfo()
        {
            var info = _esp.SendAndReadUntil(GetStationAddressCommand, OK);
            foreach (var line in info)
            {
                ParseAddressInfo(line);
            }
        }

        private void GetApAddressInfo()
        {
            var info = _esp.SendAndReadUntil(GetApAddressCommand, OK);
            foreach (var line in info)
            {
                ParseAddressInfo(line);
            }
        }

        private bool ParseAddressInfo(string line)
        {
            var matched = false;

            if (line.IndexOf(GetStationAddressResponse) == 0)
            {
                var tokens = line.Split(':');
                if (tokens.Length == 3)
                {
                    switch (tokens[1].Trim().ToLower())
                    {
                        case "ip":
                            _stationAddress = IPAddress.Parse(Unquote(tokens[2]));
                            break;
                        case "gateway":
                            _stationGateway = IPAddress.Parse(Unquote(tokens[2]));
                            break;
                        case "netmask":
                            _stationNetmask = IPAddress.Parse(Unquote(tokens[2]));
                            break;
                    }
                }
            }
            else if (line.IndexOf(GetApAddressResponse) == 0)
            {
                var tokens = line.Split(':');
                if (tokens.Length == 3)
                {
                    switch (tokens[1].Trim().ToLower())
                    {
                        case "ip":
                            _apAddress = IPAddress.Parse(Unquote(tokens[2]));
                            break;
                        case "gateway":
                            _apGateway = IPAddress.Parse(Unquote(tokens[2]));
                            break;
                        case "netmask":
                            _apNetmask = IPAddress.Parse(Unquote(tokens[2]));
                            break;
                    }
                }
            }
            else if (line.IndexOf(GetStationMacAddressResponse) == 0)
            {
                var arg = Unquote(line.Substring(line.IndexOf(':') + 1));
                _stationMacAddress = arg;
            }
            else if (line.IndexOf(GetApMacAddressResponse) == 0)
            {
                var arg = Unquote(line.Substring(line.IndexOf(':') + 1));
                _apMacAddress = arg;
            }
            else if (line.IndexOf("STAIP") != -1)
            {
                var arg = Unquote(line.Substring(line.IndexOf(',') + 1));
                _stationAddress = IPAddress.Parse(arg);
                matched = true;
            }
            else if (line.IndexOf("STAMAC") != -1)
            {
                var arg = Unquote(line.Substring(line.IndexOf(',') + 1));
                _stationMacAddress = arg;
                matched = true;
            }
            else if (line.IndexOf("APIP") != -1)
            {
                var arg = Unquote(line.Substring(line.IndexOf(',') + 1));
                _apAddress = IPAddress.Parse(arg);
                matched = true;
            }
            else if (line.IndexOf("APMAC") != -1)
            {
                var arg = Unquote(line.Substring(line.IndexOf(',') + 1));
                _apMacAddress = arg;
                matched = true;
            }
            return matched;
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
