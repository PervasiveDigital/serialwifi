//Tagger:[DoNotTag]
using System;
using Microsoft.SPOT;

namespace PervasiveDigital.Diagnostics
{
    // Note that the use of structs in this fashion should result in the use of sizeof(UInt32) in stack space and not
    //   result in a heap allocation. Otherwise logging would create a lot of heap churn.

    public interface IEventTag
    {
        UInt32 Tag { get; }
    }

    public struct AutoTag : IEventTag
    {
        public AutoTag(UInt32 tag)
        {
            _tag = tag;
        }
        private UInt32 _tag;
        public UInt32 Tag { get { return _tag; } }

        public static explicit operator AutoTag(UInt32 tag)
        {
            return new AutoTag(tag);
        }
    }

    public struct FixedTag : IEventTag
    {
        public FixedTag(UInt32 tag)
        {
            _tag = tag;
        }
        private UInt32 _tag;
        public UInt32 Tag { get { return _tag; } }

        public static explicit operator FixedTag(UInt32 tag)
        {
            return new FixedTag(tag);
        }
    }

    public enum Severity : byte
    {
        Verbose = 0,
        Debug = 1,
        Info = 2,
        Warning = 3,
        Error = 4,
        Critical = 5,
        Performance = 6
    }

    public interface ITraceListener
    {
        void RecordEvent(byte[] eventBlock);
    }
}
