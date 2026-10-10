// Copyright (c) David Shiflet. All rights reserved.

using System;
using System.CommandLine;
using System.Threading;
using System.Threading.Tasks;

namespace Bubbles.XEvent.Listen
{
    internal class SessionCommand
    {
        private readonly Argument<string> sessionNameArgument = new("session-name")
        {
            Description = "The name of the Extended Events session to listen to."
        };

        

        private readonly Option<TargetType?> targetArgument = new("--target", "-t")
        {
            Description = "The target type to listen to. Defaults to Live.",
            Required = false,
        };


        public void AddCommand(RootCommand rootCommand)
        {
            var command = new Command("session", "Listen to a specific Extended Events session.")
            {
                sessionNameArgument,
                targetArgument                
            };
            command.SetAction(async (parseResult, cancellationToken) =>
            {
                var context = new SessionCommandContext(parseResult);
                context.PromptForPasswordIfRequired();
                await ReadSession(context, cancellationToken);
            });
            rootCommand.Subcommands.Add(command);
        }

        public static async Task ReadSession(SessionCommandContext context, CancellationToken cancellationToken)
        {
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(context, new System.Text.Json.JsonSerializerOptions { WriteIndented = true,  }));
        }

        public class SessionCommandContext : CommandContextBase
        {
            public SessionCommandContext(ParseResult parseResult) : base(parseResult)
            {
                Server = string.IsNullOrEmpty(Server) ? "localhost" : Server;
                Target = parseResult.GetValueOrEnvVar("--target", "xelisten.target", TargetType.Live);
                SessionName = parseResult.GetRequiredValue<string>("session-name");
            }

            public string SessionName { get; }
            
            public TargetType Target { get; }
        }
    }
}
