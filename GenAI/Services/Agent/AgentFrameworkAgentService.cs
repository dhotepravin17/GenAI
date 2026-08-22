using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Azure.AI.OpenAI;
using GenAI.Configuration;
using GenAI.Models.Agent;
using GenAI.Services.Tools;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI.Chat;

namespace GenAI.Services.Agent
{
    /// <summary>
    /// Agent built on the Microsoft Agent Framework (<see cref="AIAgent"/>) over the
    /// Azure AI Foundry model deployment, authenticated with an API key.
    /// Conversation context is carried by per-conversation <see cref="AgentSession"/> instances.
    /// </summary>
    public sealed class AgentFrameworkAgentService : IAgentService
    {
        private readonly AIAgent _agent;
        private readonly IContextStore _contextStore;
        private readonly IAgentTraceRecorder _trace;
        private readonly ChatOptions _baseChatOptions;
        private readonly AzureAIFoundryOptions _options;
        private readonly ILogger<AgentFrameworkAgentService> _logger;
        private readonly ConcurrentDictionary<string, AgentSession> _sessions = new();

        public AgentFrameworkAgentService(
            AzureOpenAIClient client,
            AgentTools tools,
            IContextStore contextStore,
            IAgentTraceRecorder trace,
            IOptions<AzureAIFoundryOptions> options,
            ILogger<AgentFrameworkAgentService> logger)
        {
            _contextStore = contextStore;
            _trace = trace;
            _options = options.Value;
            _logger = logger;

            _baseChatOptions = new ChatOptions
            {
                Instructions = _options.SystemPrompt,
                MaxOutputTokens = _options.MaxOutputTokens,
                Temperature = _options.Temperature,
                Tools = tools.CreateTools()
            };

            // Tools are invoked automatically by the framework when the model requests them.
            _agent = client
                .GetChatClient(_options.DeploymentName)
                .AsAIAgent(new ChatClientAgentOptions
                {
                    Name = "GenAIAgent",
                    ChatOptions = _baseChatOptions
                });
        }

        public async Task<AgentChatResponse> ChatAsync(AgentChatRequest request, CancellationToken cancellationToken = default)
        {
            var conversationId = string.IsNullOrWhiteSpace(request.ConversationId)
                ? Guid.NewGuid().ToString("N")
                : request.ConversationId;

            if (!_sessions.TryGetValue(conversationId, out var session))
            {
                var newSession = await _agent.CreateSessionAsync(cancellationToken);
                session = _sessions.GetOrAdd(conversationId, newSession);
            }

            _logger.LogInformation("Running agent for conversation {ConversationId}.", conversationId);

            // Stored context is folded into the instructions for this run only, so it
            // applies to every turn without being repeated as a chat message.
            var runOptions = BuildRunOptions(conversationId);

            using var run = _trace.BeginRun(conversationId);
            RecordRequest(conversationId, runOptions is not null);

            var started = Stopwatch.StartNew();
            AgentResponse response = await _agent.RunAsync(
                request.Message,
                session,
                runOptions,
                cancellationToken);
            started.Stop();

            _trace.Record(
                AgentTraceCategory.Model,
                "Model replied",
                $"{response.Text.Length} characters, {response.Usage?.TotalTokenCount ?? 0} tokens",
                (int)started.ElapsedMilliseconds);

            _trace.Record(
                AgentTraceCategory.Memory,
                "Turn stored in agent session",
                $"session {conversationId} updated by the Agent Framework");

            return new AgentChatResponse
            {
                ConversationId = conversationId,
                Reply = response.Text,
                Model = _options.DeploymentName,
                TotalTokens = (int?)response.Usage?.TotalTokenCount
            };
        }

        public async IAsyncEnumerable<AgentStreamChunk> ChatStreamingAsync(
            AgentChatRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var conversationId = string.IsNullOrWhiteSpace(request.ConversationId)
                ? Guid.NewGuid().ToString("N")
                : request.ConversationId;

            if (!_sessions.TryGetValue(conversationId, out var session))
            {
                var newSession = await _agent.CreateSessionAsync(cancellationToken);
                session = _sessions.GetOrAdd(conversationId, newSession);
            }

            _logger.LogInformation("Streaming agent run for conversation {ConversationId}.", conversationId);

            var runOptions = BuildRunOptions(conversationId);

            var traceQueue = new ConcurrentQueue<AgentTraceEntry>();

            // AsyncLocal state does not survive a "yield return", so the trace scope is
            // re-entered around every await instead of being opened once for the method.
            using (_trace.BeginRun(conversationId, traceQueue))
            {
                RecordRequest(conversationId, runOptions is not null);
            }

            var started = Stopwatch.StartNew();
            var characters = 0;

            await using var updates = _agent
                .RunStreamingAsync(request.Message, session, runOptions, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);

            while (true)
            {
                bool hasNext;
                using (_trace.BeginRun(conversationId, traceQueue))
                {
                    hasNext = await updates.MoveNextAsync();
                }

                // Emit any background steps (e.g. tool calls) recorded while advancing.
                while (traceQueue.TryDequeue(out var traceEntry))
                {
                    yield return new AgentStreamChunk { ConversationId = conversationId, Trace = traceEntry };
                }

                if (!hasNext)
                {
                    break;
                }

                var text = updates.Current.Text;
                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }

                characters += text.Length;
                yield return new AgentStreamChunk { ConversationId = conversationId, Delta = text };
            }

            started.Stop();

            using (_trace.BeginRun(conversationId, traceQueue))
            {
                _trace.Record(
                    AgentTraceCategory.Model,
                    "Model finished streaming",
                    $"{characters} characters streamed",
                    (int)started.ElapsedMilliseconds);

                _trace.Record(
                    AgentTraceCategory.Memory,
                    "Turn stored in agent session",
                    $"session {conversationId} updated by the Agent Framework");
            }

            while (traceQueue.TryDequeue(out var traceEntry))
            {
                yield return new AgentStreamChunk { ConversationId = conversationId, Trace = traceEntry };
            }

            yield return new AgentStreamChunk { ConversationId = conversationId, IsFinal = true };
        }

        /// <summary>Records what is being sent to the model for this turn.</summary>
        private void RecordRequest(string conversationId, bool hasContext)
        {
            _trace.Record(
                AgentTraceCategory.Request,
                $"Running agent on {_options.DeploymentName}",
                $"session: {conversationId}, stored context: {(hasContext ? "yes" : "no")}, "
                + $"temperature: {_options.Temperature}, maxOutputTokens: {_options.MaxOutputTokens}");
        }

        public bool EndConversation(string conversationId)
        {
            var removedContext = _contextStore.Clear(conversationId);
            return _sessions.TryRemove(conversationId, out _) || removedContext;
        }

        /// <summary>
        /// Returns per-run options carrying the conversation's stored context,
        /// or null when no context is set so the agent's own options are used.
        /// </summary>
        private ChatClientAgentRunOptions? BuildRunOptions(string conversationId)
        {
            var context = _contextStore.Get(conversationId);
            if (string.IsNullOrWhiteSpace(context))
            {
                return null;
            }

            var chatOptions = _baseChatOptions.Clone();
            chatOptions.Instructions = AgentInstructions.Compose(_options.SystemPrompt, context);

            return new ChatClientAgentRunOptions(chatOptions);
        }
    }
}
