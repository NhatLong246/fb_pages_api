using BackendApi.Data;
using BackendApi.Models;
using BackendApi.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// Add services to the container.

builder.Services.AddControllers();

// Configure Facebook Options
builder.Services.Configure<FacebookOptions>(builder.Configuration.GetSection("Facebook"));
builder.Services.Configure<KafkaOptions>(builder.Configuration.GetSection("Kafka"));
builder.Services.Configure<CircuitBreakerOptions>(builder.Configuration.GetSection("CircuitBreaker"));
builder.Services.Configure<DashboardOptions>(builder.Configuration.GetSection("Dashboard"));
builder.Services.AddSingleton<FacebookApiCircuitBreaker>();
builder.Services.AddDbContext<BackendDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));
builder.Services.AddHostedService<FacebookActionWorker>();

// Register Facebook Service and HttpClient
builder.Services.AddHttpClient<BackendApi.Services.IFacebookService, BackendApi.Services.FacebookService>(client =>
{
    var baseUrl = builder.Configuration.GetValue<string>("Facebook:BaseUrl") ?? "https://graph.facebook.com/v19.0/";
    if (!baseUrl.EndsWith("/")) baseUrl += "/";
    client.BaseAddress = new Uri(baseUrl);
});

// Configure lowercase URLs
builder.Services.AddRouting(options => options.LowercaseUrls = true);

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/api/page"))
    {
        await next();
        return;
    }

    var options = context.RequestServices
        .GetRequiredService<IOptions<DashboardOptions>>()
        .Value;
    if (string.IsNullOrWhiteSpace(options.AdminApiKey) ||
        options.AdminApiKey == "YOUR_ADMIN_API_KEY")
    {
        await next();
        return;
    }

    if (!context.Request.Headers.TryGetValue("X-Admin-Key", out var provided) ||
        !string.Equals(provided, options.AdminApiKey, StringComparison.Ordinal))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = "Missing or invalid X-Admin-Key." });
        return;
    }

    await next();
});

app.MapControllers();

app.Run();
