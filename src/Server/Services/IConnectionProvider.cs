using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.SqlClient;

namespace Bubbles.XEvent.MCPServer.Services
{
    /// <summary>
    /// Enumerates available connections for SQL Server
    /// </summary>
    public interface IConnectionProvider
    {
        IEnumerable<SqlConnectionEntry> GetConnections();
        string DefaultConnectionName { get; }

        SqlConnectionEntry? GetConnection(string connectionName);

        string EmptyListMessage { get; }
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
            var builder = new SqlConnectionStringBuilder(connectionString);
            ServerName = builder.DataSource;
            DatabaseName = builder.InitialCatalog;
            AuthenticationType = builder.IntegratedSecurity ? "Windows Authentication" :
                (builder.Authentication == SqlAuthenticationMethod.NotSpecified || builder.Authentication == SqlAuthenticationMethod.SqlPassword) ? 
                "SQL Server Authentication" : builder.Authentication.ToString();
            UserName = builder.UserID;
        }

        /// <summary>
        /// The connection string used to connect to the SQL Server.
        /// </summary>
        public string ConnectionString { get; init; }

        public string ServerName { get; }

        public string DatabaseName { get; }

        public string AuthenticationType { get; }

        public string UserName { get; }
    }

    /// <summary>
    /// Implements IConnectionProvider by returning the single connection set by CONNECTION_STRING environment variable. If the environment variable is not set, returns a string for localhost using Windows auth.
    /// </summary>
    public class EnvironmentConnectionProvider : IConnectionProvider
    {
        public const string ConnectionStringEnvVar = "CONNECTION_STRING";

        public string DefaultConnectionName => "Default";

        public SqlConnectionEntry? GetConnection(string connectionName)
        {
            var connection = connectionName == "Default" ? GetConnections().First() : null;
            return connection;
        }

        public IEnumerable<SqlConnectionEntry> GetConnections()
        {
            var connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvVar) 
                                   ?? "Server=localhost;Integrated Security=True;";
            yield return new SqlConnectionEntry("Default", connectionString);
        }

        public string EmptyListMessage => "Set the CONNECTION_STRING environment variable to a valid SQL Server connection string.";
    }
}
