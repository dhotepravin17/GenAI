using System.ComponentModel.DataAnnotations;

namespace GenAI.Models.Agent
{
    /// <summary>Incoming chat message for the agent.</summary>
    public sealed class AgentChatRequest
    {
        /// <summary>User message to send to the agent.</summary>
        [Required]
        [StringLength(8000, MinimumLength = 1)]
        public string Message { get; init; } = string.Empty;

        /// <summary>
        /// Optional conversation identifier. Omit to start a new conversation;
        /// pass the value returned by a previous response to continue it.
        /// </summary>
        [StringLength(64)]
        [RegularExpression("^[a-zA-Z0-9-]*$", ErrorMessage = "ConversationId may contain only letters, digits and dashes.")]
        public string? ConversationId { get; init; }
    }
}
