namespace GenAI.Models.Agent
{
    /// <summary>The context currently stored for a conversation.</summary>
    public sealed class AgentContextResponse
    {
        /// <summary>Conversation the context belongs to.</summary>
        public required string ConversationId { get; init; }

        /// <summary>Stored context, or null when none is set.</summary>
        public string? Context { get; init; }

        /// <summary>Length of the stored context in characters.</summary>
        public int Length => Context?.Length ?? 0;
    }
}
