using System;
using System.Threading;
using Microsoft.SPOT;

using PervasiveDigital.Diagnostics;

namespace DiagnosticsTest
{
    public class Program
    {
        public static void Main()
        {
            var one = (int)1;
            var two = (Int16)2;
            var three = (byte)3;

            // Control logging by filtering messages. Bonus points: Periodically get these settings from the network for
            //   distributed control of telemetry gathering.
            Logger.MinimumSeverityToLog = Severity.Info;
            Logger.CategoriesToLog = Logger.DefaultCategories; // this is a bit mask, so you can have 32 categories and log them in any combination

            // Creating a listener that will print to Debug.Print (not advised for release-mode production software)
            // Use an SD-card or networked listener for production telemetry.
            var listener = new DebugPrintListener();
            Logger.AddListener(listener);

            // Generic logging with no formatting
            Logger.Error((FixedTag)1, Logger.DefaultCategories, one, two, three);

            listener.EventDescriptors.Add((uint)2, "This {0} reports {1} {2} and {3} at {4}");
            Logger.Warning((FixedTag)2, Logger.DefaultCategories, "event", 1, (UInt64)2, (byte)3, DateTime.UtcNow);

            //Event: This is an auto-tagged event with arguments of {0} and {1}
            Logger.Info((AutoTag)0xc5ba21f4, Logger.DefaultCategories, 1, 2);

            //Event: This is an event without arguments
            Logger.Info((AutoTag)0xd4ab1103, Logger.DefaultCategories);

            // Performance monitoring
            var perfTimer = Logger.BeginPerformanceTimer();
            Thread.Sleep(3124);
            Logger.EndPerformanceTimer((FixedTag)3, perfTimer);

            // Asserts
            // In debug only
            Logger.DebugAssert((FixedTag)4, Logger.DefaultCategories, true == false, "No it doesn't");

            // In debug and release only
            Logger.ShipAssert((FixedTag)5, Logger.DefaultCategories, true == false, "No it doesn't, even in release software");

            while (true)
            {
                Thread.Sleep(5000);
            }
        }
    }
}
