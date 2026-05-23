var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();

// Configure Facebook Options
builder.Services.Configure<BackendApi.Models.FacebookOptions>(builder.Configuration.GetSection("Facebook"));

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

app.MapControllers();

app.Run();
