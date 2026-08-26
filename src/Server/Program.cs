using System;
using System.IO;
using System.Reflection;
using Bubbles.XEvent.MCPServer.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;


var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(opts =>
{
   opts.LogToStandardErrorThreshold = Microsoft.Extensions.Logging.LogLevel.Trace; 
});

builder.Services
.AddMcpServer()
.WithStdioServerTransport()
.WithToolsFromAssembly();
var ssmsEnvVar = Environment.GetEnvironmentVariable("SSMS_CONNECTIONS");
var regSrvrFilePath = Environment.GetEnvironmentVariable("REGSRVR_FILE");
var useSsms = !string.IsNullOrEmpty(ssmsEnvVar) && ssmsEnvVar.Equals("true", StringComparison.OrdinalIgnoreCase);
builder.Services.AddSingleton<IUrlStreamProvider, UrlStreamProvider>();
if (!useSsms)
{
    _ = builder.Services.AddSingleton<IConnectionProvider, EnvironmentConnectionProvider>();
}
else
{
    if (string.IsNullOrEmpty(regSrvrFilePath) || !File.Exists(regSrvrFilePath))
    {        
         _ = builder.Services.AddSingleton<IConnectionProvider, SsmsConnectionProvider>();
    }
    else
    {
        _ = builder.Services.AddSingleton<IConnectionProvider>(new SsmsConnectionProvider(regSrvrFilePath));
    }   
}


await builder.Build().RunAsync();