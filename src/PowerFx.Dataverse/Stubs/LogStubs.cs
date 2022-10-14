using System;

namespace Microsoft.AppMagic.Common.Telemetry
{
    public sealed class Log
    {
        private static readonly Lazy<Log> _instance = new Lazy<Log>(() => new Log(), true);
        public static Log Instance { get { return _instance.Value; } }

        internal void Error(string operationName, string message = null)
        {
            // Do nothing in stub
        }

        /// <summary>
        /// Log a telemetry event
        /// </summary>
        /// <param name="eventName">Name of the event to be logged</param>
        /// <param name="serializedJson">Extra data to be logged with the telemetry event.</param>
        internal void TrackEvent(string eventName, string serializedJson)
        {
            // Do nothing in stub
        }
    }
}

