using System.Text.Json;
using FileStoringService.Repositories;
using FileStoringService.HealthChecks;
using FileStoringService.Filters;
using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.WriteIndented = true;
        options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    });

builder.Services.AddEndpointsApiExplorer();

// Configure Swagger/OpenAPI
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "File Storing Service API",
        Version = "v1",
        Description = "Service for storing and retrieving student works",
        Contact = new OpenApiContact
        {
            Name = "File Storing Service Team",
            Email = "support@filestoring.example.com"
        },
        License = new OpenApiLicense
        {
            Name = "MIT License",
            Url = new Uri("https://opensource.org/licenses/MIT")
        }
    });
    
    // Добавляем поддержку аннотаций
    c.EnableAnnotations();
    
    // Добавляем фильтр для работы с workId
    c.ParameterFilter<WorkIdParameterFilter>();
    
    // Добавляем XML комментарии (если есть)
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        c.IncludeXmlComments(xmlPath);
    }
    
    // Настраиваем Bearer Auth (опционально)
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// Register repository as singleton (in-memory)
builder.Services.AddSingleton<IWorkRepository>(provider =>
{
    var logger = provider.GetRequiredService<ILogger<InMemoryWorkRepository>>();
    return new InMemoryWorkRepository(logger);
});

// Register Health Checks
builder.Services.AddHealthChecks()
    .AddCheck<FileStorageHealthCheck>("file_storage")
    .AddCheck<MemoryHealthCheck>("memory");

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();
builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));

// Configuration
builder.Services.Configure<StorageOptions>(
    builder.Configuration.GetSection("Storage"));

// Register HttpClient for inter-service communication
builder.Services.AddHttpClient("FileAnalysisService", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["FileAnalysisService:BaseUrl"] 
        ?? "http://localhost:5002");
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    client.Timeout = TimeSpan.FromSeconds(30);
});

// Add CORS for development
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseCors("AllowAll");
    
    app.UseSwagger(c =>
    {
        c.RouteTemplate = "api-docs/{documentName}/swagger.json";
        c.PreSerializeFilters.Add((swaggerDoc, httpReq) =>
        {
            swaggerDoc.Servers = new List<OpenApiServer>
            {
                new OpenApiServer { Url = $"{httpReq.Scheme}://{httpReq.Host.Value}" },
                new OpenApiServer { Url = "http://localhost:5001" },
                new OpenApiServer { Url = "http://file-storing-service:8080" }
            };
        });
    });
    
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/api-docs/v1/swagger.json", "File Storing Service API v1");
        c.RoutePrefix = "api-docs";
        c.DocumentTitle = "File Storing Service API Documentation";
        c.DisplayRequestDuration();
        c.EnableDeepLinking();
        c.DefaultModelsExpandDepth(-1); // Скрыть модели по умолчанию
    });
}
else
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}

// Global exception handler middleware
app.UseExceptionHandler(exceptionHandlerApp =>
{
    exceptionHandlerApp.Run(async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        var correlationId = Guid.NewGuid().ToString();
        
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";
        
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogError(exception, "[{CorrelationId}] Unhandled exception occurred", correlationId);
        
        var problemDetails = new ProblemDetails
        {
            Title = "An unexpected error occurred",
            Status = StatusCodes.Status500InternalServerError,
            Detail = app.Environment.IsDevelopment() ? exception?.Message : "Please contact support",
            Instance = context.Request.Path,
            Extensions = new Dictionary<string, object?>
            {
                ["correlationId"] = correlationId,
                ["timestamp"] = DateTime.UtcNow,
                ["traceId"] = context.TraceIdentifier
            }
        };
        
        context.Response.Headers.Append("X-Correlation-ID", correlationId);
        await context.Response.WriteAsJsonAsync(problemDetails);
    });
});

// Request logging middleware
app.Use(async (context, next) =>
{
    var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault() 
        ?? Guid.NewGuid().ToString();
    
    context.Items["CorrelationId"] = correlationId;
    context.Response.Headers.Append("X-Correlation-ID", correlationId);
    
    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    
    try
    {
        logger.LogInformation(
            "[{CorrelationId}] {Method} {Path} started from {RemoteIp}",
            correlationId, 
            context.Request.Method, 
            context.Request.Path,
            context.Connection.RemoteIpAddress);
        
        await next();
        
        stopwatch.Stop();
        
        logger.LogInformation(
            "[{CorrelationId}] {Method} {Path} completed in {ElapsedMs}ms with status {StatusCode}",
            correlationId, 
            context.Request.Method, 
            context.Request.Path,
            stopwatch.ElapsedMilliseconds, 
            context.Response.StatusCode);
    }
    catch (Exception)
    {
        stopwatch.Stop();
        logger.LogError(
            "[{CorrelationId}] {Method} {Path} failed after {ElapsedMs}ms",
            correlationId, 
            context.Request.Method, 
            context.Request.Path,
            stopwatch.ElapsedMilliseconds);
        throw;
    }
});

// Health check endpoint
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        
        var result = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                duration = e.Value.Duration.TotalMilliseconds,
                description = e.Value.Description,
                data = e.Value.Data
            }),
            totalDuration = report.TotalDuration.TotalMilliseconds
        };
        
        await context.Response.WriteAsJsonAsync(result);
    }
});

// Simple status endpoint
app.MapGet("/", () => Results.Ok(new
{
    service = "File Storing Service",
    version = "1.0.0",
    status = "running",
    timestamp = DateTime.UtcNow,
    endpoints = new[]
    {
        new { path = "/api/files", methods = new[] { "POST", "GET" } },
        new { path = "/api/files/{id}/meta", methods = new[] { "GET" } },
        new { path = "/api/files/{id}/download", methods = new[] { "GET" } },
        new { path = "/health", methods = new[] { "GET" } },
        new { path = "/api-docs", methods = new[] { "GET" } }
    }
}));

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();

// Configuration classes
public class StorageOptions
{
    public string Path { get; set; } = "storage";
    public long MaxFileSize { get; set; } = 10485760; // 10 MB
    public string[] AllowedExtensions { get; set; } = { ".txt", ".pdf", ".doc", ".docx", ".zip" };
}