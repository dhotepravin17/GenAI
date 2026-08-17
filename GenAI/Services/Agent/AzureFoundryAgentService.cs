using Azure.AI.OpenAI;
using GenAI.Configuration;
using GenAI.Models.Agent;
using GenAI.Services.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace GenAI.Services.Agent
{
    /// <summary>
    /// Agent that talks to the Azure AI Foundry deployment through the
    /// <see cref="IChatClient"/> abstraction, managing conversation history itself
    /// via <see cref="IConversationStore"/>.
    /// <para>
    /// This is the lighter alternative to <see cref="AgentFrameworkAgentService"/>:
    /// it keeps full control of the message list instead of delegating to agent sessions.
    /// </para>
    /// </summary>
    public sealed class AzureFoundryAgentService : IAgentService
    {
        private readonly IChatClient _chatClient;
        private readonly IConversationStore _conversationStore;
        private readonly ChatOptions _chatOptions;
        private readonly AzureAIFoundryOptions _options;
        private readonly ILogger<AzureFoundryAgentService> _logger;

        public AzureFoundryAgentService(
            AzureOpenAIClient client,
            IConversationStore conversationStore,
            AgentTools tools,
            IOptions<AzureAIFoundryOptions> options,
            ILogger<AzureFoundryAgentService> logger)
        {
            _conversationStore = conversationStore;
            _options = options.Value;
            _logger = logger;

            // UseFunctionInvocation runs the tool-call loop, so tools work on this
            // path exactly as they do on the Agent Framework path.
            _chatClient = client
                .GetChatClient(_options.DeploymentName)
                .AsIChatClient()
                .AsBuilder()
                .UseFunctionInvocation()
                .Build();

            _chatOptions = new ChatOptions
            {
                MaxOutputTokens = _options.MaxOutputTokens,
                Temperature = _options.Temperature,
                Tools = tools.CreateTools()
            };
        }

        public async Task<AgentChatResponse> ChatAsync(AgentChatRequest request, CancellationToken cancellationToken = default)
        {
            var conversationId = string.IsNullOrWhiteSpace(request.ConversationId)
                ? Guid.NewGuid().ToString("N")
                : request.ConversationId;

            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, _options.SystemPrompt)
            };
            messages.AddRange(_conversationStore.GetHistory(conversationId));

            var userMessage = new ChatMessage(ChatRole.User, request.Message);
            messages.Add(userMessage);

            _logger.LogInformation(
                "Sending chat request for conversation {ConversationId} with {MessageCount} messages.",
                conversationId,
                messages.Count);

            ChatResponse response = await _chatClient.GetResponseAsync(messages, _chatOptions, cancellationToken);

            var reply = response.Text;

            _conversationStore.Append(
                conversationId,
                [userMessage, new ChatMessage(ChatRole.Assistant, reply)],
                _options.MaxHistoryMessages);

            return new AgentChatResponse
            {
                ConversationId = conversationId,
                Reply = reply,
                Model = response.ModelId ?? _options.DeploymentName,
                TotalTokens = (int?)response.Usage?.TotalTokenCount
            };
        }

        public bool EndConversation(string conversationId) => _conversationStore.Remove(conversationId);
    }
}
