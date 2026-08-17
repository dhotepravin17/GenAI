namespace GenAI.Models.Agent
{
    /// <summary>Agent reply returned to the caller.</summary>
    public sealed class AgentChatResponse
    {
        /// <summary>Conversation identifier; send it back to continue the same conversation.</summary>
        public required string ConversationId { get; init; }

        /// <summary>The agent's reply text.</summary>
        public required string Reply { get; init; }

        /// <summary>Model deployment that produced the reply.</summary>
        public required string Model { get; init; }

        /// <summary>Total tokens consumed by this exchange (prompt + completion), when reported.</summary>
        public int? TotalTokens { get; init; }
    }
}
