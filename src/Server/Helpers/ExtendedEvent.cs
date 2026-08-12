using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.SqlServer.XEvent.XELite;

namespace Bubbles.XEvent.MCPServer.Helpers
{
    internal class ExtendedEvent(IXEvent input, IList<string> fieldsAndActions) : IXEvent
    {
        public string Name => input.Name;

        public Guid UUID => input.UUID;

        public DateTimeOffset Timestamp => input.Timestamp;

        public IReadOnlyDictionary<string, object> Fields { get; } = new Dictionary<string, object>(input.Fields.Where(f => fieldsAndActions.Count == 0 || fieldsAndActions.Contains(f.Key))).AsReadOnly();

        public IReadOnlyDictionary<string, object> Actions { get; } = new Dictionary<string, object>(input.Actions.Where(a => fieldsAndActions.Count == 0 || fieldsAndActions.Contains(a.Key))).AsReadOnly();

        public long XEventStartOffsetInBytes => input.XEventStartOffsetInBytes;

        public long XEventEndOffsetInBytes => input.XEventEndOffsetInBytes;

        public long XEventSizeInBytes => input.XEventSizeInBytes;
    }
}
