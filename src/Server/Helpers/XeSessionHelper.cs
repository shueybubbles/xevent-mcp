using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Sdk.Sfc;
using Microsoft.SqlServer.Management.XEvent;
using Microsoft.SqlServer.Management.XEventDbScoped;

namespace Bubbles.XEvent.MCPServer.Helpers
{
    /// <summary>
    /// Provides methods to discover data about extended event sessions and their targets.
    /// </summary>
    public class XeSessionHelper : IDisposable
    {
        public XeSessionHelper(string connectionString)
        {
            Connection = new SqlConnection(connectionString);
            var serverConnection = new ServerConnection(Connection);
            XeStore = serverConnection.DatabaseEngineEdition == DatabaseEngineEdition.SqlDatabase
                ? new DatabaseXEStore(new SqlStoreConnection(Connection))
                : new XEStore(new SqlStoreConnection(Connection));
        }

        public SqlConnection Connection { get; }
        private BaseXEStore XeStore { get; }

        public void Dispose()
        {
            Connection.Dispose();
            GC.SuppressFinalize(this);
        }

        public string GetFileTargetFilePath(string session)
        {
            var xeSession = XeStore.Sessions[session] ?? throw new ArgumentException($"Session '{session}' not found.");
            var fileTarget = xeSession.Targets["package0.event_file"] ?? throw new ArgumentException($"File target not found in session '{session}'.");
            var fileName = fileTarget.TargetFields["filename"]?.Value.ToString() ?? throw new InvalidOperationException($"File target in session '{session}' does not have a filename.");
            return Path.ChangeExtension(fileName, null); // Remove the extension to get the base path                
        }

    }

}
