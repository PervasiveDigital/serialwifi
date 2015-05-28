//Tagger:[DoNotTag]
using System;
using Microsoft.SPOT;
using System.Collections;
using PervasiveDigital.Utilities;

namespace PervasiveDigital.Diagnostics
{
    /// <summary>
    /// The DebugPrintListener is purely a convenience class. It can be used in DEBUG builds
    /// to dump a human-readable version of the event data. If you are using the automated source-code
    /// tagger, then you can use the Tagger:[DumpTags] marker to generate event descriptors that
    /// will format your event nicely based on the event-formatting comments. You can also provide
    /// your own formatting strings if you are not using the source-code tagger. Just pass in
    /// a hashtable with eventTag as the key and the formatting string as the value.
    /// 
    /// Time-wise this is a very expensive logging path and will interfere with time-sensitive operations.
    /// It is only suitable for debug. I would strongly recommend an SD-card or network based logging system
    /// and then post-processing of the events for most real-world ship-quality software.
    /// </summary>
    public class DebugPrintListener : ITraceListener
    {
        private Hashtable _eventDescriptors = new Hashtable()
        {
        };

        public DebugPrintListener()
        {
        }

        /// <summary>
        /// For fast and space-efficient initialization with an existing table of event descriptors
        /// </summary>
        /// <param name="eventDescriptors"></param>
        public DebugPrintListener(Hashtable eventDescriptors)
        {
            _eventDescriptors = eventDescriptors;
        }

        public Hashtable EventDescriptors
        {
            get { return _eventDescriptors; }
        }

        public void RecordEvent(byte[] buffer)
        {
            var evt = Logger.Decode(buffer);
            if (_eventDescriptors.Contains(evt.EventTag))
            {
                var str = StringUtilities.Format((string)_eventDescriptors[evt.EventTag], evt.Args);
                Debug.Print(StringUtilities.Format("{0} {1} : {2}", evt.Timestamp.ToString("G"), SevToString(evt.Severity), str));
            }
            else
            {
                Debug.Print(StringUtilities.Format("{0} {1} : {2}", evt.Timestamp.ToString("G"), SevToString(evt.Severity), evt.EventTag));
                for (var i = 0 ; i<evt.Args.Length ; ++i)
                {
                    Debug.Print(StringUtilities.Format("    [{0}] : {1}", i, evt.Args[i].ToString()));
                }
            }
        }

        private string SevToString(Severity sev)
        {
            switch (sev)
            {
                case Severity.Critical:
                    return "Critical";
                case Severity.Debug:
                    return "Debug";
                case Severity.Error:
                    return "Error";
                case Severity.Info:
                    return "Info";
                case Severity.Performance:
                    return "Perf";
                case Severity.Verbose:
                    return "Verbose";
                case Severity.Warning:
                    return "Warning";
                default:
                    return "???";
            }
        }
    }
}
