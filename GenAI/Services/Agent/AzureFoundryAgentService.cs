using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Azure.AI.OpenAI;
using GenAI.Configuration;
using GenAI.Models.Agent;
using GenAI.Services.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace GenAI.Services.Agent
{
    /// <summary>
    /// Agent that talks to the Azure AI Foundry deployment through the
    /// <see cref="IChatClient"/> abstraction, managing conversation history itself
    /// via <see cref="IConversationStore"/>.
    /// <para>
    /// This is the lighter alternative to <see cref="AgentFrameworkAgentService"/>:
    /// it keeps full control of the message list instead of delegating to agent sessions.
    /// </para>
    /// </summary>
    public sealed class AzureFoundryAgentService : IAgentService
    {
        private readonly IChatClient _chatClient;
        private readonly IConversationStore _conversationStore;
        private readonly IContextStore _contextStore;
        private readonly IAgentTraceRecorder _trace;
        private readonly ChatOptions _chatOptions;
        private readonly AzureAIFoundryOptions _options;
        private readonly ILogger<AzureFoundryAgentService> _logger;

        public AzureFoundryAgentService(
            AzureOpenAIClient client,
            IConversationStore conversationStore,
            IContextStore contextStore,
            IAgentTraceRecorder trace,
            AgentTools tools,
            IOptions<AzureAIFoundryOptions> options,
            ILogger<AzureFoundryAgentService> logger)
        {
            _conversationStore = conversationStore;
            _contextStore = contextStore;
            _trace = trace;
            _options = options.Value;
            _logger = logger;

            // UseFunctionInvocation runs the tool-call loop, so tools work on this
            // path exactly as they do on the Agent Framework path.
            _chatClient = client
                .GetChatClient(_options.DeploymentName)
                .AsIChatClient()
                .AsBuilder()
                .UseFunctionInvocation()
                .Build();

            _chatOptions = new ChatOptions
            {
                MaxOutputTokens = _options.MaxOutputTokens,
                Temperature = _options.Temperature,
                Tools = tools.CreateTools()
            };
        }

        public async Task<AgentChatResponse> ChatAsync(AgentChatRequest request, CancellationToken cancellationToken = default)
        {
            var conversationId = string.IsNullOrWhiteSpace(request.ConversationId)
                ? Guid.NewGuid().ToString("N")
                : request.ConversationId;

            // Stored context is folded into the system message, so it applies to
            // every turn without being repeated in the chat history.
            var instructions = AgentInstructions.Compose(
                _options.SystemPrompt,
                _contextStore.Get(conversationId));

            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, instructions)
            };
            messages.AddRange(_conversationStore.GetHistory(conversationId));

            var userMessage = new ChatMessage(ChatRole.User, request.Message);
            messages.Add(userMessage);

            _logger.LogInformation(
                "Sending chat request for conversation {ConversationId} with {MessageCount} messages.",
                conversationId,
                messages.Count);

            using var run = _trace.BeginRun(conversationId);
            RecordRequest(conversationId, messages.Count);

            var started = Stopwatch.StartNew();
            ChatResponse response = await _chatClient.GetResponseAsync(messages, _chatOptions, cancellationToken);
            started.Stop();

            var reply = response.Text;

            _trace.Record(
                AgentTraceCategory.Model,
                "Model replied",
                $"{reply.Length} characters, {response.Usage?.TotalTokenCount ?? 0} tokens",
                (int)started.ElapsedMilliseconds);

            _conversationStore.Append(
                conversationId,
                [userMessage, new ChatMessage(ChatRole.Assistant, reply)],
                _options.MaxHistoryMessages);

            RecordMemoryWrite(conversationId);

            return new AgentChatResponse
            {
                ConversationId = conversationId,
                Reply = reply,
                Model = response.ModelId ?? _options.DeploymentName,
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

            var instructions = AgentInstructions.Compose(
                _options.SystemPrompt,
                _contextStore.Get(conversationId));

            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, instructions)
            };
            messages.AddRange(_conversationStore.GetHistory(conversationId));

            var userMessage = new ChatMessage(ChatRole.User, request.Message);
            messages.Add(userMessage);

            _logger.LogInformation("Streaming chat request for conversation {ConversationId}.", conversationId);

            var traceQueue = new ConcurrentQueue<AgentTraceEntry>();

            // AsyncLocal state does not survive a "yield return", so the trace scope is
            // re-entered around every await instead of being opened once for the method.
            using (_trace.BeginRun(conversationId, traceQueue))
            {
                RecordRequest(conversationId, messages.Count);
            }

            var reply = new StringBuilder();
            var started = Stopwatch.StartNew();

            await using var updates = _chatClient
                .GetStreamingResponseAsync(messages, _chatOptions, cancellationToken)
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

                reply.Append(text);
                yield return new AgentStreamChunk { ConversationId = conversationId, Delta = text };
            }

            started.Stop();

            // History is stored only once the full reply has been streamed.
            _conversationStore.Append(
                conversationId,
                [userMessage, new ChatMessage(ChatRole.Assistant, reply.ToString())],
                _options.MaxHistoryMessages);

            using (_trace.BeginRun(conversationId, traceQueue))
            {
                _trace.Record(
                    AgentTraceCategory.Model,
                    "Model finished streaming",
                    $"{reply.Length} characters streamed",
                    (int)started.ElapsedMilliseconds);

                RecordMemoryWrite(conversationId);
            }

            while (traceQueue.TryDequeue(out var traceEntry))
            {
                yield return new AgentStreamChunk { ConversationId = conversationId, Trace = traceEntry };
            }

            yield return new AgentStreamChunk { ConversationId = conversationId, IsFinal = true };
        }

        /// <summary>Records what is being sent to the model for this turn.</summary>
        private void RecordRequest(string conversationId, int messageCount)
        {
            var hasContext = !string.IsNullOrWhiteSpace(_contextStore.Get(conversationId));

            _trace.Record(
                AgentTraceCategory.Request,
                $"Sending {messageCount} message(s) to {_options.DeploymentName}",
                $"history: {Math.Max(messageCount - 2, 0)}, stored context: {(hasContext ? "yes" : "no")}, "
                + $"temperature: {_options.Temperature}, maxOutputTokens: {_options.MaxOutputTokens}");
        }

        /// <summary>Records the history write, including whether trimming has kicked in.</summary>
        private void RecordMemoryWrite(string conversationId)
        {
            var stored = _conversationStore.GetHistory(conversationId).Count;
            var atLimit = stored >= _options.MaxHistoryMessages;

            _trace.Record(
                AgentTraceCategory.Memory,
                "Saved turn to conversation history",
                $"history now {stored}/{_options.MaxHistoryMessages} message(s)"
                + (atLimit ? ", oldest messages are being dropped" : string.Empty));
        }

        public bool EndConversation(string conversationId)
        {
            var removedContext = _contextStore.Clear(conversationId);
            return _conversationStore.Remove(conversationId) || removedContext;
        }
    }
}
