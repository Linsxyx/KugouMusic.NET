using KgWebApi.Net.Data;
using KgWebApi.Net.Services;
using KuGou.Net.Infrastructure.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;
using System.Net;

var builder = WebApplication.CreateBuilder(args);

var logDir = Path.Combine(builder.Environment.ContentRootPath, "Logs");

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
    .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Extensions.Http", LogEventLevel.Warning)
    .Enrich.FromLogContext()
#if DEBUG
    .WriteTo.Async(a => a.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"))
#else
    .WriteTo.Async(a => a.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"))
    .WriteTo.Async(a => a.File(
        Path.Combine(logDir, "kgwebapi-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        retainedFileTimeLimit: TimeSpan.FromDays(30),
        fileSizeLimitBytes: 20 * 1024 * 1024,
        rollOnFileSizeLimit: true,
        shared: true,
        outputTemplate:
        "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"))
#endif
    .CreateLogger();

builder.Host.UseSerilog();

var composition = new WebApiKuGouServiceProvider();
builder.Host.UseServiceProviderFactory(composition);

var configuredConnectionString = builder.Configuration.GetConnectionString("KgWebApi");
var sqliteConnectionString = !string.IsNullOrWhiteSpace(configuredConnectionString)
    ? configuredConnectionString
    : $"Data Source={Path.Combine(builder.Environment.ContentRootPath, "App_Data", "kgwebapi.multisession.db")}";


// 注册控制器
builder.Services
    .AddMvc()
    .AddControllersAsServices();

builder.Services.AddHttpClient(WebApiKgHttpClientNames.KuGou)
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
        MaxConnectionsPerServer = 64
    })
    .SetHandlerLifetime(Timeout.InfiniteTimeSpan);
builder.Services.AddHttpContextAccessor();
builder.Services.AddDbContext<KgWebApiDbContext>(options => options.UseSqlite(sqliteConnectionString));
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

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

app.UseSerilogRequestLogging();
app.UseMiddleware<KgWebSessionMiddleware>();
app.UseHttpsRedirection();
//app.UseCors("AllowAll");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

try
{
    app.Run();
}
finally
{
    Log.CloseAndFlush();
}
