// <copyright file="Program.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.OpenApi.Models;
using Microsoft.PowerFx;
using Microsoft.PowerFx.Connectors;

namespace Repl
{
    public static class Exts
    {
        public static IReadOnlyList<ConnectorFunction> AddPlugIn(this PowerFxConfig config, string @namespace, OpenApiDocument swagger)
        {
            return config.AddActionConnector(new ConnectorSettings(@namespace) { Compatibility = ConnectorCompatibility.PowerAppsCompatibility }, swagger);
        }
    }
}
