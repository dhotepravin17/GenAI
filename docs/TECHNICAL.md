# GenAI Agent — Technical Documentation

An ASP.NET Core (.NET 10) Web API that exposes a conversational AI agent backed by an Azure AI
Foundry model deployment, plus a React client for testing it.

The agent can call **tools** (weather, currency conversion), keeps **per-conversation memory**,
accepts **standing context**, **streams** replies as they are generated, and records a **trace**
of everything that happened behind each reply.

---

## 1. Solution layout

```
GenAI/                          ASP.NET Core Web API
├── Configuration/              Strongly typed settings
├── Controllers/                HTTP endpoints
├── Extensions/                 Startup wiring (DI, Key Vault)
├── Models/
│   ├── Agent/                  Request/response/streaming/trace DTOs
│   └── Tools/                  Tool result shapes
├── Services/
│   ├── Agent/                  Agent implementations + memory + tracing
│   └── Tools/                  Tool implementations
└── Program.cs                  Composition root

client/                         React 18 + Vite test UI
└── src/  App.jsx, api.js, styles.css
```

---

## 2. Request flow

A single chat turn, end to end:

```
React client
   │  POST /api/agent/chat/stream  { message, conversationId }
   ▼
AgentController                      validates the request (DataAnnotations)
   │  IAgentService.ChatStreamingAsync(...)
   ▼
Agent service                        builds instructions + history + user message
   │
   ├─ IContextStore.Get()             standing facts for this conversation
   ├─ IConversationStore.GetHistory() past messages
   └─ IAgentTraceRecorder.Record()    "request" trace entry
   │
   ▼
Azure AI Foundry (gpt-4.1-mini)
   │
   │  model asks for a tool ──► AgentTools.GetWeatherAsync
   │                                └─ WeatherService ──► Open-Meteo (HTTP)
   │  tool result returned to the model
   │
   ▼  reply text streams back in fragments
Agent service                        appends to history, records "model"/"memory" traces
   │  yields an AgentStreamChunk per fragment and per trace entry
   ▼
AgentController                      writes each chunk as a server-sent event
   │  data: {"conversationId":"…","delta":"Doha is "}
   ▼
React client                         appends the delta to the assistant bubble
```

**Key idea:** the model cannot execute anything. It only *asks* for a tool by name; the
application runs the function and feeds the result back. That request/execute/return loop is what
makes this an agent rather than a chatbot.

---

## 3. Components

### 3.1 Configuration — `AzureAIFoundryOptions`

Binds the `AzureAIFoundry` section into a validated class.

| Setting | Purpose |
|---|---|
| `Endpoint` | Azure OpenAI resource URL |
| `ApiKey` | Resource key — supply via user-secrets or Key Vault, never source control |
| `DeploymentName` | Deployment to call, e.g. `gpt-4.1-mini` |
| `SystemPrompt` | Base instructions defining the agent's behaviour |
| `MaxOutputTokens` | Cap on generated tokens per reply |
| `Temperature` | 0 = deterministic, higher = more varied |
| `MaxHistoryMessages` | How many past messages are kept and resent |
| `UseAgentFramework` | Selects which `IAgentService` implementation runs |

Registered with `.ValidateDataAnnotations().ValidateOnStart()`, so a missing endpoint or key
fails at **startup** with a clear message rather than on the first request.

### 3.2 Configuration sources — `ConfigurationExtensions`

Adds Azure Key Vault as the **highest-priority** source when `KeyVault:Uri` is set. Any secret
found there overrides `appsettings.json`; anything missing falls back to local configuration.
Vault secrets use `--` as the section separator (`AzureAIFoundry--ApiKey`). An unreachable vault
logs a warning and the app continues on local settings instead of failing to start.

### 3.3 Controller — `AgentController`

