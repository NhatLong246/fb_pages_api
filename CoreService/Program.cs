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

builder.Services.Configure<FacebookClientOptions>(
    builder.Configuration.GetSection("Facebook"));

builder.Services.AddHttpClient<IFacebookApiClient, FacebookApiClient>(client =>
{
    var baseUrl = builder.Configuration["Facebook:BaseUrl"]
                  ?? "https://graph.facebook.com/v19.0/";
    if (!baseUrl.EndsWith("/")) baseUrl += "/";
    client.BaseAddress = new Uri(baseUrl);
});

builder.Services.AddDbContext<CoreDbContext>(opts =>
    opts.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

builder.Services.AddHttpClient("gemini", c =>
{
    c.DefaultRequestHeaders.Add("x-goog-api-key",
        builder.Configuration["Gemini:ApiKey"]);
});

builder.Services.AddSingleton<SpamDetector>();
builder.Services.AddScoped<AiAnalyzer>();
builder.Services.AddScoped<DecisionEngine>();
builder.Services.AddScoped<ActionExecutor>();
builder.Services.AddSingleton<IFailedEventPublisher, FailedEventPublisher>();
builder.Services.Configure<KafkaConsumerOptions>(
    builder.Configuration.GetSection("Kafka"));

builder.Services.AddHostedService<CoreEventConsumerService>();
builder.Services.AddHostedService<FailedEventRetryService>();

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
