using Microsoft.Extensions.AI;

namespace GenAI.Services.Agent
{
    /// <summary>Stores per-conversation chat history.</summary>
    public interface IConversationStore
    {
        /// <summary>Returns the history for a conversation, or an empty list if none exists.</summary>
        IReadOnlyList<ChatMessage> GetHistory(string conversationId);

        /// <summary>Appends messages to a conversation, trimming to <paramref name="maxMessages"/>.</summary>
        void Append(string conversationId, IEnumerable<ChatMessage> messages, int maxMessages);

        /// <summary>Removes a conversation. Returns true if it existed.</summary>
        bool Remove(string conversationId);
    }
}