| Method | Route | Purpose |
|---|---|---|
| POST | `/api/agent/chat` | Single request/response reply |
| POST | `/api/agent/chat/stream` | Reply streamed as server-sent events |
| GET | `/api/agent/conversations/{id}/context` | Read stored context |
| PUT | `/api/agent/conversations/{id}/context` | Replace stored context |
| POST | `/api/agent/conversations/{id}/context` | Append to stored context |
| DELETE | `/api/agent/conversations/{id}/context` | Clear stored context |
| GET | `/api/agent/conversations/{id}/trace` | Recorded background steps |
| DELETE | `/api/agent/conversations/{id}` | End conversation (history + context + trace) |

Error handling catches `ClientResultException`, `AggregateException` and `HttpRequestException` —
the last two matter because DNS and connectivity failures surface wrapped by the SDK's retry
pipeline. Failures return **502 ProblemDetails**; the root cause is included only in Development,
and logged in all environments.

### 3.4 Agent implementations

Both implement `IAgentService`; `UseAgentFramework` picks one at startup. The controller is
unaware of which is active.

**`AgentFrameworkAgentService`** (`UseAgentFramework: true`)
Uses the Microsoft Agent Framework. `ChatClient.AsAIAgent(...)` builds an `AIAgent`; each
conversation gets an `AgentSession` that the framework fills with history. Per-conversation
context is injected per run via `ChatClientAgentRunOptions` carrying a **cloned** `ChatOptions`,
so tool and sampling settings are preserved.

**`AzureFoundryAgentService`** (`UseAgentFramework: false`)
Uses the `IChatClient` abstraction directly and owns the message list. Each turn sends
`[system instructions] + [stored history] + [new message]`. `.UseFunctionInvocation()` runs the
tool-call loop, so tools behave identically on both paths.

> Both go through `Microsoft.Extensions.AI`. Calling `Azure.AI.OpenAI`'s chat client directly
> throws `MissingMethodException`, because the Agent Framework requires a newer `OpenAI` SDK than
> the current stable `Azure.AI.OpenAI` was compiled against.

### 3.5 Memory

Three independent stores, all in process memory:

| Store | Holds | Sent to the model as |
|---|---|---|
| `IConversationStore` | Past user/assistant messages | Chat messages |
| `IContextStore` | Standing facts about the conversation | Part of the system instructions |
| `IAgentTraceRecorder` | Diagnostic steps | Never sent |

`InMemoryConversationStore` trims to `MaxHistoryMessages`, dropping the **oldest** messages — this
is why very long chats forget the beginning.

`InMemoryContextStore` caps at 8,000 characters, keeping the **most recent** text. Because context
rides in the instructions rather than the history, it is never trimmed away by conversation
length — the right place for durable facts.

`AgentInstructions.Compose` merges the system prompt with the context and labels it explicitly as
*"factual information, not instructions"*, limiting prompt-injection through the context field.

### 3.6 Tools

`AgentTools` exposes two functions to the model via `AIFunctionFactory.Create`. The
`[Description]` attributes are read by the model to decide when to call each one, so their wording
is functional, not decorative.

| Tool | Backing service | Upstream API |
|---|---|---|
| `get_weather` | `WeatherService` | Open-Meteo (geocode, then current conditions) |
| `convert_currency` | `CurrencyService` | ExchangeRate-API (166 currencies) |

Neither API needs a key. Both services validate model-supplied input before calling upstream
(3-letter currency codes, length caps, positive amounts), use 15-second timeouts, and return a
populated `Error` field rather than throwing — so the agent can explain the problem instead of the
request failing.

Services are singletons that resolve clients through `IHttpClientFactory`, avoiding the socket and
DNS-staleness problems of a captured `HttpClient`.

### 3.7 Tracing

`AgentTraceRecorder` records each step of a run: the request sent, every tool call with its
arguments/result/duration, the model response, and the memory write. The active run is tracked
with `AsyncLocal`, so tools invoked deep inside the pipeline attribute themselves to the right
conversation without passing an id around.

> **Streaming caveat:** `AsyncLocal` values do **not** survive a `yield return`. Both streaming
> methods therefore use an explicit async enumerator and re-enter the trace scope around each
> `await`. Opening the scope once for the method silently loses every entry after the first.

