using System;
using System.Collections.Generic;
using System.Text;
using Bubbles.XEvent.MCPServer.Services;

namespace Bubbles.XEvent.MCPServer.Helpers
{
    public record AvailableConnections
    {
        public required SqlConnectionEntry[] Connections { get; init; }
        public required string DefaultConnectionName { get; init; }
        public required string Message { get; init; }
    }
}
