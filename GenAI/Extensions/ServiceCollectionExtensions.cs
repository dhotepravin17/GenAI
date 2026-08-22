using Azure;
using Azure.AI.OpenAI;
using GenAI.Configuration;
using GenAI.Services.Agent;
using GenAI.Services.Tools;
using Microsoft.Extensions.Options;

namespace GenAI.Extensions
{
    /// <summary>Dependency injection registrations for the Azure AI Foundry agent.</summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers configuration, the Azure OpenAI client and the agent services.
        /// Configuration is validated at startup so a missing endpoint or API key
        /// fails fast instead of at first request.
        /// </summary>
        public static IServiceCollection AddAzureFoundryAgent(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddOptions<AzureAIFoundryOptions>()
                .BindConfiguration(AzureAIFoundryOptions.SectionName)
                .ValidateDataAnnotations()
                .ValidateOnStart();

            services.AddSingleton(sp =>
            {
                var options = sp.GetRequiredService<IOptions<AzureAIFoundryOptions>>().Value;
                return new AzureOpenAIClient(new Uri(options.Endpoint), new AzureKeyCredential(options.ApiKey));
            });

            services.AddSingleton<IConversationStore, InMemoryConversationStore>();
            services.AddSingleton<IContextStore, InMemoryContextStore>();

            // Tracing powers the raw message window; turn it off in production
            // where recording prompts and tool arguments is not appropriate.
            var traceEnabled = configuration.GetValue("Diagnostics:EnableAgentTrace", true);
            services.AddSingleton<IAgentTraceRecorder>(_ => new AgentTraceRecorder(traceEnabled));
            services.AddAgentTools();

            // Both agent implementations are registered; AzureAIFoundry:UseAgentFramework
            // selects which one backs IAgentService.
            services.AddSingleton<AzureFoundryAgentService>();
            services.AddSingleton<AgentFrameworkAgentService>();
            services.AddSingleton<IAgentService>(sp =>
            {
                var options = sp.GetRequiredService<IOptions<AzureAIFoundryOptions>>().Value;
                return options.UseAgentFramework
                    ? sp.GetRequiredService<AgentFrameworkAgentService>()
                    : sp.GetRequiredService<AzureFoundryAgentService>();
            });

            return services;
        }

        /// <summary>
        /// Registers the tools the agent can call, each with its own configured
        /// <see cref="HttpClient"/> so upstream calls cannot hang the request.
        /// </summary>
        private static IServiceCollection AddAgentTools(this IServiceCollection services)
        {
            services.AddHttpClient(WeatherService.HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(15);
            });

            services.AddHttpClient(CurrencyService.HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(15);
            });

            services.AddSingleton<IWeatherService, WeatherService>();
            services.AddSingleton<ICurrencyService, CurrencyService>();
            services.AddSingleton<AgentTools>();

            return services;
        }
    }
}
