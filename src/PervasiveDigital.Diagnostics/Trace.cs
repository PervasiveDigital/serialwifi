//Tagger:[DoNotTag]
using System;
using System.Collections;
using System.Text;
using System.Diagnostics;

using Microsoft.SPOT;

namespace PervasiveDigital.Diagnostics
{
    public static class Logger
    {
#if DEBUG
        private static Severity _minSev = Severity.Debug;
#else
        private static Severity _minSev = Severity.Info;
#endif
        private static Severity _assertSeverity = Severity.Warning;
        private static UInt32 _categories = 0xffffffff;
        private static ArrayList _listeners = new ArrayList();
        private static object _sync = new object();
        private static UInt32 _perfTimerId = 0;
        private static Hashtable _timers = new Hashtable();
        public const UInt32 ListenerReservedEventTags = 0xffffff00;
        public const UInt32 DefaultCategories = 0xffffffff;

        public static void AddListener(ITraceListener listener)
        {
            if (_listeners.Contains(listener))
                return;
            _listeners.Add(listener);
        }

        /// <summary>
        /// Log messages with lower severities than this will be discarded
        /// </summary>
        public static Severity MinimumSeverityToLog
        {
            get { return _minSev; }
            set { _minSev = value; }
        }

        /// <summary>
        /// This is a 32-bit flag field. For an event to be logged, it must have at least one
        /// bit 'set' in common with this flag. In other words, (eventCategory & CategoriesToLog) must be non-zero.
        /// </summary>
        public static UInt32 CategoriesToLog
        {
            get { return _categories; }
            set { _categories = value; }
        }

        /// <summary>
        /// The severity at which asserts are logged. Normally this is 'Warning', though you may 
        /// consider asserts to be more or less interesting and you can adjust this accordingly.
        /// All asserts are logged at the same severity.
        /// </summary>
        public static Severity AssertSeverity
        {
            get { return _assertSeverity; }
            set { _assertSeverity = value; }
        }

        /// <summary>
        /// This assert fires only in Debug builds
        /// </summary>
        /// <param name="tag">A unique tag for this assert</param>
        /// <param name="category">The category to use for this assert. This can cause your assert to be filtered out from the logging stream.</param>
        /// <param name="condition">When this condition is FALSE, the assert will be logged</param>
        /// <param name="args">The arguments to be logged. These should at least contain the items in your condition.</param>
        [Conditional("DEBUG")]
        public static void DebugAssert(IEventTag tag, UInt32 category, bool condition, params object[] args)
        {
            if (!condition)
                RecordEvent(tag, category, AssertSeverity, args);
        }

        /// <summary>
        /// This assert fires in both debug and release configurations
        /// </summary>
        /// <param name="tag">A unique tag for this assert</param>
        /// <param name="category">The category to use for this assert. This can cause your assert to be filtered out from the logging stream.</param>
        /// <param name="condition">When this condition is FALSE, the assert will be logged</param>
        /// <param name="args">The arguments to be logged. These should at least contain the items in your condition.</param>
        public static void ShipAssert(IEventTag tag, UInt32 category, bool condition, params object[] args)
        {
            if (!condition)
                RecordEvent(tag, category, AssertSeverity, args);
        }

        public static void Verbose(IEventTag eventTag, UInt32 category, params object[] args)
        {
            RecordEvent(eventTag, category, Severity.Verbose, args);
        }

        public static void Debug(IEventTag eventTag, UInt32 category, params object[] args)
        {
            RecordEvent(eventTag, category, Severity.Debug, args);
        }

        public static void Info(IEventTag eventTag, UInt32 category, params object[] args)
        {
            RecordEvent(eventTag, category, Severity.Info, args);
        }

        public static void Warning(IEventTag eventTag, UInt32 category, params object[] args)
        {
            RecordEvent(eventTag, category, Severity.Warning, args);
        }

        public static void Error(IEventTag eventTag, UInt32 category, params object[] args)
        {
            RecordEvent(eventTag, category, Severity.Error, args);
        }

