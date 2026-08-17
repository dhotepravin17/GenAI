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
builder.Services.AddAzureFoundryAgent();

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

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
