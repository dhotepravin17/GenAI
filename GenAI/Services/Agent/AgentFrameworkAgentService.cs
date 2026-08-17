using System.Collections.Concurrent;
using Azure.AI.OpenAI;
using GenAI.Configuration;
using GenAI.Models.Agent;
using GenAI.Services.Tools;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI.Chat;

namespace GenAI.Services.Agent
{
    /// <summary>
    /// Agent built on the Microsoft Agent Framework (<see cref="AIAgent"/>) over the
    /// Azure AI Foundry model deployment, authenticated with an API key.
    /// Conversation context is carried by per-conversation <see cref="AgentSession"/> instances.
    /// </summary>
    public sealed class AgentFrameworkAgentService : IAgentService
    {
        private readonly AIAgent _agent;
        private readonly AzureAIFoundryOptions _options;
        private readonly ILogger<AgentFrameworkAgentService> _logger;
        private readonly ConcurrentDictionary<string, AgentSession> _sessions = new();

        public AgentFrameworkAgentService(
            AzureOpenAIClient client,
            AgentTools tools,
            IOptions<AzureAIFoundryOptions> options,
            ILogger<AgentFrameworkAgentService> logger)
        {
            _options = options.Value;
            _logger = logger;

            // Tools are invoked automatically by the framework when the model requests them.
            _agent = client
                .GetChatClient(_options.DeploymentName)
                .AsAIAgent(new ChatClientAgentOptions
                {
                    Name = "GenAIAgent",
                    ChatOptions = new ChatOptions
                    {
                        Instructions = _options.SystemPrompt,
                        MaxOutputTokens = _options.MaxOutputTokens,
                        Temperature = _options.Temperature,
                        Tools = tools.CreateTools()
                    }
                });
        }

        public async Task<AgentChatResponse> ChatAsync(AgentChatRequest request, CancellationToken cancellationToken = default)
        {
            var conversationId = string.IsNullOrWhiteSpace(request.ConversationId)
                ? Guid.NewGuid().ToString("N")
                : request.ConversationId;

            if (!_sessions.TryGetValue(conversationId, out var session))
            {
                var newSession = await _agent.CreateSessionAsync(cancellationToken);
                session = _sessions.GetOrAdd(conversationId, newSession);
            }

            _logger.LogInformation("Running agent for conversation {ConversationId}.", conversationId);

            AgentResponse response = await _agent.RunAsync(request.Message, session, cancellationToken: cancellationToken);

            return new AgentChatResponse
            {
                ConversationId = conversationId,
                Reply = response.Text,
                Model = _options.DeploymentName,
                TotalTokens = (int?)response.Usage?.TotalTokenCount
            };
        }

        public bool EndConversation(string conversationId) => _sessions.TryRemove(conversationId, out _);
    }
}
