using System;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;

namespace CachemanCommon
{
    public sealed class Log
    {
        public static bool UseEventLog { get; set; }
        private Log() { }

        public static string EventSource { get; set; }
        static Log() {
            UseEventLog = true;
            EventSource = "Cacheman";
        }
        public static void LogMessage(string message) {
            LogImpl(message, EventLogEntryType.Information);
        }

        public static void LogError(string message) {
            LogImpl(message, EventLogEntryType.Error);
        }

        private static void LogImpl(string message, EventLogEntryType entryType) {
            
            Console.WriteLine(message);
            Trace.WriteLine(message);
            if (UseEventLog) {
                EventLog.WriteEntry(EventSource, message, entryType);
            }
        }

    }
}
