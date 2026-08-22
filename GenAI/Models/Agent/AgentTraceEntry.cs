namespace GenAI.Models.Agent
{
    /// <summary>
    /// One recorded step of an agent run, used by the raw message window to show
    /// what happened behind a reply: the request sent, tool calls, the model's
    /// response and what was written to memory.
    /// </summary>
    public sealed class AgentTraceEntry
    {
        /// <summary>Unique id so a client can de-duplicate entries.</summary>
        public required string Id { get; init; }

        /// <summary>When the step was recorded.</summary>
        public required DateTimeOffset Timestamp { get; init; }

        /// <summary>Step category: request, model, tool, memory or error.</summary>
        public required string Category { get; init; }

        /// <summary>Short one-line description of the step.</summary>
        public required string Title { get; init; }

        /// <summary>Optional payload, e.g. tool arguments or the result summary.</summary>
        public string? Detail { get; init; }

        /// <summary>How long the step took, when it is a measured operation.</summary>
        public int? DurationMs { get; init; }
    }

    /// <summary>Well-known <see cref="AgentTraceEntry.Category"/> values.</summary>
    public static class AgentTraceCategory
    {
        public const string Request = "request";
        public const string Model = "model";
        public const string Tool = "tool";
        public const string Memory = "memory";
    }
}
