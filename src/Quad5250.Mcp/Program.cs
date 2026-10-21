using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quad5250.Mcp;

var builder = Host.CreateApplicationBuilder(args);

// stdout carries the MCP protocol; every log line must go to stderr.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddSingleton(McpSettings.FromEnvironment());
builder.Services.AddSingleton<TerminalService>();
builder.Services
    .AddMcpServer(o => o.ServerInstructions =
        """
        Drives a real IBM i 5250 terminal session, for researching green-screen
        applications during modernization. Typical flow: list_hosts, connect,
        sign_on, then read_screen / fill_fields / press_key, and screenshot for
        documentation. Sign off (SIGNOFF on a command line) before disconnect.

        Every press_key reaches the live system. Prefer inquiry paths, and don't
        press Enter on maintenance or posting screens unless the user asked for
        that change. Screens may show patient (PHI) or financial data: quote only
        what the task needs, and redact screenshots before sharing them.

        Rows and columns are 1-based. Field ids are per screen; read the screen
        again after it changes.
        """)
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
