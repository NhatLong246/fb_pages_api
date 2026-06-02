using RetryService.Models;
using RetryService.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.Configure<RetryOptions>(
    builder.Configuration.GetSection("Kafka"));

builder.Services.AddHostedService<FailedEventRetryWorker>();

var app = builder.Build();
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "retry-service" }));
app.Run();
