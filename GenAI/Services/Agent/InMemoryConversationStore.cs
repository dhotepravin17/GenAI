using System.Collections.Concurrent;
using Microsoft.Extensions.AI;

namespace GenAI.Services.Agent
{
    /// <summary>
    /// Thread-safe in-memory conversation store. Suitable for a single instance;
    /// swap for a distributed implementation (e.g. Redis) when scaling out.
    /// </summary>
    public sealed class InMemoryConversationStore : IConversationStore
    {
        private readonly ConcurrentDictionary<string, List<ChatMessage>> _conversations = new();

        public IReadOnlyList<ChatMessage> GetHistory(string conversationId)
        {
            if (_conversations.TryGetValue(conversationId, out var history))
            {
                lock (history)
                {
                    return history.ToList();
                }
            }

            return Array.Empty<ChatMessage>();
        }

        public void Append(string conversationId, IEnumerable<ChatMessage> messages, int maxMessages)
        {
            var history = _conversations.GetOrAdd(conversationId, _ => new List<ChatMessage>());
            lock (history)
            {
                history.AddRange(messages);
                if (history.Count > maxMessages)
                {
                    history.RemoveRange(0, history.Count - maxMessages);
                }
            }
        }

        public bool Remove(string conversationId) => _conversations.TryRemove(conversationId, out _);
    }
}
