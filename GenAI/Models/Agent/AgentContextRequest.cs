using System.ComponentModel.DataAnnotations;

namespace GenAI.Models.Agent
{
    /// <summary>Context to store against a conversation.</summary>
    public sealed class AgentContextRequest
    {
        /// <summary>
        /// Background information the agent should treat as known, for example
        /// "The user is a .NET developer working on the Kahramaa registration API".
        /// </summary>
        [Required]
        [StringLength(8000, MinimumLength = 1)]
        public string Context { get; init; } = string.Empty;
    }
}