        public static void Critical(IEventTag eventTag, UInt32 category, params object[] args)
        {
            RecordEvent(eventTag, category, Severity.Critical, args);
        }

        public static UInt32 BeginPerformanceTimer()
        {
            var timerId = _perfTimerId;
            _timers.Add(timerId, Environment.TickCount);
            return timerId;
        }

        public static void EndPerformanceTimer(IEventTag eventTag, UInt32 timerId)
        {
            EndPerformanceTimer(eventTag, DefaultCategories, timerId);
        }

        public static void EndPerformanceTimer(IEventTag eventTag, UInt32 category, UInt32 timerId)
        {
            var now = Environment.TickCount;
            if (!_timers.Contains(timerId))
            {
#if DEBUG
                throw new Exception("Timer ID not found");
#else
                return;
#endif
            }
            var then = (int)_timers[timerId];
            var interval = now - then;
            RecordEvent(eventTag, category, Severity.Performance, interval);
        }

        public static void RecordEvent(IEventTag eventTag, UInt32 category, Severity severity, params object[] args)
        {
            if (args.Length > 255)
                throw new Exception("Number of arguments to log is > 255");
            if ((eventTag.Tag & ListenerReservedEventTags) == ListenerReservedEventTags)
                throw new Exception("Listener reserved event tag - tags in this range are for internal use by listeners");

            if ((category & _categories) == 0)
                return;
            if (severity < _minSev)
                return;
            lock (_sync)
            {
                int blocksize = 0;
                var typeCodes = new byte[args.Length + 1];
                typeCodes[0] = (byte)args.Length;
                for (var i = 0 ; i < args.Length ; ++i)
                {
                    blocksize += LengthOf(args[i]);
                    typeCodes[i+1] = (byte)TypeCodeOf(args[i]);
                }
                blocksize += (
                    sizeof(UInt32) +                    // record size
                    sizeof(UInt32) +                    // eventTag
                    sizeof(long) +                      // timestamp
                    sizeof(UInt32) +                    // category
                    sizeof(byte) +                      // severity
                    typeCodes.Length * sizeof(byte)     // argument count and type codes
                    );

                byte[] buffer = new byte[blocksize];
                int offset = 0;
                Marshall(buffer, ref offset, (UInt32)blocksize);
                Marshall(buffer, ref offset, eventTag.Tag);
                Marshall(buffer, ref offset, DateTime.UtcNow);
                Marshall(buffer, ref offset, category);
                Marshall(buffer, ref offset, (byte)severity);
                Marshall(buffer, ref offset, typeCodes);
                foreach (var arg in args)
                {
                    Marshall(buffer, ref offset, arg);
                }
                foreach (var listener in _listeners)
                {
                    ((ITraceListener)listener).RecordEvent(buffer);
                }
            }
        }

        public struct DiagnosticEvent
        {
            public UInt32 EventTag;
            public DateTime Timestamp;
            public UInt32 Category;
            public Severity Severity;
            public object[] Args;
        }

        public static DiagnosticEvent Decode(byte[] buffer)
        {
            var result = new DiagnosticEvent();

            int offset = 0;
            var blocksize = (UInt32)Unmarshall(buffer, ref offset, TypeCode.UInt32);
            if (buffer.Length != blocksize)
                throw new Exception("invalid event record");
            result.EventTag = (UInt32)Unmarshall(buffer, ref offset, TypeCode.UInt32);
            var ticks = (Int64)Unmarshall(buffer, ref offset, TypeCode.Int64);
            result.Timestamp = new DateTime(ticks);
            result.Category = (UInt32)Unmarshall(buffer, ref offset, TypeCode.UInt32);
            result.Severity = (Severity)Unmarshall(buffer, ref offset, TypeCode.Byte);
            var tcArray = (TypeCode[])Unmarshall(buffer, ref offset, TypeCode.Empty);
            result.Args = new object[tcArray.Length];
            for (int i = 0 ; i<tcArray.Length; ++i)
            {
                result.Args[i] = Unmarshall(buffer, ref offset, tcArray[i]);
            }
            return result;
        }

