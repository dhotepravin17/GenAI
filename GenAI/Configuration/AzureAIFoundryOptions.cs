using System.ComponentModel.DataAnnotations;

namespace GenAI.Configuration
{
    /// <summary>
    /// Strongly typed configuration for the Azure AI Foundry model deployment.
    /// The API key must be supplied via user-secrets or environment variables,
    /// never committed to source control.
    /// </summary>
    public sealed class AzureAIFoundryOptions
    {
        /// <summary>Configuration section name in appsettings.json.</summary>
        public const string SectionName = "AzureAIFoundry";

        /// <summary>
        /// Azure AI Foundry / Azure OpenAI resource endpoint,
        /// e.g. https://my-resource.openai.azure.com or https://my-resource.cognitiveservices.azure.com.
        /// </summary>
        [Required]
        [Url]
        public string Endpoint { get; init; } = string.Empty;

        /// <summary>
        /// API key for the Foundry resource. Load from user-secrets (dev) or
        /// environment variable AzureAIFoundry__ApiKey (prod). Never log this value.
        /// </summary>
        [Required]
        public string ApiKey { get; init; } = string.Empty;

        /// <summary>Name of the model deployment created in the Foundry portal (e.g. gpt-4o-mini).</summary>
        [Required]
        public string DeploymentName { get; init; } = string.Empty;

        /// <summary>Optional embedding model deployment (e.g. text-embedding-ada-002).</summary>
        public string? EmbeddingDeployment { get; init; }

        /// <summary>System instructions that define the agent's behavior.</summary>
        [Required]
        public string SystemPrompt { get; init; } =
            "You are a helpful assistant. Answer concisely and accurately.";

        /// <summary>Maximum tokens the model may generate per response.</summary>
        [Range(1, 16384)]
        public int MaxOutputTokens { get; init; } = 800;

        /// <summary>Sampling temperature; lower values give more deterministic answers.</summary>
        [Range(0.0, 2.0)]
        public float Temperature { get; init; } = 0.7f;

        /// <summary>Maximum number of past messages kept per conversation.</summary>
        [Range(2, 200)]
        public int MaxHistoryMessages { get; init; } = 20;

        /// <summary>
        /// true (default) uses the Microsoft Agent Framework implementation;
        /// false uses the direct chat-client implementation.
        /// </summary>
        public bool UseAgentFramework { get; init; } = true;
    }
}
