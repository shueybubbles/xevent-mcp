// Copyright (c) David Shiflet. All rights reserved.

using System;
using System.CommandLine;
using System.Threading.Tasks;
using Bubbles.XEvent.MCPServer.Helpers;
using Microsoft.Data.SqlClient;

namespace Bubbles.XEvent.Listen
{
    internal class ScriptCommand
    {
        private readonly Argument<string> sessionNameArgument = new("session-name")
        {
            Description = "The name of the Extended Events session to script."
        };

        public void AddCommand(RootCommand rootCommand)
        {
            var command = new Command("script", "Generate a CREATE script for a specific Extended Events session.")
            {
                sessionNameArgument
            };

            command.SetAction((parseResult, _) =>
            {
                var context = new ScriptCommandContext(parseResult);
                context.PromptForPasswordIfRequired();

                try
                {
                    using var sessionHelper = new XeSessionHelper(context.ConnectionString);
                    Console.Out.WriteLine(sessionHelper.GetSessionCreateScript(context.SessionName));
                }
                catch (ArgumentException ex)
                {
                    Console.Error.WriteLine(ex.Message);
                    Environment.ExitCode = 1;
                }
                catch (SqlException ex)
                {
                    Console.Error.WriteLine($"SQL error {ex.Number}: {ex.Message}");
                    Environment.ExitCode = 1;
                }

                return Task.CompletedTask;
            });

            rootCommand.Subcommands.Add(command);
        }

        private class ScriptCommandContext(ParseResult parseResult) : CommandContextBase(parseResult)
        {
            public string SessionName { get; } = parseResult.GetRequiredValue<string>("session-name");
        }
    }
}
