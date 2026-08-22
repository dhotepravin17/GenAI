using System.ClientModel;
using System.Text.Json;
using System.ComponentModel.DataAnnotations;
using GenAI.Models.Agent;
using GenAI.Services.Agent;
using Microsoft.AspNetCore.Mvc;

namespace GenAI.Controllers
{
    /// <summary>Endpoints for chatting with the Azure AI Foundry agent.</summary>
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public sealed class AgentController : ControllerBase
    {
        private readonly IAgentService _agentService;
        private readonly ILogger<AgentController> _logger;
        private readonly IHostEnvironment _environment;
        private readonly IContextStore _contextStore;
        private readonly IAgentTraceRecorder _traceRecorder;

        private static readonly JsonSerializerOptions JsonOptions =
            new(JsonSerializerDefaults.Web);

        public AgentController(
            IAgentService agentService,
            ILogger<AgentController> logger,
            IHostEnvironment environment,
            IContextStore contextStore,
            IAgentTraceRecorder traceRecorder)
        {
            _agentService = agentService;
            _logger = logger;
            _environment = environment;
            _contextStore = contextStore;
            _traceRecorder = traceRecorder;
        }

        /// <summary>Sends a message to the agent and returns its reply.</summary>
        /// <remarks>Pass the returned <c>conversationId</c> in subsequent requests to keep context.</remarks>
        [HttpPost("chat")]
        [ProducesResponseType<AgentChatResponse>(StatusCodes.Status200OK)]
        [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
        [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
        public async Task<ActionResult<AgentChatResponse>> Chat(
            [FromBody] AgentChatRequest request,
            CancellationToken cancellationToken)
        {
            try
            {
                var response = await _agentService.ChatAsync(request, cancellationToken);
                return Ok(response);
            }
            catch (Exception ex) when (ex is ClientResultException or AggregateException or HttpRequestException)
            {
                // Covers provider errors (4xx/5xx from the service) and connectivity/DNS
                // failures, which the retry pipeline surfaces as AggregateException.
                _logger.LogError(ex, "Azure AI Foundry request failed.");

                // Outside Development the cause is logged only, never returned, so that
                // provider and infrastructure details are not exposed to callers.
                return Problem(
                    title: "The AI service is unreachable or could not process the request.",
                    detail: _environment.IsDevelopment() ? GetRootCause(ex).Message : null,
                    statusCode: StatusCodes.Status502BadGateway);
            }
        }

        /// <summary>
        /// Sends a message and streams the reply as server-sent events.
        /// Each event carries a JSON <c>AgentStreamChunk</c>.
        /// </summary>
        [HttpPost("chat/stream")]
        [Produces("text/event-stream")]
        public async Task ChatStream([FromBody] AgentChatRequest request, CancellationToken cancellationToken)
        {
            Response.Headers.ContentType = "text/event-stream";
            Response.Headers.CacheControl = "no-cache";
            Response.Headers.Connection = "keep-alive";
            // Tells reverse proxies not to buffer, which would defeat streaming.
            Response.Headers["X-Accel-Buffering"] = "no";

            try
            {
                await foreach (var chunk in _agentService.ChatStreamingAsync(request, cancellationToken))
                {
                    await WriteEventAsync(chunk, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Client navigated away or aborted; nothing to report.
            }
            catch (Exception ex) when (ex is ClientResultException or AggregateException or HttpRequestException)
            {
                _logger.LogError(ex, "Streaming chat failed.");

                await WriteEventAsync(
                    new AgentStreamChunk
                    {
                        ConversationId = request.ConversationId ?? string.Empty,
                        IsFinal = true,
                        Error = _environment.IsDevelopment()
                            ? GetRootCause(ex).Message
                            : "The AI service is unreachable or could not process the request."
                    },
                    cancellationToken);
            }
        }

        /// <summary>Writes one server-sent event and flushes it to the client immediately.</summary>
        private async Task WriteEventAsync(AgentStreamChunk chunk, CancellationToken cancellationToken)
        {
            var json = JsonSerializer.Serialize(chunk, JsonOptions);
            // SSE frame: "data: <json>" terminated by a blank line.
            await Response.WriteAsync($"data: {json}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }

        /// <summary>
        /// Returns the recorded background steps for a conversation: what was sent to
        /// the model, which tools ran, and what was written to memory.
        /// </summary>
        [HttpGet("conversations/{conversationId}/trace")]
        [ProducesResponseType<IReadOnlyList<AgentTraceEntry>>(StatusCodes.Status200OK)]
        public ActionResult<IReadOnlyList<AgentTraceEntry>> GetTrace(
            [FromRoute][StringLength(64)] string conversationId)
        {
            return Ok(_traceRecorder.GetTrace(conversationId));
        }

        /// <summary>Returns the context currently stored for a conversation.</summary>
        [HttpGet("conversations/{conversationId}/context")]
        [ProducesResponseType<AgentContextResponse>(StatusCodes.Status200OK)]
        public ActionResult<AgentContextResponse> GetContext(
            [FromRoute][StringLength(64)] string conversationId)
        {
            return Ok(new AgentContextResponse
            {
                ConversationId = conversationId,
                Context = _contextStore.Get(conversationId)
            });
        }

        /// <summary>Saves context for a conversation, replacing anything already stored.</summary>
        [HttpPut("conversations/{conversationId}/context")]
        [ProducesResponseType<AgentContextResponse>(StatusCodes.Status200OK)]
        [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
        public ActionResult<AgentContextResponse> SaveContext(
            [FromRoute][StringLength(64)] string conversationId,
            [FromBody] AgentContextRequest request)
        {
            _contextStore.Save(conversationId, request.Context);
            _logger.LogInformation("Context saved for conversation {ConversationId}.", conversationId);

            return Ok(new AgentContextResponse
            {
                ConversationId = conversationId,
                Context = _contextStore.Get(conversationId)
            });
        }

        /// <summary>Appends to the context stored for a conversation.</summary>
        [HttpPost("conversations/{conversationId}/context")]
        [ProducesResponseType<AgentContextResponse>(StatusCodes.Status200OK)]
        [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
        public ActionResult<AgentContextResponse> AppendContext(
            [FromRoute][StringLength(64)] string conversationId,
            [FromBody] AgentContextRequest request)
        {
            var context = _contextStore.Append(conversationId, request.Context);
            _logger.LogInformation("Context appended for conversation {ConversationId}.", conversationId);

            return Ok(new AgentContextResponse
            {
                ConversationId = conversationId,
                Context = context
            });
        }

        /// <summary>Clears the context stored for a conversation.</summary>
        [HttpDelete("conversations/{conversationId}/context")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult ClearContext(
            [FromRoute][StringLength(64)] string conversationId)
        {
            return _contextStore.Clear(conversationId) ? NoContent() : NotFound();
        }

        /// <summary>Unwraps aggregate/nested exceptions to the innermost cause.</summary>
        private static Exception GetRootCause(Exception exception)
        {
            var current = exception is AggregateException aggregate
                ? aggregate.Flatten().InnerExceptions[0]
                : exception;

            while (current.InnerException is not null)
            {
                current = current.InnerException;
            }

            return current;
        }

        /// <summary>Ends a conversation and deletes its history.</summary>
        [HttpDelete("conversations/{conversationId}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult EndConversation(
            [FromRoute][StringLength(64)] string conversationId)
        {
            var ended = _agentService.EndConversation(conversationId);
            var tracesCleared = _traceRecorder.Clear(conversationId);

            return ended || tracesCleared ? NoContent() : NotFound();
        }
    }
}
