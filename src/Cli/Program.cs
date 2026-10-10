// Copyright (c) David Shiflet. All rights reserved.

using System.CommandLine;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Bubbles.XEvent.Listen
{
    internal class Program
    {
        public static async Task<int> Main(string[] args)
        {
            var root = CreateRootCommand();
            new SessionCommand().AddCommand(root);
            new FileCommand().AddCommand(root);
            return await root.Parse(args).InvokeAsync();
        }

        private static RootCommand CreateRootCommand()
        {
            Option<string> serverArgument = new("--server", "-S")
            {
                Description = "The name of the SQL Server instance to connect to. Defaults to localhost.",
                Required = false,
                Recursive = true,
            };

            Option<string> databaseArgument = new("--database", "-D")
            {
                Description = "The name of the database to connect to. Defaults to the default database for the user. Only required for Azure SQL Database connections.",
                Required = false,
                Recursive = true,
            };

            Option<string> userArgument = new("--user", "-U")
            {
                Description = "The user or principal name to use for authentication.",
                Required = false,
                Recursive = true,
            };

            Option<string> passwordArgument = new("--password", "-P")
            {
                Description = "The password to use for authentication.",
                Required = false,
                Recursive = true,
            };

            Option<SqlAuthenticationMethod?> authArgument = new("--auth", "-A")
            {
                Description = "The authentication method to use for the connection. Defaults to NotSpecified.",
                Required = false,
                Recursive = true,
            };

            Option<long?> timeoutArgument = new("--timeout", "-T")
            {
                Description = "Maximum number of seconds to listen to the session. Includes time to establish the SQL connection. Defaults to no timeout.",
                Required = false,
                Validators =
                {
                    result =>
                    {
                        if (result.GetValueOrDefault<long>() < 1)
                        {
                            result.AddError("The timeout must be a positive integer.");
                        }
                    }
                }
            };

            // These options apply to all commands, so we define them here and add them to the root command.
            Option<string[]> fieldsOption = new("--fields", "-f")
            {
                Arity = ArgumentArity.OneOrMore,
                Required = false,
                Description = "List of fields and actions to include in the output. If not specified, all fields will be included.",
                Recursive = true,
                AllowMultipleArgumentsPerToken = true,
            };

            Option<string[]> eventsOption = new("--events", "-e")
            {
                Arity = ArgumentArity.OneOrMore,
                Required = false,
                Description = "List of events to include in the output. If not specified, all events will be included.",
                Recursive = true,
                AllowMultipleArgumentsPerToken = true,

            };

            Option<long?> maxEventsOption = new("--max-events", "-m")
            {
                Required = false,
                Description = "Maximum number of events to return. Default is all events.",
                Recursive = true,
                Validators =
                {
                    result =>
                    {
                        if (result.GetValueOrDefault<long>() < 1)
                        {
                            result.AddError("The maximum number of events must be a positive integer.");
                        }
                    }
                }
            };

            Option<OutputFormat?> outputFormatOption = new("--output", "-o")
            {
                Required = false,
                Description = "Output format.",
                Recursive = true,
            };

            return new RootCommand("xelisten is a command-line tool for listening to SQL Server Extended Events in real-time.")
            {
                serverArgument,
                databaseArgument,
                userArgument,
                passwordArgument,
                authArgument,
                timeoutArgument,
                fieldsOption,
                eventsOption,
                maxEventsOption,
                outputFormatOption
            };
        }
    }
}