        private static TypeCode TypeCodeOf(object arg)
        {
            if (arg is Array)
                throw new Exception("Array types are not loggable");
            if (arg is byte)
                return TypeCode.Byte;
            else if (arg is Int16)
                return TypeCode.Int16;
            else if (arg is UInt16)
                return TypeCode.UInt16;
            else if (arg is Int32)
                return TypeCode.Int32;
            else if (arg is UInt32)
                return TypeCode.UInt32;
            else if (arg is Int64)
                return TypeCode.Int64;
            else if (arg is UInt64)
                return TypeCode.UInt64;
            else if (arg is DateTime)
                return TypeCode.DateTime;
            else // all other types are either string or get converted to string
                return TypeCode.String;
        }

        private static int LengthOf(object arg)
        {
            if (arg is byte)
                return sizeof(byte);
            else if (arg is Int16 || arg is UInt16)
                return sizeof(Int16);
            else if (arg is Int32 || arg is UInt32)
                return sizeof(Int32);
            else if (arg is Int64 || arg is UInt64 || arg is DateTime)
                return sizeof(Int64);
            else if (arg is string)
                return ((string)arg).Length + sizeof(UInt16);
            else
                return (arg.ToString().Length) + sizeof(UInt16);
        }
 
        private static void Marshall(byte[] buffer, ref int offset, object arg)
        {
            var type = arg.GetType();
            if (type == typeof(byte))
            {
                buffer[offset++] = (byte)arg;
            }
            else if (type == typeof(Int16))
            {
                buffer[offset++] = (byte)(((Int16)arg) & 0xff);
                buffer[offset++] = (byte)(((Int16)arg >> 8) & 0xff);
            }
            else if (type == typeof(UInt16))
            {
                buffer[offset++] = (byte)(((UInt16)arg) & 0xff);
                buffer[offset++] = (byte)(((UInt16)arg >> 8) & 0xff);
            }
            else if (type == typeof(Int32))
            {
                buffer[offset++] = (byte)(((Int32)arg) & 0xff);
                buffer[offset++] = (byte)(((Int32)arg >> 8) & 0xff);
                buffer[offset++] = (byte)(((Int32)arg >> 16) & 0xff);
                buffer[offset++] = (byte)(((Int32)arg >> 24) & 0xff);
            }
            else if (type == typeof(UInt32))
            {
                buffer[offset++] = (byte)(((UInt32)arg) & 0xff);
                buffer[offset++] = (byte)(((UInt32)arg >> 8) & 0xff);
                buffer[offset++] = (byte)(((UInt32)arg >> 16) & 0xff);
                buffer[offset++] = (byte)(((UInt32)arg >> 24) & 0xff);
            }
            else if (type == typeof(Int64))
            {
                buffer[offset++] = (byte)(((Int64)arg) & 0xff);
                buffer[offset++] = (byte)(((Int64)arg >> 8) & 0xff);
                buffer[offset++] = (byte)(((Int64)arg >> 16) & 0xff);
                buffer[offset++] = (byte)(((Int64)arg >> 24) & 0xff);

                buffer[offset++] = (byte)(((Int64)arg >> 32) & 0xff);
                buffer[offset++] = (byte)(((Int64)arg >> 40) & 0xff);
                buffer[offset++] = (byte)(((Int64)arg >> 48) & 0xff);
                buffer[offset++] = (byte)(((Int64)arg >> 56) & 0xff);
            }
            else if (type == typeof(UInt64))
            {
                buffer[offset++] = (byte)(((UInt64)arg) & 0xff);
                buffer[offset++] = (byte)(((UInt64)arg >> 8) & 0xff);
                buffer[offset++] = (byte)(((UInt64)arg >> 16) & 0xff);
                buffer[offset++] = (byte)(((UInt64)arg >> 24) & 0xff);

                buffer[offset++] = (byte)(((UInt64)arg >> 32) & 0xff);
                buffer[offset++] = (byte)(((UInt64)arg >> 40) & 0xff);
                buffer[offset++] = (byte)(((UInt64)arg >> 48) & 0xff);
                buffer[offset++] = (byte)(((UInt64)arg >> 56) & 0xff);
            }
            else if (type == typeof(DateTime))
            {
                Marshall(buffer, ref offset, ((DateTime)arg).Ticks);
            }
            else if (type == typeof(byte[])) // special case for the typecode array
            {
                var value = (byte[])arg;
                var length = value.Length;
                Array.Copy(value, 0, buffer, offset, length);
                offset += length;
            }
            else // string and unknown types
            {
                var value = Encoding.UTF8.GetBytes(arg.ToString());
                UInt16 length = (UInt16)value.Length;
                buffer[offset++] = (byte)((length) & 0xff);
                buffer[offset++] = (byte)((length >> 8) & 0xff);
                Array.Copy(value, 0, buffer, offset, length);
                offset += length;
            }
        }

