using CoreService.Data;
using CoreService.Models;
using CoreService.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.Configure<RateLimitOptions>(
    builder.Configuration.GetSection("RateLimit"));

builder.Services.AddDbContext<CoreDbContext>(opts =>
    opts.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

builder.Services.AddHttpClient("gemini", c =>
{
    c.BaseAddress = new Uri("https://generativelanguage.googleapis.com/v1beta/");

    var apiKey = builder.Configuration["Gemini:ApiKey"];
    if (!string.IsNullOrWhiteSpace(apiKey))
    {
        c.DefaultRequestHeaders.Add("x-goog-api-key", apiKey);
    }
});

builder.Services.AddSingleton<SpamDetector>();
builder.Services.AddScoped<RateLimitService>();
builder.Services.AddScoped<AiAnalyzer>();
builder.Services.AddScoped<DecisionEngine>();
builder.Services.AddScoped<ActionExecutor>();
builder.Services.AddSingleton<IFacebookActionCommandPublisher, FacebookActionCommandPublisher>();
builder.Services.Configure<KafkaConsumerOptions>(
    builder.Configuration.GetSection("Kafka"));

builder.Services.AddHostedService<CoreEventConsumerService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
