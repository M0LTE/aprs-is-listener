using aprs_is_listener;
using Serilog;

var logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<AprsIsListener>();
builder.Logging.ClearProviders();
builder.Services.AddLogging(a => a.AddSerilog(logger));

var host = builder.Build();
host.Run();