        private static object Unmarshall(byte[] buffer, ref int offset, TypeCode tc)
        {
            object result = null;
            switch (tc)
            {
                case TypeCode.Empty: // secret code for the argument typecode array
                    var argCount = (int)buffer[offset++];
                    var tcArray = new TypeCode[argCount];
                    for (var i = 0; i < argCount; ++i )
                    {
                        tcArray[i] = (TypeCode)buffer[offset++];
                    }
                    result = tcArray;
                    break;
                case TypeCode.Byte:
                    result = buffer[offset++];
                    break;
                case TypeCode.Int16:
                    result = (Int16)(buffer[offset] | buffer[offset+1] << 8);
                    offset += 2;
                    break;
                case TypeCode.UInt16:
                    result = (UInt16)(buffer[offset] | buffer[offset+1] << 8);
                    offset += 2;
                    break;
                case TypeCode.Int32:
                    result = (Int32)(buffer[offset] | buffer[offset+1] << 8 | buffer[offset+2] << 16 | buffer[offset+3] << 24);
                    offset += 4;
                    break;
                case TypeCode.UInt32:
                    result = (UInt32)(buffer[offset] | buffer[offset+1] << 8 | buffer[offset+2] << 16 | buffer[offset+3] << 24);
                    offset += 4;
                    break;
                case TypeCode.Int64:
                    result = (Int64)(((UInt64)buffer[offset]) | ((UInt64)buffer[offset + 1]) << 8 | ((UInt64)buffer[offset + 2]) << 16 | ((UInt64)buffer[offset + 3]) << 24 |
                                     ((UInt64)buffer[offset + 4]) << 32 | ((UInt64)buffer[offset + 5]) << 40 | ((UInt64)buffer[offset + 6]) << 48 | ((UInt64)buffer[offset + 7]) << 56);
                    offset += 8;
                    break;
                case TypeCode.UInt64:
                    result = (UInt64)(((UInt64)buffer[offset]) | ((UInt64)buffer[offset + 1]) << 8 | ((UInt64)buffer[offset + 2]) << 16 | ((UInt64)buffer[offset + 3]) << 24 |
                                     ((UInt64)buffer[offset + 4]) << 32 | ((UInt64)buffer[offset + 5]) << 40 | ((UInt64)buffer[offset + 6]) << 48 | ((UInt64)buffer[offset + 7]) << 56);
                    offset += 8;
                    break;
                case TypeCode.DateTime:
                    result = new DateTime((long)Unmarshall(buffer, ref offset, TypeCode.Int64));
                    break;
                case TypeCode.String:
                    var len = (int)(buffer[offset] | buffer[offset+1] << 8);
                    offset += 2;
                    result = new string(Encoding.UTF8.GetChars(buffer, offset, len));
                    offset += len;
                    break;
                default:
                    throw new Exception("Unsupported type");
            }
            return result;
        }
        
    }
}
