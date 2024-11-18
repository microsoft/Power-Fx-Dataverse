// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.PowerFx.Connectors;
using Xunit.Abstractions;

namespace PowerFx.Dataverse.Cdp.Tests
{
    internal class ConsoleLogger : ConnectorLogger
    {
        private readonly ITestOutputHelper _console;

        private readonly bool _includeDebug;

        private readonly List<ConnectorLog> _logs = new ();

        internal ConsoleLogger(ITestOutputHelper console, bool includeDebug = false)
        {
            _console = console;
            _includeDebug = includeDebug;
        }

        internal string GetLogs()
        {
            return string.Join("|", _logs.Select(cl => GetMessage(cl)).Where(m => m != null));
        }

        private string GetMessage(ConnectorLog connectorLog)
        {
            string cat = connectorLog.Category switch
            {
                LogCategory.Exception => "EXCPT",
                LogCategory.Error => "ERROR",
                LogCategory.Warning => "WARN ",
                LogCategory.Information => "INFO ",
                LogCategory.Debug => "DEBUG",
                _ => "??"
            };

            return $"{DateTime.UtcNow.ToString("O")} [{cat}] {connectorLog.Message}";
        }

        protected override void Log(ConnectorLog log)
        {
            if (_includeDebug || log.Category != LogCategory.Debug)
            {
                _console.WriteLine(GetMessage(log));
                _logs.Add(log);
            }
        }

        internal void WriteLine(string str)
        {
            _console.WriteLine(str);
        }
    }
}
