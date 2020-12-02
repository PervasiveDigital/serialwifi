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

        private const string AT = "AT";
        private const string OK = "OK";
        private const string ErrorReply = "ERROR";
        private const string ConnectReply = "CONNECT";
        private const string FailReply = "FAIL";
        private const string DnsFailReply = "DNS Fail";
        private static readonly string [] InfoReplies = {"WIFI CONNECTED","WIFI DISCONNECT","WIFI GOT IP","no ip","busy s...","busy p...",DnsFailReply};
        private static readonly string[] FailReplies = {FailReply, ErrorReply};

        public enum Commands
        {
           EchoOffCommand,
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
           ListAccessPointsSortCommand,
           JoinAccessPointCommand,
           QuitAccessPointCommand,
           ListConnectedClientsCommand,
           SleepCommand,
           SetMuxModeCommand,
           SessionStartCommand,
           SessionEndCommand,
           ServerCommand,
           UpdateCommand,
           LinkedReply,
           SendCommand,
           SendCommandReply,
           ConnectReply,
        }

        private Hashtable _commandSet40 = new Hashtable();
        private Hashtable _commandSet51 = new Hashtable();
        public delegate void WifiBootedEventHandler(object sender, EventArgs args);
        public delegate void WifiErrorEventHandler(object sender, EventArgs args);
        public delegate void WifiConnectionStateEventHandler(object sender, EventArgs args);
        public delegate void ServerConnectionOpenedHandler(object sender, WifiSocket socket);
        public delegate void ProgressCallback(string progress);
        public delegate void WifiInfoEventHandler(object sender, WifiInfoEventArgs args);

        private readonly ManualResetEvent _isInitializedEvent = new ManualResetEvent(false);
        private readonly WifiSocket[] _sockets = new WifiSocket[4];
        private ServerConnectionOpenedHandler _onServerConnectionOpenedHandler;
        private int _inboundPort = -1;
        private Esp8266Serial _esp;
        private int _lastSocketUsed = -1;
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
        //public event WifiConnectionStateEventHandler ConnectionStateChanged;
        public event WifiInfoEventHandler Info;

        private OutputPort _powerPin = null;
        private OutputPort _resetPin = null;

        private enum Protocols
        {
            Protocol_40,
            Protocol_51,
        }
        private Protocols _protocol;

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
            _esp.Fault += OnEspFault;
            _esp.Start();
            ThreadPool.QueueUserWorkItem(BackgroundInitialize);
        }

        private void OnEspFault(object sender, int cause)
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
                    _esp.SendAndReadUntil(Command(Commands.ResetCommand), OK);
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

        public void Restore()
        {
            EnsureInitialized();
            lock (_oplock)
            {
                _esp.SendAndReadUntil(Command(Commands.RestoreCommand), OK);
            }
        }

        /// <summary>
        /// See if device is responsive to AT commands on serial
        /// It may be down, or out of sync, etc
        /// We can use this if we think it should be responsive, and if fails we can reset the device and/or the library
        /// </summary>
        /// <returns>true if device responds</returns>
        public bool IsAlive()
        {
            bool pingSuccess = false;
            int pingRetries = 2;
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
                catch (Exception e)
                {
                    Debug.Print("No Response: " + e.Message);
                }
            } while (--pingRetries > 0 && !pingSuccess);
            return pingSuccess;
        }

        /// <summary>
        /// Reset serial port
        /// </summary>
        public void ResetPort()
        {
            if (_esp != null)
            {
                //TODO - DAV Com port seems to wedge occasionally - try this, else Dispose and make a new one?
                _esp.Stop();
                _esp.Start();
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
                _esp.SendCommand(Command(Commands.UpdateCommand));
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
                            throw new ErrorException(Command(Commands.UpdateCommand));
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
        public void Connect(string ssid, string password, bool persist = false)
        {
            EnsureInitialized();
            lock (_oplock)
            {
                var info = _esp.SendAndReadUntil(Command(Commands.JoinAccessPointCommand, persist) + '"' + ssid + "\",\"" + password + '"', OK, FailReplies, JoinTimeout);
                // We are going to ignore the returned address data (which varies for different firmware) and request address data from the chip in the property accessors

                //TODO - Check for ERROR response and throw that exception. Add new exception for FAIL, or perhaps use other generic... DAV 25APR2020
                if (info.Length>0 && info[info.Length - 1] == FailReply)
                {
                    throw new Exception("Connect FAIL");
                }
            }
        }

        public void Disconnect()
        {
            EnsureInitialized();
            lock (_oplock)
            {
                _esp.SendAndExpect(Command(Commands.QuitAccessPointCommand), OK);
            }
        }

        public void CreateServer(int port, ServerConnectionOpenedHandler onServerConnectionOpenedHandler)
        {
            EnsureInitialized();
            lock (_oplock)
            {
                _esp.SendAndExpect(Command(Commands.ServerCommand) + "1," + port, new [] { "no change" }, OK);
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
        /// A timeInMs of zero causes an indefinite sleep
        /// You can manually wake by pulsing the reset line low, we do that by passing a timeInMs of -1
        /// </summary>
        /// <param name="timeInMs"></param>
        public void Sleep(int timeInMs)
        {
            EnsureInitialized();
            lock (_oplock)
            {
                if (timeInMs == -1)
                {
                    if (_resetPin != null)
                    {
                        _resetPin.Write(false);
                        Thread.Sleep(3);
                        _resetPin.Write(true);
                    }                
                } else
                    _esp.SendAndExpect(Command(Commands.SleepCommand) + timeInMs.ToString(), OK);
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
                var info = _esp.SendAndReadUntil(Command(Commands.GetOperatingModeCommand), OK);
                foreach (var line in info)
                {
                    if (line.IndexOf(Response(Commands.GetOperatingModeResponse))==0)
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
                _esp.SendAndExpect(Command(Commands.SetOperatingModeCommand, persist) + arg, OK);
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
                _esp.SendAndExpect(Command(Commands.SetAccessPointModeCommand, persist) + 
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
                    var info = _esp.SendAndReadUntil(Command(Commands.GetAccessPointModeCommand), OK);
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
                    var info = _esp.SendAndReadUntil(Command(Commands.GetAccessPointModeCommand), OK);
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
                    var info = _esp.SendAndReadUntil(Command(Commands.GetAccessPointModeCommand), OK);
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
                    var info = _esp.SendAndReadUntil(Command(Commands.GetAccessPointModeCommand), OK);
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
                _esp.SendAndExpect(Command(Commands.SetDhcpMode, persist) + arg + ',' + (enable ? '1' : '0'), OK);
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
                int i = _lastSocketUsed + 1;
                for(int j=0; j < _sockets.Length; ++j,++i)
                {
                    if (i >= _sockets.Length) i = 0;
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
                    reply = _esp.SendCommandAndReadReply(command);
                    do
                    {
                        if (reply.IndexOf(ConnectReply) > -1)
                        {
                            success = true;
                            break;
                        }

                        if (CheckInfoMessage(reply))
                            success = false;

                        if (reply.IndexOf(DnsFailReply) > -1)
                        {
                            success = false;
                            break;
                        }

                        if (Array.IndexOf(FailReplies, reply) > -1)
                        {
                            DeleteSocket(socket);
                            throw new ErrorException(command);
                        }

                        reply = _esp.GetReplyWithTimeout(1000);

                    } while (true);
 
                    if (!success)
                        Thread.Sleep(500);

                } while (--retries > 0 && !success);

                if (retries == 0 && !success)
                {
                    if (reply.IndexOf(ConnectReply) == -1)
                        throw new DnsLookupFailedException(sock.Hostname);
                    throw new FailedExpectException(Command(Commands.SessionStartCommand), ConnectReply, reply);
                }

                reply = reply.Substring(0, reply.IndexOf(','));
                if (int.Parse(reply) != socket)
                {
                    DeleteSocket(socket);
                    throw new Exception("Unexpected socket response");
                }
                sock.Connected = true;
                return sock;
            }
        }

        internal bool CheckInfoMessage(string msg)
        {
            int i;
            if ((i = Array.IndexOf(InfoReplies, msg)) > -1)
            {
                WifiInfoEventArgs args = new WifiInfoEventArgs();
                args.Info = msg;
                args.Number = i;
                OnWifiInfo(args);
                return true;
            }
            return false;
        }

        protected virtual void OnWifiInfo(WifiInfoEventArgs e)
        {
            if (this.Info != null)
                Info(this, e);
        }

        public class WifiInfoEventArgs : EventArgs
        {
            public string Info { get; set; }
            public int Number { get; set; }
        }

        internal void DeleteSocket(int socket)
        {
            EnsureInitialized();
            if (socket >= 0 && socket < _sockets.Length)
            {
                _sockets[socket] = null;
            }
        }

        internal void CloseSocket(int socket)
        {
            EnsureInitialized();
            lock (_oplock)
            {
                if (socket >= 0 && socket < _sockets.Length)
                {
                    _esp.SendAndExpect(Command(Commands.SessionEndCommand) + socket, OK);
                }
            }
        }

        internal void SendPayload(int iSocket, byte[] payload)
        {
            EnsureInitialized();
            lock (_oplock)
            {
                _esp.SendAndExpect(Command(Commands.SendCommand) + iSocket + ',' + payload.Length, OK);
                _esp.Write(payload);
                _esp.Find(Response(Commands.SendCommandReply));
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
                _esp.SendAndExpect(Command(Commands.SetStationAddressCommand, persist) + '"' + value.ToString() + '"', OK);
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
                _esp.SendAndExpect(Command(Commands.SetApAddressCommand, persist) + '"' + value.ToString() + '"', OK);
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
                        return line.Substring(line.IndexOf(':')+1);
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
                        return line.Substring(line.IndexOf(':')+1);
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
                        return line.Substring(line.IndexOf(':')+1);
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
                var info = _esp.SendAndReadUntil(Command(Commands.GetStationMacAddress), OK);
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
                _esp.SendAndExpect(Command(Commands.SetStationMacAddress, persist) + '"' + value + '"', OK);
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
                var info = _esp.SendAndReadUntil(Command(Commands.GetApMacAddress), OK);
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
                _esp.SendAndExpect(Command(Commands.SetApMacAddress, persist) + '"' + value + '"', OK);
            }
        }

        public AccessPoint[] GetAccessPoints(bool sorted = false)
        {
            ArrayList result = new ArrayList();

            EnsureInitialized();
            lock (_oplock)
            {
                //if (this.Mode != OperatingMode.Station)
                //    throw new Exception("You must be in 'Station' mode to retrieve access points.");

                if(sorted)
                    _esp.SendAndReadUntil(Command(Commands.ListAccessPointsSortCommand), OK);

                var response = _esp.SendAndReadUntil(Command(Commands.ListAccessPointsCommand), OK);
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
                var response = _esp.SendAndReadUntil(Command(Commands.ListConnectedClientsCommand), OK);
                foreach (var line in response)
                {
                    if (line != null && line.Length > 0)
                    {
                        //result.Add(line);
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
                // start with the least common denominator command set
                _protocol = Protocols.Protocol_40;
                InitializeCommandSet();
                
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
                                // Give it a nudge in case com port wedged
                                if(pingRetries == 5)
                                    ResetPort();

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
                        // And reset the comport
                        ResetPort();
                    }

                    success = false;
                    try
                    {
                        _esp.SendAndExpect(Command(Commands.EchoOffCommand), OK, 2000);

                        SetMuxMode(true);

                        // Get the firmware version information
                        this.Version = _esp.SendAndReadUntil(Command(Commands.GetFirmwareVersionCommand), OK);

                        if ((this.AtProtocolVersion.StartsWith("1.")) || (this.AtProtocolVersion.StartsWith("0.51")) )
                            _protocol = Protocols.Protocol_51;
                        else
                            _protocol = Protocols.Protocol_40;

                        _isInitializedEvent.Set();
                        success = true;
                        if (this.Booted!=null)
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
            _commandSet40[Commands.EchoOffCommand] = "ATE0";
            _commandSet40[Commands.ResetCommand] = "AT+RST";
            _commandSet40[Commands.GetFirmwareVersionCommand] = "AT+GMR";
            _commandSet40[Commands.SetOperatingModeCommand] = "AT+CWMODE=";
            _commandSet40[Commands.GetOperatingModeCommand] = "AT+CWMODE?";
            _commandSet40[Commands.GetOperatingModeResponse] = "+CWMODE:";
            _commandSet40[Commands.SetDhcpMode] = "AT+CWDHCP=";
            _commandSet40[Commands.SetAccessPointModeCommand] = "AT+CWSAP=";
            _commandSet40[Commands.GetAccessPointModeCommand] = "AT+CWSAP?";
            _commandSet40[Commands.GetAccessPointModeResponse] = "+CWSAP:";
            _commandSet40[Commands.GetAddressInformationCommand] = "AT+CIFSR";
            _commandSet40[Commands.SetStationAddressCommand] = "AT+CIPSTA=";
            _commandSet40[Commands.GetStationAddressCommand] = "AT+CIPSTA?";
            _commandSet40[Commands.GetStationAddressResponse] = "+CIPSTA:";
            _commandSet40[Commands.SetApAddressCommand] = "AT+CIPAP=";
            _commandSet40[Commands.GetApAddressCommand] = "AT+CIPAP?";
            _commandSet40[Commands.GetApAddressResponse] = "+CIPAP:";
            _commandSet40[Commands.GetStationMacAddress] = "AT+CIPSTAMAC?";
            _commandSet40[Commands.GetStationMacAddressResponse] = "+CIPSTAMAC:";
            _commandSet40[Commands.SetStationMacAddress] = "AT+CIPSTAMAC=";
            _commandSet40[Commands.GetApMacAddress] = "AT+CIPAPMAC?";
            _commandSet40[Commands.GetApMacAddressResponse] = "+CIPAPMAC:";
            _commandSet40[Commands.SetApMacAddress] = "AT+CIPAPMAC=";
            _commandSet40[Commands.ListAccessPointsCommand] = "AT+CWLAP";
            _commandSet40[Commands.ListAccessPointsSortCommand] = "AT+CWLAPOPT=1,31";
            _commandSet40[Commands.JoinAccessPointCommand] = "AT+CWJAP=";
            _commandSet40[Commands.QuitAccessPointCommand] = "AT+CWQAP";
            _commandSet40[Commands.ListConnectedClientsCommand] = "AT+CWLIF";
            _commandSet40[Commands.SleepCommand] = "AT+GSLP=";
            _commandSet40[Commands.SetMuxModeCommand] = "AT+CIPMUX=";
            _commandSet40[Commands.SessionStartCommand] = "AT+CIPSTART=";
            _commandSet40[Commands.SessionEndCommand] = "AT+CIPCLOSE=";
            _commandSet40[Commands.ServerCommand] = "AT+CIPSERVER=";
            _commandSet40[Commands.UpdateCommand] = "AT+CIUPDATE";
            _commandSet40[Commands.LinkedReply] = "Linked";
            _commandSet40[Commands.SendCommand] = "AT+CIPSEND=";
            _commandSet40[Commands.SendCommandReply] = "SEND OK";

            _commandSet51[Commands.RestoreCommand] = new[] { "AT+RESTORE", "AT+RESTORE" };
            _commandSet51[Commands.SetOperatingModeCommand] = new[] { "AT+CWMODE_CUR=", "AT+CWMODE_DEF=" };
            _commandSet51[Commands.GetOperatingModeCommand] = new[] { "AT+CWMODE_CUR?", "AT+CWMODE_DEF?" };
            _commandSet51[Commands.GetOperatingModeResponse] = new[] { "+CWMODE_CUR:", "+CWMODE_DEF:" };
            _commandSet51[Commands.SetDhcpMode] = new[] { "AT+CWDHCP_CUR=", "AT+CWDHCP_DEF=" };
            _commandSet51[Commands.GetAccessPointModeCommand] = new[] { "AT+CWSAP_CUR?", "AT+CWSAP_DEF?" };
            _commandSet51[Commands.SetAccessPointModeCommand] = new[] { "AT+CWSAP_CUR=", "AT+CWSAP_DEF=" };
            _commandSet51[Commands.GetAccessPointModeResponse] = new[] { "+CWSAP_CUR:", "+CWSAP_DEF:" };
            _commandSet51[Commands.SetStationAddressCommand] = new[] { "AT+CIPSTA_CUR=", "AT+CIPSTA_DEF=" };
            _commandSet51[Commands.GetStationAddressCommand] = new[] { "AT+CIPSTA_CUR?", "AT+CIPSTA_DEF?" };
            _commandSet51[Commands.GetStationAddressResponse] = new[] { "+CIPSTA_CUR:", "+CIPSTA_DEF:" };
            _commandSet51[Commands.SetApAddressCommand] = new[] { "AT+CIPAP_CUR=", "AT+CIPAP_DEF=" };
            _commandSet51[Commands.GetApAddressCommand] = new[] { "AT+CIPAP_CUR?", "AT+CIPAP_DEF?" };
            _commandSet51[Commands.GetApAddressResponse] = new[] { "+CIPAP_CUR:", "+CIPAP_DEF:" };
            _commandSet51[Commands.SetStationMacAddress] = new[] { "AT+CIPSTAMAC_CUR=", "AT+CIPSTAMAC_DEF=" };
            _commandSet51[Commands.GetStationMacAddress] = new[] { "AT+CIPSTAMAC_CUR?", "AT+CIPSTAMAC_DEF?" };
            _commandSet51[Commands.GetStationMacAddressResponse] = new[] { "+CIPSTAMAC_CUR:", "+CIPSTAMAC_DEF:" };
            _commandSet51[Commands.SetApMacAddress] = new[] { "AT+CIPAPMAC_CUR=", "AT+CIPAPMAC_DEF=" };
            _commandSet51[Commands.GetApMacAddress] = new[] { "AT+CIPAPMAC_CUR?", "AT+CIPAPMAC_DEF?" };
            _commandSet51[Commands.GetApMacAddressResponse] = new[] { "+CIPAPMAC_CUR:", "+CIPAPMAC_DEF:" };
            _commandSet51[Commands.JoinAccessPointCommand] = new[] { "AT+CWJAP_CUR=", "AT+CWJAP_DEF=" };
        }

        private string Command(Commands cmd, bool persist = false)
        {
            string result = null;
            if (_protocol == Protocols.Protocol_51)
            {
                var cmds = ((string[]) _commandSet51[cmd]);
                if (cmds!=null)
                    result = cmds[persist ? 1 : 0];
            }
            if (result == null)
                result = (string)_commandSet40[cmd];
            if (result==null)
                throw new Exception("command not supported");
            return result;
        }

        private string Response(Commands cmd, bool persist = false)
        {
            string result = null;
            if (_protocol == Protocols.Protocol_51)
            {
                var cmds = ((string[])_commandSet51[cmd]);
                if (cmds != null)
                    result = cmds[persist ? 1 : 0];
            }
            if (result == null)
                result = (string)_commandSet40[cmd];
            if (result == null)
                throw new Exception("command not supported");
            return result;
        }

        private void GetStationAddressInfo()
        {
            var info = _esp.SendAndReadUntil(Command(Commands.GetStationAddressCommand), OK);
            foreach (var line in info)
            {
                ParseAddressInfo(line);
            }
        }

        private void GetApAddressInfo()
        {
            var info = _esp.SendAndReadUntil(Command(Commands.GetApAddressCommand), OK);
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
        
        private void SetMuxMode(bool enableMux)
        {
            lock (_oplock)
            {
                _esp.SendAndExpect(Command(Commands.SetMuxModeCommand) + (enableMux ? '1' : '0'), OK);
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
