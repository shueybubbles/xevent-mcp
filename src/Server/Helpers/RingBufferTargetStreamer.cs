using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Management.Common;

namespace Bubbles.XEvent.MCPServer.Helpers
{
    /// <summary>
    /// Reads ring_buffer target for the given session and invokes the provided event handler for each event read.
    /// </summary>
    public class RingBufferTargetStreamer(SqlConnection sqlConnection, string sessionName)
    {
        private readonly SqlConnection sqlConnection = sqlConnection;
        private readonly string sessionName = sessionName;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="xeConnectionOpen"></param>
        /// <param name="xeEventHandler"></param>
        /// <param name="maxEvents">Maximum number of events to return</param>
        /// <param name="fieldsAndActions">comma-delimited list of fields and actions to include. If empty, all fields and actions are included</param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task ReadEventStream(HandleConnectionOpen xeConnectionOpen, HandleRingBufferXEvent xeEventHandler, int maxEvents = 100, string events = "", string fieldsAndActions = "", CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(xeEventHandler);

            var database = "";
            var serverConnection = new ServerConnection(sqlConnection);
            
            if (serverConnection.DatabaseEngineType == DatabaseEngineType.SqlAzureDatabase)
            {
                    database = "_database";
            }
            using var sqlCommand = new SqlCommand($"SELECT CAST(targets.target_data AS XML) FROM sys.dm_xe{database}_session_targets AS targets INNER JOIN sys.dm_xe{database}_sessions AS sessions ON CAST(sessions.address AS binary(8)) = CAST(targets.event_session_address AS binary(8)) WHERE sessions.name = @session_name AND targets.target_name = 'ring_buffer'", sqlConnection)
            {
                CommandTimeout = 0
            };
            await xeConnectionOpen();
            try
            {
                sqlCommand.Parameters.Add("@session_name", SqlDbType.NVarChar, 256).Value = sessionName;
                Trace.TraceInformation("Querying XE ring_buffer target for session: {0}", sessionName);

                var selectedNames = fieldsAndActions.ToHashSet();
                var selectedEvents = events.ToHashSet();
                using var xmlReader = await sqlCommand.ExecuteXmlReaderAsync(cancellationToken);
                var eventCount = 0;
                while (eventCount < maxEvents && xmlReader.ReadToFollowing("event"))
                {
                    using var eventReader = xmlReader.ReadSubtree();
                    var eventElement = await XElement.LoadAsync(eventReader, LoadOptions.None, cancellationToken);
                    
                    if (selectedEvents.Count > 0 && !selectedEvents.Contains(eventElement.Attribute("name")!.Value))
                    {
                        continue;
                    }

                    var fields = eventElement
                        .Elements("data")
                        .Where(data => selectedNames.Count == 0 || selectedNames.Contains(data.Attribute("name")!.Value))
                        .ToDictionary(
                            data => data.Attribute("name")!.Value,
                            data => data.Element("value")?.Value ?? string.Empty,
                            StringComparer.OrdinalIgnoreCase);

                    var actions = eventElement
                        .Elements("action")
                        .Where(action => selectedNames.Count == 0 || selectedNames.Contains(action.Attribute("name")!.Value))
                        .ToDictionary(
                            action => action.Attribute("name")!.Value,
                            action => action.Element("value")?.Value ?? string.Empty,
                            StringComparer.OrdinalIgnoreCase);

                    _ = DateTimeOffset.TryParse((string?)eventElement.Attribute("timestamp"), out var timestamp);

                    eventCount++;
                    await xeEventHandler(new RingBufferExtendedEvent(
                        timestamp,
                        (string?)eventElement.Attribute("package") ?? string.Empty,
                        eventElement.Attribute("name")!.Value,
                        fields,
                        actions));
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }
            finally
            {
                await sqlConnection.CloseAsync();
            }
        }
    }

    public delegate Task HandleRingBufferXEvent(IRingBufferExtendedEvent ringBufferXEvent);
    public interface IRingBufferExtendedEvent
    {
        DateTimeOffset Timestamp { get; }
        string Package { get; }
        string Name { get; }
        IReadOnlyDictionary<string, string> Fields { get; }
        IReadOnlyDictionary<string, string> Actions { get; }
    }

    
    public class RingBufferExtendedEvent(DateTimeOffset timestamp, string package, string name, IReadOnlyDictionary<string, string> fields, IReadOnlyDictionary<string, string> actions) : IRingBufferExtendedEvent
    {
        public DateTimeOffset Timestamp { get; } = timestamp;
        public string Package { get; } = package;
        public string Name { get; } = name;
        public IReadOnlyDictionary<string, string> Fields { get; } = fields;
        public IReadOnlyDictionary<string, string> Actions { get; } = actions;
    }
}
