using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Bubbles.XEvent.MCPServer.Helpers
{
    /// <summary>
    /// Provides methods to read extended event data from a file target using the sys.fn_xe_file_target_read_file function.
    /// </summary>
    /// <param name="filePath"></param>
    /// <param name="sqlConnection"></param>
    public class XEFileTargetStreamer(string filePath, SqlConnection sqlConnection)
    {
        private readonly string filePath = filePath;
        private readonly SqlConnection sqlConnection = sqlConnection;

        /// <summary>
        /// Reads the extended event data from the specified file target and invokes the provided event handler for each event read.
        /// </summary>
        /// <param name="xeConnectionOpen">Called after the connection opens</param>
        /// <param name="xeEventHandler"></param>
        /// <param name="path"></param>
        /// <param name="fileName"></param>
        /// <param name="fileOffset"></param>
        /// <param name="fieldsAndActionsFilter"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task ReadEventStream(HandleConnectionOpen xeConnectionOpen, HandleFileXEvent xeEventHandler, string path, string? fileName, long? fileOffset, string? fieldsAndActionsFilter, CancellationToken cancellationToken)
        {
            using var sqlCommand = new SqlCommand("SELECT object_name, CAST(event_data AS XML) as event_data, file_name, file_offset, timestamp_utc, module_guid, package_guid from sys.fn_xe_file_target_read_file ( @path , null , @initial_file_name , @initial_offset )", sqlConnection)
            {
                CommandTimeout = 0
            };
            await sqlConnection.OpenAsync(cancellationToken);
            try
            {
                await xeConnectionOpen();
                sqlCommand.Parameters.Add("@path", SqlDbType.NVarChar, 260).Value = path;
                sqlCommand.Parameters.Add("@initial_file_name", SqlDbType.NVarChar, 260).Value = (object?)fileName ?? DBNull.Value;
                sqlCommand.Parameters.Add("@initial_offset", SqlDbType.BigInt).Value = (object?)fileOffset ?? DBNull.Value;
                using var dataReader = await sqlCommand.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken);
                while (await dataReader.ReadAsync(cancellationToken))
                {
                    var fileXEvent = new FileXEvent(
                        name: dataReader.GetString(0),
                        eventData: dataReader.GetString(1),
                        fileName: dataReader.GetString(2),
                        fileOffset: dataReader.GetInt64(3),
                        timestamp: dataReader.GetDateTime(4),
                        moduleGuid: dataReader.GetGuid(5),
                        packageGuid: dataReader.GetGuid(6),
                        fieldsAndActions: fieldsAndActionsFilter ?? string.Empty
                    );
                    await xeEventHandler(fileXEvent);
                }
            }
            finally
            {
                await sqlConnection.CloseAsync();
            }
        }
    }

    public interface IFileExtendedEvent
    {
        DateTime Timestamp { get; }
        Guid ModuleGuid { get; }
        Guid PackageGuid { get; }
        string Name { get;  }
        IReadOnlyDictionary<string, string> Fields { get; }
        IReadOnlyDictionary<string, string> Actions { get; }
        string FileName { get; }
        long FileOffset { get; }

    }

    public class FileXEvent : IFileExtendedEvent
    {
        public DateTime Timestamp { get; }
        public Guid ModuleGuid { get; }
        public Guid PackageGuid { get; }
        public string Name { get; }
        public IReadOnlyDictionary<string, string> Fields { get; private set; } = new Dictionary<string, string>();
        public IReadOnlyDictionary<string, string> Actions { get; private set; } = new Dictionary<string, string>();
        public string FileName { get; }
        public long FileOffset { get; }

        /// <summary>
        /// Initializes a new instance of the FileXEvent class with the specified parameters. Parses the event data to populate the Fields and Actions dictionaries.
        /// </summary>
        /// <param name="timestamp"></param>
        /// <param name="moduleGuid"></param>
        /// <param name="packageGuid"></param>
        /// <param name="name"></param>
        /// <param name="eventData"></param>
        /// <param name="fileName"></param>
        /// <param name="fileOffset"></param>
        /// <param name="fieldsAndActions">Comma-separated list of fields and actions to include</param>
        public FileXEvent(DateTime timestamp, Guid moduleGuid, Guid packageGuid, string name, string eventData, string fileName, long fileOffset, string fieldsAndActions = "")
        {
            Timestamp = timestamp;
            ModuleGuid = moduleGuid;
            PackageGuid = packageGuid;
            Name = name;
            FileName = fileName;
            FileOffset = fileOffset;
            ParseEventData(eventData, fieldsAndActions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        private void ParseEventData(string eventData, string[] fieldsAndActionsFilter)
        {
            var fields = new Dictionary<string, string>();
            var actions = new Dictionary<string, string>();
            var xmlDoc = new System.Xml.XmlDocument();
            xmlDoc.LoadXml(eventData);
            var eventNode = xmlDoc.SelectSingleNode("/event");
            if (eventNode != null)
            {
                foreach (System.Xml.XmlNode childNode in eventNode.ChildNodes)
                {
                    if (childNode.Name == "data")
                    {
                        var nameAttr = childNode.Attributes?["name"]?.Value;
                        // exclude fields that are not in the filter list if the filter list is not empty
                        if (nameAttr == null || (fieldsAndActionsFilter.Length > 0 && !fieldsAndActionsFilter.Contains(nameAttr)))
                        {
                            continue;
                        }
                        var val = childNode.SelectSingleNode("value")?.InnerText;
                        if (nameAttr != null && val != null)
                        {
                            fields[nameAttr] = val;
                        }
                    }
                    else if (childNode.Name == "action")
                    {
                        var nameAttr = childNode.Attributes?["name"]?.Value;
                        // exclude actions that are not in the filter list if the filter list is not empty
                        if (nameAttr == null || (fieldsAndActionsFilter.Length > 0 && !fieldsAndActionsFilter.Contains(nameAttr)))
                        {
                            continue;
                        }
                        var val = childNode.SelectSingleNode("value")?.InnerText;
                        if (nameAttr != null && val != null)
                        {
                            actions[nameAttr] = val;
                        }
                    }
                }
            }
            Fields = fields.AsReadOnly();
            Actions = actions.AsReadOnly();
        }
    }
    public delegate Task HandleConnectionOpen();
    public delegate Task HandleFileXEvent(IFileExtendedEvent fileExtendedEvent);
}
