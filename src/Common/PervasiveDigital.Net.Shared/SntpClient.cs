using System;
using System.Threading;
using Microsoft.SPOT;
using Microsoft.SPOT.Hardware;
using Microsoft.SPOT.Touch;

namespace PervasiveDigital.Net
{
    public class SntpClient : IDisposable
    {
        private const ulong NTP_OFFSET = 2208988800UL;
        private const int SNTP_PACKET_SIZE = 48;
        private byte[] _packet = new byte[SNTP_PACKET_SIZE];
        private readonly INetworkAdapter _adapter;
        private readonly string _host;
        private TimeSpan _pollingInterval;
        private AutoResetEvent _responseReceived = new AutoResetEvent(false);
        private Timer _timer = null;
        private DateTime _lastTimeRetrieved;

        public SntpClient(INetworkAdapter adapter, string host)
            : this(adapter, host, new TimeSpan(24, 0, 0))
        {
        }

        public SntpClient(INetworkAdapter adapter, string host, TimeSpan pollingInterval)
        {
            if (pollingInterval.Ticks < new TimeSpan(0, 0, 15).Ticks)
            {
                throw new ArgumentOutOfRangeException("pollingInterval", "polling interval should be 15 seconds or more to avoid flooding the server");
            }
            _adapter = adapter;
            _host = host;
            _pollingInterval = pollingInterval;
        }

        public void Dispose()
        {
            Stop();
        }

        public void Start()
        {
            if (_timer != null)
                return;
            SetTime();
            _timer = new Timer(TimerCallbackHandler, null, TimeSpan.Zero, _pollingInterval);
        }

        public void Stop()
        {
            if (_timer != null)
            {
                _timer.Dispose();
                _timer = null;
            }
        }

        private void TimerCallbackHandler(object state)
        {
            SetTime();
        }

        /// <summary>
        /// Get the current time from an NTP server.  Do not use this under normal circumstances.
        /// Instead, call Start() and just use DateTime.Now or DateTime.UtcNow. Calling this routine
        /// too frequently can cause the server to ban your ip address or suspend service to you.
        /// </summary>
        /// <returns></returns>
        public DateTime RequestTimeFromServer()
        {
            DateTime result;
            lock (_adapter.OperationLock)
            {
                result = GetTimeFromServer();
            }
            return result;
        }

        public void SetTime()
        {
            lock (_adapter.OperationLock)
            {
                var now = GetTimeFromServer();
                Utility.SetLocalTime(now);
            }
        }

        private DateTime GetTimeFromServer()
        {
            using (var socket = _adapter.OpenSocket(_host, 123, false))
            {
                socket.DataReceived += OnDataReceived;
                Array.Clear(_packet,0,_packet.Length);
                _packet[0] = 0xe3; // LI, Version and Mode
                _packet[1] = 0; // Stratum or type of clock
                _packet[2] = 6; // Polling interval
                _packet[3] = 0xEC; // Peer clock precision
                // Leave eight bytes of zeros for root delay and root dispersion
                _packet[12] = 49;
                _packet[13] = 0x4E;
                _packet[14] = 49;
                _packet[15] = 52;

                socket.Send(_packet);

#if DEBUG
                _responseReceived.WaitOne();
#else
                // wait for a response
                if (!_responseReceived.WaitOne(5000, false))
                    throw new Exception("No SNTP respose received");
#endif
            }
            var result = _lastTimeRetrieved;
            _lastTimeRetrieved = DateTime.MinValue;
            return result;
        }

        private void OnDataReceived(object sender, SocketReceivedDataEventArgs args)
        {
            var data = args.Data;

            // weird expression format in order to get the sign extension correct
            ulong timestamp = ((ulong)data[40] << 24 | (ulong)data[41] << 16 | (ulong)data[42] << 8 | (ulong)data[43]);

            DateTime result = new DateTime(1900, 1, 1, 0, 0, 0);
            result = result.AddTicks((long)timestamp * TimeSpan.TicksPerSecond);
            _lastTimeRetrieved = result;
            _responseReceived.Set();
        }
    }
}
