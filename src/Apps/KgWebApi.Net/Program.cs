using KgWebApi.Net.Data;
using KgWebApi.Net.Services;
using KuGou.Net.Infrastructure;
using KuGou.Net.Infrastructure.Http;
using KuGou.Net.Infrastructure.Http.Handlers;
using KuGou.Net.Protocol.Session;
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

builder.Services.AddHttpClient();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IKgWebSessionContext, KgWebSessionContext>();
builder.Services.AddDbContext<KgWebApiDbContext>(options => options.UseSqlite(sqliteConnectionString));

builder.Services.AddScoped<ISessionPersistence, KgWebSessionPersistence>();
builder.Services.AddScoped(_ => new CookieContainer());
builder.Services.AddScoped<KgSessionManager>();
builder.Services.AddScoped<KgSignatureHandler>();
builder.Services.AddScoped<IKgTransport, WebApiKgTransport>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});
builder.Services.AddWebApiKuGouServices();

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

app.UseMiddleware<KgWebSessionMiddleware>();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
