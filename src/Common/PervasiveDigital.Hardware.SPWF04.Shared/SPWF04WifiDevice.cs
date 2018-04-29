using System;
using System.Collections;
using Microsoft.SPOT;
using Microsoft.SPOT.Hardware;
using System.IO.Ports;
using System.Net;
using System.Threading;

using PervasiveDigital.Net;
using PervasiveDigital.Utilities;

namespace PervasiveDigital.Hardware.SPWF04
{
    public class Spwf04WifiDevice : IWifiAdapter, IDisposable
    {
        // The amount of time that we will search for 'OK' in response to joining an AP
        public const int JoinTimeout = 30000;

        private const string AT = "AT";
        private const string OK = "AT-S.OK";
        private const string ErrorReply = "ERROR";
        private const string ConnectReply = "CONNECT";

        public enum Commands
        {
            // AT Commands
            ConfigCommand,
            EnableWifiCommand,
            SetSsid,

            // Config values
            ConsoleEcho,
            WifiPrivacyMode,
            WpaPskPassword,

            ResetCommand,
            RestoreCommand,
            GetFirmwareVersionCommand,
            SetOperatingModeCommand,
            GetOperatingModeCommand,
            GetOperatingModeResponse,
            SetDhcpMode,
            SetAccessPointModeCommand,
            GetAccessPointModeCommand,
            GetAccessPointModeResponse,
            GetAddressInformationCommand,
            SetStationAddressCommand,
            GetStationAddressCommand,
            GetStationAddressResponse,
            SetApAddressCommand,
            GetApAddressCommand,
            GetApAddressResponse,
            GetStationMacAddress,
            GetStationMacAddressResponse,
            SetStationMacAddress,
            GetApMacAddress,
            GetApMacAddressResponse,
            SetApMacAddress,
            ListAccessPointsCommand,
            JoinAccessPointCommand,
            QuitAccessPointCommand,
            ListConnectedClientsCommand,
            SleepCommand,
            SessionStartCommand,
            SessionEndCommand,
            ServerCommand,
            UpdateCommand,
            LinkedReply,
            SendCommand,
            SendCommandReply,
            ConnectReply,
        }

        private Hashtable _commandSet = new Hashtable();
        public delegate void WifiBootedEventHandler(object sender, EventArgs args);
        public delegate void WifiErrorEventHandler(object sender, EventArgs args);
        public delegate void WifiConnectionStateEventHandler(object sender, bool connected);
        public delegate void ServerConnectionOpenedHandler(object sender, WifiSocket socket);
        public delegate void ProgressCallback(string progress);

        private readonly ManualResetEvent _isInitializedEvent = new ManualResetEvent(false);
        private readonly WifiSocket[] _sockets = new WifiSocket[4];
        private ServerConnectionOpenedHandler _onServerConnectionOpenedHandler;
        private int _inboundPort = -1;
        private Spwf04Serial _device;
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
        public event HardwareFaultHandler HardwareFault;
        //public event WifiErrorEventHandler Error;
        public event WifiConnectionStateEventHandler WifiConnectionStateChanged;
        public event IndicationReceivedHandler IndicationReceived;
        private OutputPort _resetPin = null;

        public Spwf04WifiDevice(SerialPort port, OutputPort resetPin)
        {
            _resetPin = resetPin;
            Initialize(port);
        }

        private void Initialize(SerialPort port)
        {
            _device = new Spwf04Serial(port);
            _device.DataReceived += OnDataReceived;
            _device.SocketClosed += OnSocketClosed;
            _device.SocketOpened += OnSocketOpened;
            _device.IndicationReceived += OnIndicationReceived;
            _device.Fault += OnFault;
            _device.Start();
            ThreadPool.QueueUserWorkItem(BackgroundInitialize);
        }

        private void OnIndicationReceived(object sender, int code, string details)
        {
            switch (code)
            {
                case 24:
                case 38:
                    if (this.WifiConnectionStateChanged != null)
                        this.WifiConnectionStateChanged(this, code == 24);  //24==up, 38==down
                    break;
            }
            if (this.IndicationReceived != null)
                this.IndicationReceived(this, code, details);
        }

