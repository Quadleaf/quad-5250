using Quad5250.Server.Hubs;
using Quad5250.Server.Logging;
using Quad5250.Server.Sessions;

var builder = WebApplication.CreateBuilder(args);

// Mirror console logs to logs/quad5250.log at the repo root for easier diagnosis
// of disconnects (the hex dumps are too long to scroll through in a terminal).
var logPath = Path.Combine(builder.Environment.ContentRootPath, "..", "..", "logs", "quad5250.log");
builder.Logging.AddProvider(new FileLoggerProvider(Path.GetFullPath(logPath)));

builder.Services.AddSignalR();
builder.Services.AddSingleton<SessionRegistry>();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy
        .WithOrigins("http://localhost:5173")
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});

var app = builder.Build();

app.UseCors();
app.MapHub<TerminalHub>("/hubs/terminal");

app.Run();
