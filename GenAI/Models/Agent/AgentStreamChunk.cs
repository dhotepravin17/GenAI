namespace GenAI.Models.Agent
{
    /// <summary>One server-sent event in a streaming chat response.</summary>
    public sealed class AgentStreamChunk
    {
        /// <summary>Conversation the chunk belongs to; sent on every chunk so the client can store it.</summary>
        public required string ConversationId { get; init; }

        /// <summary>Next piece of reply text, or null on the final chunk.</summary>
        public string? Delta { get; init; }

        /// <summary>True on the last chunk, signalling the client that the reply is complete.</summary>
        public bool IsFinal { get; init; }

        /// <summary>Set when the run failed; the client should show this instead of a reply.</summary>
        public string? Error { get; init; }

        /// <summary>
        /// A background step (request sent, tool call, memory write) emitted as it
        /// happens, so the client can show a live trace alongside the reply.
        /// </summary>
        public AgentTraceEntry? Trace { get; init; }
    }
}
