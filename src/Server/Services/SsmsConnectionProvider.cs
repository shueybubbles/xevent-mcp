using Microsoft.Data.SqlClient;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.SqlServer.Management.RegisteredServers;

namespace Bubbles.XEvent.MCPServer.Services
{
    public class SsmsConnectionProvider : IConnectionProvider
    {
        private const string DefaultConnectionKey = "__bubbles__default__";
        private readonly FileSystemWatcher fileWatcher = new();
        private ConcurrentDictionary<string, SqlConnectionEntry> connections = new();
        private readonly object syncobj = new();

        private readonly string regFilePath = string.Empty;

        public SsmsConnectionProvider() 
        { 
            fileWatcher.Path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "SQL Server Management Studio");
            fileWatcher.Filter = RegisteredServersStore.RegisteredServersFileName;
            fileWatcher.Changed += (s, e) => ResetConnections();
            ResetConnections();
        }

        public SsmsConnectionProvider(string regFilePath)
        {
            this.regFilePath = regFilePath;
            ResetConnections();
        }

        private void ResetConnections()
        {
            var newConnections = new ConcurrentDictionary<string, SqlConnectionEntry>();
            var store = RegisteredServersStore.LocalFileStore;
            if (!string.IsNullOrEmpty(regFilePath))
            {
                store = RegisteredServersStore.InitializeLocalRegisteredServersStore(regFilePath);
            }
            else
            {
                RegisteredServersStore.ReloadLocalFileStore();
            }
            
            LoadServerGroup(string.Empty, store.MruSqlConnectionsGroup, newConnections);
            if (!newConnections.IsEmpty)
            {
                var lastConnection = newConnections.OrderBy(kvp => kvp.Key).Last();
                // if the current collection has a default connection, use that one instead of the last one in the new collection
                if (connections.TryGetValue(DefaultConnectionKey, out var currentDefaultConnection) && newConnections.TryGetValue(currentDefaultConnection.Name, out var newDefaultConnection))
                {
                    _ = newConnections.TryAdd(DefaultConnectionKey, newDefaultConnection);
                }
                else
                {
                    _ = newConnections.TryAdd(DefaultConnectionKey, lastConnection.Value);
                }
            }
            LoadServerGroup(string.Empty, store.DatabaseEngineServerGroup, newConnections);

            _ = Interlocked.Exchange(ref connections, newConnections);
            
        }

        private static void LoadServerGroup(string parent, ServerGroup group, ConcurrentDictionary<string, SqlConnectionEntry> newConnections)
        {
            var prefix = string.IsNullOrEmpty(parent) ? string.Empty : $"{parent}/";
            foreach (var server in group.RegisteredServers)
            {
                var key = $"{prefix}{group.Name}/{server.Name}";
                _ = newConnections.TryAdd(key, new SqlConnectionEntry(key, server.ConnectionString));
            }
            foreach (var subgroup in group.ServerGroups)
            {
                LoadServerGroup($"{prefix}{group.Name}", subgroup, newConnections);
            }
        }

        public string DefaultConnectionName => connections.TryGetValue(DefaultConnectionKey, out var defaultConnection) ? defaultConnection.Name : string.Empty;

        public string EmptyListMessage => "No connections found. Please add a connection or registered server in SQL Server Management Studio (SSMS) or that REGSRVR_FILE variable is set to a valid xml file.";

        /// <summary>
        /// SSMS connections come from the MruSqlConnectionsGroup and DatabaseEngineServerGroup
        /// </summary>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public IEnumerable<SqlConnectionEntry> GetConnections() => connections.ToArray().Where(k => k.Key != DefaultConnectionKey).Select(kvp => kvp.Value);

        public SqlConnectionEntry? GetConnection(string connectionName)
        {
            if (connections.TryGetValue(connectionName, out var connection))
            {
                if (OperatingSystem.IsWindows())
                {
                    var builder = new SqlConnectionStringBuilder(connection.ConnectionString);
                    if (builder.Authentication == SqlAuthenticationMethod.ActiveDirectoryInteractive)
                    {
                        builder.Authentication = SqlAuthenticationMethod.ActiveDirectoryDefault;
                        return new SqlConnectionEntry(connection.Name, builder.ConnectionString);
                    }                    
                }
                return connection;
            }
            return null;
        }
    }
}
