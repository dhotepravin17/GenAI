using System.Collections.Concurrent;
using GenAI.Models.Agent;

namespace GenAI.Services.Agent
{
    /// <summary>
    /// In-memory trace recorder. The active run is tracked with <see cref="AsyncLocal{T}"/>
    /// so tools invoked deep inside the agent pipeline record against the right
    /// conversation without having to pass it around.
    /// </summary>
    public sealed class AgentTraceRecorder : IAgentTraceRecorder
    {
        /// <summary>Maximum entries kept per conversation; oldest are dropped first.</summary>
        public const int MaxEntriesPerConversation = 200;

        private static readonly AsyncLocal<RunScope?> CurrentRun = new();

        private readonly ConcurrentDictionary<string, List<AgentTraceEntry>> _traces = new();

        public AgentTraceRecorder(bool isEnabled) => IsEnabled = isEnabled;

        public bool IsEnabled { get; }

        public IDisposable BeginRun(string conversationId, ConcurrentQueue<AgentTraceEntry>? liveQueue = null)
        {
            if (!IsEnabled)
            {
                return NullScope.Instance;
            }

            var scope = new RunScope(conversationId, liveQueue, CurrentRun.Value);
            CurrentRun.Value = scope;
            return scope;
        }

        public void Record(string category, string title, string? detail = null, int? durationMs = null)
        {
            var scope = CurrentRun.Value;
            if (!IsEnabled || scope is null)
            {
                return;
            }

            var entry = new AgentTraceEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Timestamp = DateTimeOffset.UtcNow,
                Category = category,
                Title = title,
                Detail = detail,
                DurationMs = durationMs
            };

            var entries = _traces.GetOrAdd(scope.ConversationId, _ => new List<AgentTraceEntry>());
            lock (entries)
            {
                entries.Add(entry);
                if (entries.Count > MaxEntriesPerConversation)
                {
                    entries.RemoveRange(0, entries.Count - MaxEntriesPerConversation);
                }
            }

            scope.LiveQueue?.Enqueue(entry);
        }

        public IReadOnlyList<AgentTraceEntry> GetTrace(string conversationId)
        {
            if (!_traces.TryGetValue(conversationId, out var entries))
            {
                return Array.Empty<AgentTraceEntry>();
            }

            lock (entries)
            {
                return entries.ToList();
            }
        }

        public bool Clear(string conversationId) => _traces.TryRemove(conversationId, out _);

        /// <summary>Restores the previous run when disposed, so nested runs behave sensibly.</summary>
        private sealed class RunScope : IDisposable
        {
            private readonly RunScope? _previous;

            public RunScope(string conversationId, ConcurrentQueue<AgentTraceEntry>? liveQueue, RunScope? previous)
            {
                ConversationId = conversationId;
                LiveQueue = liveQueue;
                _previous = previous;
            }

            public string ConversationId { get; }

            public ConcurrentQueue<AgentTraceEntry>? LiveQueue { get; }

            public void Dispose() => CurrentRun.Value = _previous;
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
