using System;
using System.Collections.Generic;
using System.Text;

namespace Bubbles.XEvent.MCPServer.Services
{
    /// <summary>
    /// Enumerates available connections for SQL Server
    /// </summary>
    public interface IConnectionProvider
    {
        IEnumerable<SqlConnectionEntry> GetConnections();
    }

    /// <summary>
    /// Values enumerated by IConnectionProvider
    /// </summary>
    public record SqlConnectionEntry
    {
        /// <summary>
        /// The name of the connection.
        /// </summary>
        public string Name { get; init; }

        public SqlConnectionEntry(string name, string connectionString)
        {
            Name = name;
            ConnectionString = connectionString;
        }

        /// <summary>
        /// The connection string used to connect to the SQL Server.
        /// </summary>
        public string ConnectionString { get; init; }

    }

    /// <summary>
    /// Implements IConnectionProvider by returning the single connection set by CONNECTION_STRING environment variable. If the environment variable is not set, returns a string for localhost using Windows auth.
    /// </summary>
    public class EnvironmentConnectionProvider : IConnectionProvider
    {
        public const string ConnectionStringEnvVar = "CONNECTION_STRING";

        public IEnumerable<SqlConnectionEntry> GetConnections()
        {
            var connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvVar) 
                                   ?? "Server=localhost;Integrated Security=True;";
            yield return new SqlConnectionEntry("Default", connectionString);
        }
    }
}
