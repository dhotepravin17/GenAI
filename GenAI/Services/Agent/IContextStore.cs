namespace GenAI.Services.Agent
{
    /// <summary>
    /// Holds extra background context per conversation, kept in the API's memory.
    /// The context is added to the agent's instructions on every run, so the agent
    /// treats it as standing knowledge rather than a chat message.
    /// </summary>
    public interface IContextStore
    {
        /// <summary>Returns the stored context, or null when none is set.</summary>
        string? Get(string conversationId);

        /// <summary>Replaces any stored context with <paramref name="context"/>.</summary>
        void Save(string conversationId, string context);

        /// <summary>
        /// Appends to the stored context on a new line, or saves it when nothing is stored yet.
        /// Returns the resulting context.
        /// </summary>
        string Append(string conversationId, string context);

        /// <summary>Removes the stored context. Returns true if there was any.</summary>
        bool Clear(string conversationId);
    }
}
