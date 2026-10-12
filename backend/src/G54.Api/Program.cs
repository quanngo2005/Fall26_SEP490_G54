using G54.Api.Health;
using G54.Api.Middleware;
using G54.Api.Services;
using G54.BLL;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.OpenApi.Models;
using Serilog;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);
builder.Host.UseSerilog((context, configuration) => configuration.ReadFrom.Configuration(context.Configuration));

var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey) || System.Text.Encoding.UTF8.GetByteCount(jwtKey) < 32)
{
    throw new InvalidOperationException("Jwt:Key must contain at least 256 bits of key material.");
}

var jwtKeyIsPlaceholder = jwtKey.StartsWith("LOCAL_DEVELOPMENT_KEY_", StringComparison.Ordinal)
    || jwtKey.StartsWith("REPLACE_WITH_A_SECURE_RANDOM_SECRET", StringComparison.Ordinal);
if (!builder.Environment.IsDevelopment() && jwtKeyIsPlaceholder)
{
    throw new InvalidOperationException("A non-development Jwt:Key must be provided through secure configuration.");
}

var databaseConnection = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required.");
var redisConnection = builder.Configuration.GetConnectionString("Redis")
    ?? throw new InvalidOperationException("ConnectionStrings:Redis is required.");

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddBusinessLogic();
builder.Services.AddDataAccess(databaseConnection);
builder.Services.AddAuthenticationDataAccess(databaseConnection);
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnection));
builder.Services.AddStackExchangeRedisCache(options => options.Configuration = redisConnection);
builder.Services.AddSingleton<IAuthSessionStore, RedisAuthSessionStore>();
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("postgres", tags: ["ready"])
    .AddCheck<RedisHealthCheck>("redis", tags: ["ready"]);
builder.Services.AddCors(options => options.AddPolicy("Frontend", policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:4200"])
    .AllowAnyHeader()
    .AllowAnyMethod()));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.MapInboundClaims = false;
    options.TokenValidationParameters = new JwtTokenProvider(builder.Configuration).CreateValidationParameters();
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            var tokenType = context.Principal?.FindFirst("token_type")?.Value;
            var tokenId = context.Principal?.FindFirst("jti")?.Value;
            if (tokenType != "access" || !Guid.TryParse(tokenId, out var parsedTokenId))
            {
                context.Fail("Invalid access token.");
                return;
            }

            var cache = context.HttpContext.RequestServices.GetRequiredService<IDistributedCache>();
            if (await cache.GetStringAsync(AuthService.CreateAccessBlacklistKey(parsedTokenId), context.HttpContext.RequestAborted)
                is not null)
            {
                context.Fail("Access token has been revoked.");
            }
        },
    };
});
builder.Services.AddAuthorization();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "G54 API", Version = "v1" });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
    });
});

var app = builder.Build();
app.UseExceptionHandler();
app.UseSerilogRequestLogging();
app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.XFrameOptions = "DENY";
    await next();
});
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHsts();
}
app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/health", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new() { Predicate = check => check.Tags.Contains("ready") });
app.MapControllers();
app.Run();

public partial class Program;
