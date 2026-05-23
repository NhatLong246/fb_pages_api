using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using WebhookService.Models;
using WebhookService.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRouting(options => options.LowercaseUrls = true);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.Configure<FacebookWebhookOptions>(builder.Configuration.GetSection("Facebook"));
builder.Services.Configure<KafkaOptions>(builder.Configuration.GetSection("Kafka"));
builder.Services.AddSingleton<FacebookCommentEventNormalizer>();
builder.Services.AddSingleton<IKafkaEventPublisher, KafkaEventPublisher>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "webhook-service" }));

app.MapGet("/webhook", (HttpRequest request, IOptions<FacebookWebhookOptions> options) =>
{
    var mode = request.Query["hub.mode"].ToString();
    var verifyToken = request.Query["hub.verify_token"].ToString();
    var challenge = request.Query["hub.challenge"].ToString();

    if (!string.Equals(mode, "subscribe", StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new { error = "Invalid hub.mode. Expected 'subscribe'." });
    }

    if (string.IsNullOrWhiteSpace(options.Value.VerifyToken))
    {
        return Results.Problem(
            detail: "Facebook verify token is not configured.",
            statusCode: StatusCodes.Status500InternalServerError);
    }

    if (!string.Equals(verifyToken, options.Value.VerifyToken, StringComparison.Ordinal))
    {
        return Results.StatusCode(403);
    }

    if (string.IsNullOrWhiteSpace(challenge))
    {
        return Results.BadRequest(new { error = "Missing hub.challenge." });
    }

    return Results.Text(challenge, "text/plain");
});

app.MapPost("/webhook", async (
    HttpRequest request,
    IOptions<FacebookWebhookOptions> options,
    FacebookCommentEventNormalizer normalizer,
    IKafkaEventPublisher kafkaPublisher,
    ILogger<Program> logger) =>
{
    if (string.IsNullOrWhiteSpace(options.Value.AppSecret))
    {
        return Results.Problem(
            detail: "Facebook app secret is not configured.",
            statusCode: StatusCodes.Status500InternalServerError);
    }

    var signatureHeader = request.Headers["X-Hub-Signature-256"].ToString();
    if (string.IsNullOrWhiteSpace(signatureHeader))
    {
        return Results.Unauthorized();
    }

    string rawBody;
    using (var reader = new StreamReader(request.Body, Encoding.UTF8))
    {
        rawBody = await reader.ReadToEndAsync();
    }

    if (string.IsNullOrWhiteSpace(rawBody))
    {
        return Results.BadRequest(new { error = "Request body is empty." });
    }

    if (!IsValidSignature(signatureHeader, rawBody, options.Value.AppSecret))
    {
        return Results.StatusCode(403);
    }

    JToken payload;
    try
    {
        payload = JToken.Parse(rawBody);
    }
    catch (Exception)
    {
        return Results.BadRequest(new { error = "Invalid JSON payload." });
    }

    var normalizedEvents = normalizer.NormalizeCommentEvents(payload, options.Value.PageId);
    if (normalizedEvents.Count == 0)
    {
        logger.LogInformation("Webhook accepted but no supported comment events were found.");
        return Results.Ok(new { received = true, normalized = 0, ignored = true });
    }

    logger.LogInformation(
        "Webhook normalized {EventCount} comment events. FirstEventId={FirstEventId}",
        normalizedEvents.Count,
        normalizedEvents[0].EventId);

    try
    {
        await kafkaPublisher.PublishAsync(normalizedEvents, request.HttpContext.RequestAborted);
    }
    catch (Exception ex)
    {
        logger.LogError(
            ex,
            "Failed to publish normalized events to Kafka. FirstEventId={FirstEventId}",
            normalizedEvents[0].EventId);

        return Results.Problem(
            detail: "Failed to publish event to Kafka.",
            statusCode: StatusCodes.Status500InternalServerError);
    }

    return Results.Ok(new
    {
        received = true,
        normalized = normalizedEvents.Count,
        published = normalizedEvents.Count,
        topic = builder.Configuration["Kafka:Topic"],
        eventIds = normalizedEvents.Select(e => e.EventId).ToArray()
    });
});

app.Run();

static bool IsValidSignature(string signatureHeader, string rawBody, string appSecret)
{
    const string Prefix = "sha256=";
    if (!signatureHeader.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    var signatureHex = signatureHeader[Prefix.Length..];
    byte[] providedSignature;
    try
    {
        providedSignature = Convert.FromHexString(signatureHex);
    }
    catch (FormatException)
    {
        return false;
    }

    using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(appSecret));
    var computedSignature = hmac.ComputeHash(Encoding.UTF8.GetBytes(rawBody));

    return providedSignature.Length == computedSignature.Length
        && CryptographicOperations.FixedTimeEquals(providedSignature, computedSignature);
}
