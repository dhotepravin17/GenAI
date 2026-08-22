using System.Collections.Concurrent;
using GenAI.Models.Agent;

namespace GenAI.Services.Agent
{
    /// <summary>
    /// Records what happens during an agent run so it can be inspected afterwards.
    /// A run is scoped with <see cref="BeginRun"/>; calls to <see cref="Record"/>
    /// anywhere inside that run (including from tools) are attributed to it.
    /// </summary>
    public interface IAgentTraceRecorder
    {
        /// <summary>True when tracing is switched on by configuration.</summary>
        bool IsEnabled { get; }

        /// <summary>
        /// Marks the start of a run. Entries recorded until the returned scope is
        /// disposed belong to <paramref name="conversationId"/>. When
        /// <paramref name="liveQueue"/> is supplied, entries are also queued there
        /// so a streaming endpoint can forward them to the client as they happen.
        /// </summary>
        IDisposable BeginRun(string conversationId, ConcurrentQueue<AgentTraceEntry>? liveQueue = null);

        /// <summary>Records one step of the current run. No-op outside a run or when disabled.</summary>
        void Record(string category, string title, string? detail = null, int? durationMs = null);

        /// <summary>Returns the recorded trace for a conversation, oldest first.</summary>
        IReadOnlyList<AgentTraceEntry> GetTrace(string conversationId);

        /// <summary>Discards the trace for a conversation. Returns true if there was one.</summary>
        bool Clear(string conversationId);
    }
}
