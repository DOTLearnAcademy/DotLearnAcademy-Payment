using DotLearn.Payment.Data;
using DotLearn.Payment.Middleware;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Security.Claims;
using Amazon;
using Kralizek.Extensions.Configuration;
using Amazon.SQS;
using DotLearn.Payment.Repositories;
using DotLearn.Payment.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add Serilog
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

// AWS Secrets Manager (Only in non-Development environments)
if (!builder.Environment.IsDevelopment())
{
    // builder.Configuration.AddSecretsManager(region: Amazon.RegionEndpoint.APSoutheast2);
}

// Add services to the container.
var connStr = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<PaymentDbContext>(options =>
    options.UseSqlServer(connStr));

builder.Services.AddHealthChecks().AddSqlServer(connStr);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddScoped<IPaymentRepository, PaymentRepository>();
builder.Services.AddScoped<PaymentService>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<SqsService>();
builder.Services.AddScoped<RazorpaySignatureService>();
builder.Services.AddDefaultAWSOptions(new Amazon.Extensions.NETCore.Setup.AWSOptions
{
    Region = Amazon.RegionEndpoint.APSoutheast2
});
builder.Services.AddAWSService<IAmazonSQS>();

// Authentication & Authorization — JWT Bearer
var jwksUri = builder.Configuration["Auth:JwksUri"];
var authority = jwksUri?.Replace("/.well-known/jwks.json", "");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = authority;
        options.RequireHttpsMetadata = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "dotlearn-auth",
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            NameClaimType = "sub",
            RoleClaimType = ClaimTypes.Role
        };
    });
builder.Services.AddAuthorization();

// CORS — allow localhost dev + EC2 production
builder.Services.AddCors(options =>
{
    options.AddPolicy("DotLearnPolicy", policy =>
        policy.WithOrigins(
                "http://localhost:4200",
                "https://localhost:4200",
                "http://3.27.174.183.nip.io",
                "https://3.27.174.183.nip.io",
                builder.Configuration["AllowedOrigins:Ec2"] ?? "",
                builder.Configuration["AllowedOrigins:CloudFront"] ?? "")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials());
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Middlewares
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ExceptionHandler>();

app.UseCors("DotLearnPolicy");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers(); // MapControllers FIRST
app.MapHealthChecks("/health"); // MapHealthChecks SECOND

// Auto-create / migrate DB schema on startup (idempotent)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();

    // If table exists but is missing critical columns (old schema), drop it so it gets recreated below
    db.Database.ExecuteSqlRaw(@"
        IF EXISTS (
            SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Payments]') AND type = N'U'
        )
        AND NOT EXISTS (
            SELECT 1 FROM sys.columns
            WHERE Name = N'TransactionId' AND Object_ID = OBJECT_ID(N'[dbo].[Payments]')
        )
        BEGIN
            DROP TABLE [dbo].[Payments]
        END
    ");

    // Create table with correct schema if it doesn't exist
    db.Database.ExecuteSqlRaw(@"
        IF NOT EXISTS (
            SELECT * FROM sys.objects
            WHERE object_id = OBJECT_ID(N'[dbo].[Payments]') AND type = N'U'
        )
        BEGIN
            CREATE TABLE [dbo].[Payments] (
                [Id]            uniqueidentifier NOT NULL,
                [StudentId]     uniqueidentifier NOT NULL,
                [CourseId]      uniqueidentifier NOT NULL,
                [Amount]        decimal(18,2)    NOT NULL,
                [Currency]      nvarchar(10)     NOT NULL DEFAULT 'INR',
                [Provider]      nvarchar(50)     NOT NULL DEFAULT 'razorpay',
                [TransactionId] nvarchar(200)    NOT NULL,
                [OrderId]       nvarchar(200)    NOT NULL DEFAULT '',
                [Status]        int              NOT NULL DEFAULT 0,
                [CreatedAt]     datetime2        NOT NULL DEFAULT GETUTCDATE(),
                [CompletedAt]   datetime2        NULL,
                CONSTRAINT [PK_Payments] PRIMARY KEY ([Id])
            )
        END
    ");

    // Unique index on TransactionId
    db.Database.ExecuteSqlRaw(@"
        IF NOT EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE Name = N'IX_Payments_TransactionId'
            AND Object_ID = OBJECT_ID(N'[dbo].[Payments]')
        )
        BEGIN
            CREATE UNIQUE INDEX [IX_Payments_TransactionId]
                ON [dbo].[Payments] ([TransactionId])
        END
    ");
}

app.Run();