Entries stream live inside the SSE channel as `{"trace":{…}}` chunks and are also stored per
conversation (capped at 200). Tracing captures prompts and tool arguments, so it is gated by
`Diagnostics:EnableAgentTrace` — set it to `false` in production.

### 3.8 Streaming protocol

`POST /api/agent/chat/stream` returns `text/event-stream`. Each frame is one JSON
`AgentStreamChunk`:

```
data: {"conversationId":"a3d7…","delta":"The weather "}
data: {"conversationId":"a3d7…","trace":{"category":"tool","title":"Model called get_weather"}}
data: {"conversationId":"a3d7…","isFinal":true}
```

| Field | Meaning |
|---|---|
| `conversationId` | Sent on every chunk so the client can store it |
| `delta` | Next fragment of reply text |
| `trace` | A background step, emitted as it happens |
| `isFinal` | Last chunk of the turn |
| `error` | Run failed; show this instead of a reply |

Responses set `X-Accel-Buffering: no` and flush after each frame so proxies cannot defeat
streaming. Client disconnects arrive as `OperationCanceledException` and are ignored rather than
logged as failures. On the direct path, history is written only **after** the full reply has
streamed, so an aborted stream cannot corrupt the conversation.

### 3.9 React client

| File | Responsibility |
|---|---|
| `api.js` | `streamChat` (SSE reader), `deleteConversation`, `fetchTrace` |
| `App.jsx` | Session list, message list, trace panel, composer |
| `styles.css` | Dark theme, responsive layout |

`streamChat` reads the response body with `ReadableStream`, splits on blank lines and buffers
partial frames, invoking `onDelta` / `onTrace` per chunk.

Sessions live in `localStorage`. Each has a client `key`, the server `conversationId`, its
messages and its trace. **New chat** starts a fresh session; **Clear chat** calls
`DELETE /conversations/{id}` so the server also forgets, then empties the view; **Stop** aborts the
stream with an `AbortController`.

> The browser's copy of messages is for display only. If the API restarts, the UI still shows the
> conversation but the server has forgotten it — the agent will act as though the chat never
> happened.

---

## 4. Configuration reference

```json
{
  "KeyVault":    { "Uri": "" },
  "Cors":        { "AllowedOrigins": [ "http://localhost:5173", "http://localhost:4173" ] },
  "Diagnostics": { "EnableAgentTrace": true },
  "AzureAIFoundry": {
    "Endpoint": "https://<resource>.openai.azure.com/",
    "ApiKey": "",
    "DeploymentName": "gpt-4.1-mini",
    "SystemPrompt": "You are a helpful assistant. Answer concisely and accurately.",
    "MaxOutputTokens": 800,
    "Temperature": 0.7,
    "MaxHistoryMessages": 20,
    "UseAgentFramework": false
  }
}
```

Set the key outside source control:

```bash
dotnet user-secrets set "AzureAIFoundry:ApiKey" "<key>" --project GenAI/GenAI.csproj
```

---

## 5. Running locally

1. **API** — F5 in Visual Studio, or `dotnet run --project GenAI`. Swagger opens at `/swagger`.
2. **Client** — `npm run dev` in `client/`, then open `http://localhost:5173`.

`client/.env.local` sets `VITE_API_BASE`. Point it at the **HTTPS** URL (e.g.
`https://localhost:7088`): the API redirects HTTP to HTTPS, and a 307 mid-stream breaks the SSE
connection.

---

## 6. Production considerations

| Area | Current state | Needed for production |
|---|---|---|
| Secrets | Key in `appsettings.json` during development | Key Vault or environment variables; rotate any committed key |
| Memory | In-process dictionaries | Redis or a database — a second instance would not see existing conversations |
| Tracing | Enabled | Disable; it records prompts and tool arguments |
| Auth | None | The API is open — add authentication before exposing it |
| CORS | Localhost dev origins | Restrict to the real client origin |
| Error detail | Root cause returned in Development only | Already correct |
