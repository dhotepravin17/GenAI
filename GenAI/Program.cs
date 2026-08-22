using GenAI.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Azure Key Vault overrides local settings when configured; missing secrets
// fall back to appsettings.json / user-secrets / environment variables.
builder.AddAzureKeyVaultConfiguration();

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Azure AI Foundry agent (configuration, client and services).
builder.Services.AddAzureFoundryAgent(builder.Configuration);

// Allow the React dev server to call the API during development.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicyName, policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod());
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    // Swagger UI at /swagger, backed by the built-in OpenAPI document.
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "GenAI API v1");
    });
}

app.UseCors(CorsPolicyName);

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();

partial class Program
{
    /// <summary>Name of the CORS policy used by the React client.</summary>
    private const string CorsPolicyName = "GenAIClient";
}
