using System.Collections.Concurrent;

namespace GenAI.Services.Agent
{
    /// <summary>
    /// Thread-safe in-memory context store. Contents are lost when the process restarts;
    /// swap for a distributed implementation (e.g. Redis) when scaling out.
    /// </summary>
    public sealed class InMemoryContextStore : IContextStore
    {
        /// <summary>Upper bound on stored context, guarding against unbounded growth.</summary>
        public const int MaxContextLength = 8000;

        private readonly ConcurrentDictionary<string, string> _contexts = new();

        public string? Get(string conversationId) =>
            _contexts.TryGetValue(conversationId, out var context) ? context : null;

        public void Save(string conversationId, string context) =>
            _contexts[conversationId] = Trim(context);

        public string Append(string conversationId, string context) =>
            _contexts.AddOrUpdate(
                conversationId,
                _ => Trim(context),
                (_, existing) => Trim($"{existing}{Environment.NewLine}{context}"));

        public bool Clear(string conversationId) => _contexts.TryRemove(conversationId, out _);

        /// <summary>Keeps the most recent content when the context exceeds the limit.</summary>
        private static string Trim(string context) =>
            context.Length <= MaxContextLength
                ? context
                : context[^MaxContextLength..];
    }
}
