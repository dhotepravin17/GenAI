using GenAI.Models.Agent;

namespace GenAI.Services.Agent
{
    /// <summary>Orchestrates chat exchanges with the Azure AI Foundry model deployment.</summary>
    public interface IAgentService
    {
        /// <summary>Sends a user message to the agent and returns its reply.</summary>
        Task<AgentChatResponse> ChatAsync(AgentChatRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends a user message and streams the reply back piece by piece,
        /// so a client can render text as it is generated.
        /// </summary>
        IAsyncEnumerable<AgentStreamChunk> ChatStreamingAsync(AgentChatRequest request, CancellationToken cancellationToken = default);

        /// <summary>Deletes a conversation's history. Returns true if it existed.</summary>
        bool EndConversation(string conversationId);
    }
}