        private void OnFault(object sender, int cause)
        {
            if (this.HardwareFault != null)
                this.HardwareFault(this, cause);
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
                _device.EnableDebugOutput = value;
            }
        }

        public bool EnableVerboseOutput
        {
            get { return _enableVerboseOutput; }
            set
            {
                _enableVerboseOutput = value;
                _device.EnableVerboseOutput = value;
            }
        }

        // Use this to make sure no one else interrupts your sequence of interactions with the esp hardware block.
        public object OperationLock
        {
            get { return _oplock; }
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
                    _device.SendAndReadUntil(Command(Commands.ResetCommand), OK);
                    try
                    {
                        _device.Find("ready", 20000);
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

        public void Restore()
        {
            EnsureInitialized();
            lock (_oplock)
            {
                _device.SendAndReadUntil(Command(Commands.RestoreCommand), OK);
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
            lock (_oplock)
            {
                _device.SendCommand(Command(Commands.UpdateCommand));
                while (true)
                {
                    // Use a very long timeout - this can take a while - currently, a five minute timeout, 
                    //   but you really don't want to time-out this call and maybe cause the user to do 
                    //   something silly like power down the chip while it is updating.
                    var reply = _device.GetReplyWithTimeout(300000);
                    if (reply == OK)
                        break;
                    else
                    {
                        if (callback != null)
                            callback(reply);
                        if (reply == ErrorReply)
                        {
                            throw new ErrorException(Command(Commands.UpdateCommand));
                        }
                    }
                }
                Reset(false);
                BackgroundInitialize(null);
            }
        }

        public void EnableWifi(bool fEnable)
        {
            EnsureInitialized();
            lock (_oplock)
            {
                // The radio will respond with error 17 if you try to turn it on when it is already on
                // and with error 18 if you try to turn it off when it is already off.
                _device.SendAndExpect(Command(Commands.EnableWifiCommand) + (fEnable ? "1" : "0"), new[] { OK, ErrorCode(17), ErrorCode(18) });
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
                _device.SendAndExpect(Command(Commands.SetSsid) + ssid, OK);
                if (!StringUtilities.IsNullOrEmpty(password))
                    Configure(Commands.WpaPskPassword, password);
            }
        }

        public void SetPrivacyMode(int mode)
        {
            EnsureInitialized();
            lock (_oplock)
            {
                Configure(Commands.WifiPrivacyMode, mode.ToString());
            }
        }

        public void Disconnect()
        {
            EnsureInitialized();
            lock (_oplock)
            {
                _device.SendAndExpect(Command(Commands.QuitAccessPointCommand), OK);
            }
        }

        public void CreateServer(int port, ServerConnectionOpenedHandler onServerConnectionOpenedHandler)
        {
            EnsureInitialized();
            lock (_oplock)
            {
                _device.SendAndExpect(Command(Commands.ServerCommand) + "1," + port, OK);
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
                _device.SendAndExpect(Command(Commands.SleepCommand) + timeInMs.ToString(), OK);
            }
        }

        public OperatingMode Mode
        {
            get { return GetOperatingMode(); }
        }

        public OperatingMode GetOperatingMode()
        {
            EnsureInitialized();
            OperatingMode result = OperatingMode.Unknown;
            lock (_oplock)
            {
                var info = _device.SendAndReadUntil(Command(Commands.GetOperatingModeCommand), OK);
                foreach (var line in info)
                {
                    if (line.IndexOf(Response(Commands.GetOperatingModeResponse)) == 0)
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

        public void SetOperatingMode(OperatingMode mode, bool persist = false)
        {
            if (mode == OperatingMode.Unknown)
                throw new ArgumentException("Invalid value");
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
                        arg = 2;
                        break;
                    case OperatingMode.Both:
                        arg = 3;
                        break;
                }
                _device.SendAndExpect(Command(Commands.SetOperatingModeCommand, persist) + arg, OK);
                // Reset the chip
                //Reset(false);
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
        public void ConfigureAccessPoint(string ssid, string password, int channel, Ecn ecn, bool persist = false)
        {
            if (ecn == Ecn.Unknown || ecn == Ecn.WEP)
                throw new ArgumentException("Invalid value", "ecn");
            EnsureInitialized();
            lock (_oplock)
            {
                _device.SendAndExpect(Command(Commands.SetAccessPointModeCommand, persist) +
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
                    var info = _device.SendAndReadUntil(Command(Commands.GetAccessPointModeCommand), OK);
                    foreach (var line in info)
                    {
                        if (line.IndexOf(Response(Commands.GetAccessPointModeResponse)) == 0)
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
                    var info = _device.SendAndReadUntil(Command(Commands.GetAccessPointModeCommand), OK);
                    foreach (var line in info)
                    {
                        if (line.IndexOf(Response(Commands.GetAccessPointModeResponse)) == 0)
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
                    var info = _device.SendAndReadUntil(Command(Commands.GetAccessPointModeCommand), OK);
                    foreach (var line in info)
                    {
                        if (line.IndexOf(Response(Commands.GetAccessPointModeResponse)) == 0)
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
                    var info = _device.SendAndReadUntil(Command(Commands.GetAccessPointModeCommand), OK);
                    foreach (var line in info)
                    {
                        if (line.IndexOf(Response(Commands.GetAccessPointModeResponse)) == 0)
                        {
                            var tokens = line.Substring(line.IndexOf(':')).Split(',');
                            return (Ecn)int.Parse(tokens[3]);
                        }
                    }
                }
                return Ecn.Unknown;
            }
        }

        public void EnableDhcp(OperatingMode mode, bool enable, bool persist = false)
        {
            if (mode == OperatingMode.Unknown)
                throw new ArgumentException("Invalid value", "mode");
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
                _device.SendAndExpect(Command(Commands.SetDhcpMode, persist) + arg + ',' + (enable ? '1' : '0'), OK);
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
                for (int i = _lastSocketUsed; i < _sockets.Length; ++i)
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
                    var command = Command(Commands.SessionStartCommand) + socket + ',' +
                                                     (sock.UseTcp ? "\"TCP\",\"" : "\"UDP\",\"") + sock.Hostname + "\"," +
                                                     sock.Port;
                    reply = _device.SendCommandAndReadReply(command);
                    if (reply.ToLower().IndexOf("dns fail") != -1)
                    {
                        success = false; // a retriable failure
                    }
                    else if (reply.IndexOf(ConnectReply) == -1) // Some other unexpected response
                    {
                        if (reply.IndexOf(ErrorReply) == 0)
                            throw new ErrorException(command);
                        else
                            throw new FailedExpectException(Command(Commands.SessionStartCommand), new[] { ConnectReply }, reply);
                    }
                    if (!success)
                        Thread.Sleep(500);
                } while (--retries > 0 && !success);
                if (retries == 0 && !success)
                {
                    if (reply.IndexOf(ConnectReply) == -1)
                        throw new DnsLookupFailedException(sock.Hostname);
                    throw new FailedExpectException(Command(Commands.SessionStartCommand), new[] { ConnectReply }, reply);
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
                    _device.SendAndExpect(Command(Commands.SessionEndCommand) + socket, OK);
                }
            }
        }

        internal void SendPayload(int iSocket, byte[] payload)
        {
            EnsureInitialized();
            lock (_oplock)
            {
                _device.SendAndExpect(Command(Commands.SendCommand) + iSocket + ',' + payload.Length, OK);
                _device.Write(payload);
                _device.Find(Response(Commands.SendCommandReply));
            }
        }

        public IPAddress StationIPAddress
        {
            get { return GetStationIPAddress(); }
        }

        public IPAddress GetStationIPAddress()
        {
            EnsureInitialized();
            lock (_oplock)
            {
                GetStationAddressInfo();
            }
            return _stationAddress;
        }

        public void SetStationIPAddress(IPAddress value, bool persist = false)
        {
            EnsureInitialized();
            lock (_oplock)
            {
                _device.SendAndExpect(Command(Commands.SetStationAddressCommand, persist) + '"' + value.ToString() + '"', OK);
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
            get { return GetAccessPointIPAddress(); }
        }

        public IPAddress GetAccessPointIPAddress()
        {
            EnsureInitialized();
            lock (_oplock)
            {
                GetApAddressInfo();
            }
            return _apAddress;
        }

        public void SetAccessPointIPAddress(IPAddress value, bool persist = false)
        {
            EnsureInitialized();
            lock (_oplock)
            {
                _device.SendAndExpect(Command(Commands.SetApAddressCommand, persist) + '"' + value.ToString() + '"', OK);
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
                        return line.Substring(line.IndexOf(':') + 1);
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
                        return line.Substring(line.IndexOf(':') + 1);
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
                        return line.Substring(line.IndexOf(':') + 1);
                    }
                }
                return null;
            }
        }

        public string StationMacAddress
        {
            get { return GetStationMacAddress(); }
        }

        public string GetStationMacAddress()
        {
            EnsureInitialized();
            lock (_oplock)
            {
                var info = _device.SendAndReadUntil(Command(Commands.GetStationMacAddress), OK);
                foreach (var line in info)
                {
                    ParseAddressInfo(line);
                }
            }
            return _stationMacAddress;
        }

        public void SetStationMacAddress(string value, bool persist = false)
        {
            EnsureInitialized();
            lock (_oplock)
            {
                _device.SendAndExpect(Command(Commands.SetStationMacAddress, persist) + '"' + value + '"', OK);
            }
        }

        public string AccessPointMacAddress
        {
            get { return GetAccessPointMacAddress(); }
        }

        public string GetAccessPointMacAddress()
        {
            EnsureInitialized();
            lock (_oplock)
            {
                var info = _device.SendAndReadUntil(Command(Commands.GetApMacAddress), OK);
                foreach (var line in info)
                {
                    ParseAddressInfo(line);
                }
            }
            return _apMacAddress;
        }

        public void SetAccessPointMacAddress(string value, bool persist = false)
        {
            EnsureInitialized();
            lock (_oplock)
            {
                _device.SendAndExpect(Command(Commands.SetApMacAddress, persist) + '"' + value + '"', OK);
            }
        }

        public AccessPoint[] GetAccessPoints()
        {
            ArrayList result = new ArrayList();

            EnsureInitialized();
            lock (_oplock)
            {
                //if (this.Mode != OperatingMode.Station)
                //    throw new Exception("You must be in 'Station' mode to retrieve access points.");

                var response = _device.SendAndReadUntil(Command(Commands.ListAccessPointsCommand), OK);
                foreach (var line in response)
                {
                    var info = Unquote(line.Substring(line.IndexOf(':') + 1));
                    var tokens = info.Split(',');
                    if (tokens.Length >= 4)
                    {
                        var ecn = (Ecn)byte.Parse(tokens[0]);
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
                var response = _device.SendAndReadUntil(Command(Commands.ListConnectedClientsCommand), OK);
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

        void OnSocketOpened(object sender, int channel, out bool fHandled)
        {
            // This could be the result of an outgoing or incoming socket connection

            if (_onServerConnectionOpenedHandler == null || _sockets[channel] != null)
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
                // start with the least common denominator command set
                InitializeCommandSet();

                bool success = false;
                do
                {
                    while (true)
                    {
                        bool pingSuccess = false;
                        int pingRetries = 10;
                        do
                        {
                            try
                            {
                                _device.SendAndExpect(AT, OK, 1000);
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
                        if (pingSuccess)
                            break;
                    }

                    success = false;
                    try
                    {
                        Configure(Commands.ConsoleEcho, "0");

                        //// Get the firmware version information
                        //this.Version = _device.SendAndReadUntil(Command(Commands.GetFirmwareVersionCommand), OK);

                        _isInitializedEvent.Set();
                        success = true;
                        if (this.Booted != null)
                            this.Booted.Invoke(this, new EventArgs());
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

        private void InitializeCommandSet()
        {
            _commandSet[Commands.EnableWifiCommand] = "AT+S.WIFI="; // 0 or 1
            _commandSet[Commands.ConfigCommand] = "AT+S.SCFG=";
            _commandSet[Commands.SetSsid] = "AT+S.SSIDTXT=";

            // config values
            _commandSet[Commands.ConsoleEcho] = "console_echo";
            _commandSet[Commands.WifiPrivacyMode] = "wifi_priv_mode";
            _commandSet[Commands.WpaPskPassword] = "wifi_wpa_psk_text";
            
            //_commandSet40[Commands.EchoOffCommand] = "ATE0";
            //_commandSet40[Commands.ResetCommand] = "AT+RST";
            //_commandSet40[Commands.GetFirmwareVersionCommand] = "AT+GMR";
            //_commandSet40[Commands.SetOperatingModeCommand] = "AT+CWMODE=";
            //_commandSet40[Commands.GetOperatingModeCommand] = "AT+CWMODE?";
            //_commandSet40[Commands.GetOperatingModeResponse] = "+CWMODE:";
            //_commandSet40[Commands.SetDhcpMode] = "AT+CWDHCP=";
            //_commandSet40[Commands.SetAccessPointModeCommand] = "AT+CWSAP=";
            //_commandSet40[Commands.GetAccessPointModeCommand] = "AT+CWSAP?";
            //_commandSet40[Commands.GetAccessPointModeResponse] = "+CWSAP:";
            //_commandSet40[Commands.GetAddressInformationCommand] = "AT+CIFSR";
            //_commandSet40[Commands.SetStationAddressCommand] = "AT+CIPSTA=";
            //_commandSet40[Commands.GetStationAddressCommand] = "AT+CIPSTA?";
            //_commandSet40[Commands.GetStationAddressResponse] = "+CIPSTA:";
            //_commandSet40[Commands.SetApAddressCommand] = "AT+CIPAP=";
            //_commandSet40[Commands.GetApAddressCommand] = "AT+CIPAP?";
            //_commandSet40[Commands.GetApAddressResponse] = "+CIPAP:";
            //_commandSet40[Commands.GetStationMacAddress] = "AT+CIPSTAMAC?";
            //_commandSet40[Commands.GetStationMacAddressResponse] = "+CIPSTAMAC:";
            //_commandSet40[Commands.SetStationMacAddress] = "AT+CIPSTAMAC=";
            //_commandSet40[Commands.GetApMacAddress] = "AT+CIPAPMAC?";
            //_commandSet40[Commands.GetApMacAddressResponse] = "+CIPAPMAC:";
            //_commandSet40[Commands.SetApMacAddress] = "AT+CIPAPMAC=";
            //_commandSet40[Commands.ListAccessPointsCommand] = "AT+CWLAP";
            //_commandSet40[Commands.JoinAccessPointCommand] = "AT+CWJAP=";
            //_commandSet40[Commands.QuitAccessPointCommand] = "AT+CWQAP";
            //_commandSet40[Commands.ListConnectedClientsCommand] = "AT+CWLIF";
            //_commandSet40[Commands.SleepCommand] = "AT+GSLP=";
            //_commandSet40[Commands.SessionStartCommand] = "AT+CIPSTART=";
            //_commandSet40[Commands.SessionEndCommand] = "AT+CIPCLOSE=";
            //_commandSet40[Commands.ServerCommand] = "AT+CIPSERVER=";
            //_commandSet40[Commands.UpdateCommand] = "AT+CIUPDATE";
            //_commandSet40[Commands.LinkedReply] = "Linked";
            //_commandSet40[Commands.SendCommand] = "AT+CIPSEND=";
            //_commandSet40[Commands.SendCommandReply] = "SEND OK";

            //_commandSet51[Commands.RestoreCommand] = new[] { "AT+RESTORE", "AT+RESTORE" };
            //_commandSet51[Commands.SetOperatingModeCommand] = new[] { "AT+CWMODE_CUR=", "AT+CWMODE_DEF=" };
            //_commandSet51[Commands.GetOperatingModeCommand] = new[] { "AT+CWMODE_CUR?", "AT+CWMODE_DEF?" };
            //_commandSet51[Commands.GetOperatingModeResponse] = new[] { "+CWMODE_CUR:", "+CWMODE_DEF:" };
            //_commandSet51[Commands.SetDhcpMode] = new[] { "AT+CWDHCP_CUR=", "AT+CWDHCP_DEF=" };
            //_commandSet51[Commands.GetAccessPointModeCommand] = new[] { "AT+CWSAP_CUR?", "AT+CWSAP_DEF?" };
            //_commandSet51[Commands.SetAccessPointModeCommand] = new[] { "AT+CWSAP_CUR=", "AT+CWSAP_DEF=" };
            //_commandSet51[Commands.GetAccessPointModeResponse] = new[] { "+CWSAP_CUR:", "+CWSAP_DEF:" };
            //_commandSet51[Commands.SetStationAddressCommand] = new[] { "AT+CIPSTA_CUR=", "AT+CIPSTA_DEF=" };
            //_commandSet51[Commands.GetStationAddressCommand] = new[] { "AT+CIPSTA_CUR?", "AT+CIPSTA_DEF?" };
            //_commandSet51[Commands.GetStationAddressResponse] = new[] { "+CIPSTA_CUR:", "+CIPSTA_DEF:" };
            //_commandSet51[Commands.SetApAddressCommand] = new[] { "AT+CIPAP_CUR=", "AT+CIPAP_DEF=" };
            //_commandSet51[Commands.GetApAddressCommand] = new[] { "AT+CIPAP_CUR?", "AT+CIPAP_DEF?" };
            //_commandSet51[Commands.GetApAddressResponse] = new[] { "+CIPAP_CUR:", "+CIPAP_DEF:" };
            //_commandSet51[Commands.SetStationMacAddress] = new[] { "AT+CIPSTAMAC_CUR=", "AT+CIPSTAMAC_DEF=" };
            //_commandSet51[Commands.GetStationMacAddress] = new[] { "AT+CIPSTAMAC_CUR?", "AT+CIPSTAMAC_DEF?" };
            //_commandSet51[Commands.GetStationMacAddressResponse] = new[] { "+CIPSTAMAC_CUR:", "+CIPSTAMAC_DEF:" };
            //_commandSet51[Commands.SetApMacAddress] = new[] { "AT+CIPAPMAC_CUR=", "AT+CIPAPMAC_DEF=" };
            //_commandSet51[Commands.GetApMacAddress] = new[] { "AT+CIPAPMAC_CUR?", "AT+CIPAPMAC_DEF?" };
            //_commandSet51[Commands.GetApMacAddressResponse] = new[] { "+CIPAPMAC_CUR:", "+CIPAPMAC_DEF:" };
            //_commandSet51[Commands.JoinAccessPointCommand] = new[] { "AT+CWJAP_CUR=", "AT+CWJAP_DEF=" };
        }

        private void Configure(Commands configItem, string args)
        {
            lock (_oplock)
            {
                _device.SendAndExpect(Command(Commands.ConfigCommand) + Command(configItem) + "," + args, OK, 2000);
            }
        }

        private string Command(Commands cmd, bool persist = false)
        {
            string result = ((string)_commandSet[cmd]);
            if (result == null)
                throw new Exception("command not supported");
            return result;
        }

        private string ErrorCode(int code)
        {
            return "AT-S.ERROR:" + code + ":";
        }

        private string Response(Commands cmd, bool persist = false)
        {
            string result = ((string)_commandSet[cmd]);
            if (result == null)
                throw new Exception("command not supported");
            return result;
        }

        private void GetStationAddressInfo()
        {
            var info = _device.SendAndReadUntil(Command(Commands.GetStationAddressCommand), OK);
            foreach (var line in info)
            {
                ParseAddressInfo(line);
            }
        }

        private void GetApAddressInfo()
        {
            var info = _device.SendAndReadUntil(Command(Commands.GetApAddressCommand), OK);
            foreach (var line in info)
            {
                ParseAddressInfo(line);
            }
        }

        private bool ParseAddressInfo(string line)
        {
            var matched = false;

            if (line.IndexOf(Response(Commands.GetStationAddressResponse)) == 0)
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
            else if (line.IndexOf(Response(Commands.GetApAddressResponse)) == 0)
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
            else if (line.IndexOf(Response(Commands.GetStationMacAddressResponse)) == 0)
            {
                var arg = Unquote(line.Substring(line.IndexOf(':') + 1));
                _stationMacAddress = arg;
            }
            else if (line.IndexOf(Response(Commands.GetApMacAddressResponse)) == 0)
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
