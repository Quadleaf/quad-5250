using Quad5250.Server.Hubs;
using Quad5250.Server.Logging;
using Quad5250.Server.Sessions;

var builder = WebApplication.CreateBuilder(args);

// Mirror console logs to a file when configured (used in dev for parsing
// disconnect hex dumps). In containers stdout is captured by the platform.
var logFilePath = builder.Configuration["Logging:File:Path"];
if (!string.IsNullOrEmpty(logFilePath))
{
    var resolved = Path.IsPathFullyQualified(logFilePath)
        ? logFilePath
        : Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, logFilePath));
    builder.Logging.AddProvider(new FileLoggerProvider(resolved));
}

builder.Services.AddSignalR();
builder.Services.AddSingleton<SessionRegistry>();

// CORS only when origins are listed (dev runs the React app on a different
// port). In the deployed setup nginx fronts both apps so requests are
// same-origin and CORS isn't needed.
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
if (corsOrigins.Length > 0)
{
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy => policy
            .WithOrigins(corsOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());
    });
}

var app = builder.Build();

if (corsOrigins.Length > 0) app.UseCors();
app.MapHub<TerminalHub>("/hubs/terminal");

app.Run();
