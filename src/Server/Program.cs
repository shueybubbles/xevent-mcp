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

builder.Services.AddSingleton<IUrlStreamProvider, UrlStreamProvider>();

await builder.Build().RunAsync();