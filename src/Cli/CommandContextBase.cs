// Copyright (c) David Shiflet. All rights reserved.

using System;
using System.CommandLine;
using Microsoft.Data.SqlClient;

namespace Bubbles.XEvent.Listen
{
    public abstract class CommandContextBase(ParseResult parseResult)
    {
        public string? Server { get; protected set; } = parseResult.GetValueOrEnvVar("--server", "xelisten.server", "");
        public string Database { get; } = parseResult.GetValueOrEnvVar("--database", "xelisten.database", "");

        public string User { get; } = parseResult.GetValueOrEnvVar("--user", "xelisten.user", "");

        public string Password { get; } = parseResult.GetValueOrEnvVar("--password", "xelisten.password", "");

        public SqlAuthenticationMethod AuthenticationMethod { get; } = parseResult.GetValueOrEnvVar("--auth", "xelisten.auth", SqlAuthenticationMethod.NotSpecified);

        public long Timeout { get; } = parseResult.GetValueOrEnvVar("--timeout", "xelisten.timeout", 0);
        public string[] Events { get; } = parseResult.GetValueOrEnvVar("--events", "xelisten.events"); 

        public string[] Fields { get; } = parseResult.GetValueOrEnvVar("--fields", "xelisten.fields");

        public long MaxEvents { get; } = parseResult.GetValueOrEnvVar("--max-events", "xelisten.max-events", 0L);

        public OutputFormat OutputFormat { get; } = parseResult.GetValueOrEnvVar("--output", "xelisten.output", OutputFormat.Default);

        private SqlConnectionStringBuilder? connectionStringBuilder = null;
        public string ConnectionString
        {
            get
            {
                if (connectionStringBuilder == null)
                {
                    if (string.IsNullOrEmpty(Server))
                    {
                        var connstrVar = System.Environment.GetEnvironmentVariable("xelisten.connection-string");
                        if (!string.IsNullOrEmpty(connstrVar))
                        {
                            connectionStringBuilder = new SqlConnectionStringBuilder(connstrVar);
                            return connstrVar;
                        }
                    }
                    var builder = new SqlConnectionStringBuilder
                    {
                        DataSource = string.IsNullOrEmpty(Server) ? "localhost" : Server,
                        InitialCatalog = string.IsNullOrEmpty(Database) ? "master" : Database,
                    };
                    if (string.IsNullOrEmpty(User))
                    {
                        builder.IntegratedSecurity = AuthenticationMethod == SqlAuthenticationMethod.NotSpecified;
                    }
                    else
                    {
                        builder.UserID = User;
                        if (!string.IsNullOrEmpty(Password))
                        {
                            builder.Password = Password;
                        }
                    }
                    if (AuthenticationMethod != SqlAuthenticationMethod.NotSpecified)
                    {
                        builder.Authentication = AuthenticationMethod;
                    }
                    connectionStringBuilder = builder;
                }
                return connectionStringBuilder.ConnectionString;
            }
        }

        public bool IsPasswordRequired => !string.IsNullOrEmpty(ConnectionString) &&
            !connectionStringBuilder!.IntegratedSecurity
            && !string.IsNullOrEmpty(connectionStringBuilder.UserID)
            && string.IsNullOrEmpty(connectionStringBuilder.Password)
            && (connectionStringBuilder.Authentication == SqlAuthenticationMethod.NotSpecified || connectionStringBuilder.Authentication == SqlAuthenticationMethod.SqlPassword);

        public void SetPassword(string password) => connectionStringBuilder?.Password = password;

        private IEventOutput output = null!;
        public IEventOutput EventOutput => output ??= OutputFormat switch
        {
            OutputFormat.Default => new DefaultEventOutput(),
            OutputFormat.Json => new JsonEventOutput(),
            OutputFormat.Xml => new XmlEventOutput(),
            OutputFormat.Csv => Fields.Length > 0 ? new CsvOutput() : throw new InvalidOperationException("CSV requires --fields"),
            _ => new DefaultEventOutput()
        };
        public void PromptForPasswordIfRequired()
        {
            // if the stdin is redirected, try to read the password from it, otherwise prompt the user for the password
            if (IsPasswordRequired)
            {
                if (Console.IsInputRedirected)
                {
                    var password = Console.In.ReadLine();
                    SetPassword(password ?? "");
                }
                else
                {
                    Console.Write("Enter password:");
                    var password = string.Empty;
                    ConsoleKey key;

                    do
                    {
                        var keyInfo = Console.ReadKey(intercept: true);
                        key = keyInfo.Key;

                        if (key == ConsoleKey.Backspace && password.Length > 0)
                        {
                            // Remove the last character from the password and update the console
                            password = password[0..^1];
                            Console.Write("\b \b");
                        }
                        else if (!char.IsControl(keyInfo.KeyChar))
                        {
                            // Append the character to the password and display an asterisk
                            password += keyInfo.KeyChar;
                            Console.Write("*");
                        }
                    } while (key != ConsoleKey.Enter);

                    SetPassword(password ?? "");
                }
            }
        }
    }
}
