using System.ClientModel;
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

        public AgentController(
            IAgentService agentService,
            ILogger<AgentController> logger,
            IHostEnvironment environment)
        {
            _agentService = agentService;
            _logger = logger;
            _environment = environment;
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
            return _agentService.EndConversation(conversationId) ? NoContent() : NotFound();
        }
    }
}
