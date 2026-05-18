using KgWebApi.Net.Data;
using KuGou.Net.Protocol.Session;
using KgWebApi.Net.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using Scalar.AspNetCore;
using System.Net;

var builder = WebApplication.CreateBuilder(args);

var configuredConnectionString = builder.Configuration.GetConnectionString("KgWebApi");
var sqliteConnectionString = !string.IsNullOrWhiteSpace(configuredConnectionString)
    ? configuredConnectionString
    : $"Data Source={Path.Combine(builder.Environment.ContentRootPath, "App_Data", "kgwebapi.multisession.db")}";


// 注册控制器
builder.Services
    .AddControllers();

builder.Services
    .AddHttpClient(WebApiKuGouHttpClientNames.Outbound, client =>
    {
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.Connection.Add("keep-alive");
    })
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
        MaxConnectionsPerServer = 64
    })
    .SetHandlerLifetime(TimeSpan.FromMinutes(5));
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IKgWebSessionContext, KgWebSessionContext>();
builder.Services.AddDbContext<KgWebApiDbContext>(options => options.UseSqlite(sqliteConnectionString));

builder.Services.AddScoped<ISessionPersistence, KgWebSessionPersistence>();
builder.Services.AddScoped(_ => new CookieContainer());
builder.Services.AddWebApiKuGouServices();
builder.Services.AddOutputCache();

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info = new OpenApiInfo
        {
            Title = "KuGou Music API",
            Version = "v1",
            Description = "酷狗音乐API"
        };
        return Task.CompletedTask;
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<KgWebApiDbContext>();
    Directory.CreateDirectory(Path.Combine(builder.Environment.ContentRootPath, "App_Data"));
    dbContext.Database.EnsureCreated();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options.WithTitle("API 文档");
        options.WithTheme(ScalarTheme.Moon);
    });
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseMiddleware<KgWebSessionMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.UseOutputCache();
app.MapControllers();

app.Run();
