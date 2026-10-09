# .NET AI Application Model support application

This repository is a small, runnable .NET 10 reference app for a support-case vertical slice. It uses the stable .NET/ASP.NET Core hosting model and Microsoft.Extensions.AI (`IChatClient`) directly:

- the default `scripted` provider is deterministic and requires no credentials;
- `SupportApplication` owns the support use case: conversation state, model context, request-scoped tools, action policy, side effects, and response projection;
- the read-only ticket lookup and policy-gated specialist follow-up tools are created with `AIFunctionFactory` and invoked by the MEAI `UseFunctionInvocation` pipeline;
- buffered, streaming SSE, and bounded in-memory queue transports invoke the same application service;
- `SupportWorker` consumes support-specific work-item IDs with normal `BackgroundService`, `Channel<T>`, and dependency injection;
- conversation, action, and work-item stores are domain-named in-memory ports and adapters so the behavior is deterministic and easy to replace.

The app intentionally does not add a custom AI harness, universal response/event model, OpenAI-compatible `/responses` endpoints, durable run storage, retries, or suspend/resume behavior.

## Run

The solution targets `net10.0` and uses central package management:

```powershell
dotnet restore
dotnet build --no-restore -warnaserror
dotnet run --project src\DotnetAiAppModel.SupportAssistant
```

The default scripted configuration is in `src\DotnetAiAppModel.SupportAssistant\appsettings.json`.

To opt into the official OpenAI client explicitly, configure the provider and key before starting:

```powershell
$env:SupportModel__Provider = "openai"
$env:SupportModel__Model = "gpt-4o-mini"
$env:SupportModel__OpenAiApiKey = "<your-key>"
dotnet run --project src\DotnetAiAppModel.SupportAssistant
```

Selecting `openai` without `SupportModel__OpenAiApiKey` fails options validation at startup. The registration never falls back to the scripted client when `openai` is selected. Model/provider settings and queue settings are separate: queue capacity is `SupportWork__QueueCapacity`.

## Endpoints

Buffered response:

```powershell
Invoke-RestMethod http://localhost:5000/support `
  -Method Post -ContentType "application/json" `
  -Body '{"message":"What is the next step?","ticketId":"SUP-1001","customerId":"CUS-42","conversationId":"conversation-42"}'
```

Streaming response (`text/event-stream`):

```powershell
curl.exe -N http://localhost:5000/support/stream `
  -H "Content-Type: application/json" `
  -d '{"message":"What is the next step?","ticketId":"SUP-1001","conversationId":"conversation-42"}'
```

Queue acceptance:

```powershell
Invoke-RestMethod http://localhost:5000/support/queue `
  -Method Post -ContentType "application/json" `
  -Body '{"message":"Please check my ticket.","ticketId":"SUP-1001","conversationId":"conversation-42"}'
```

The queue endpoint creates a support work item before attempting channel delivery. It returns `202 Accepted` with a correlation ID and a status location. If the bounded channel is full, it returns `429 Too Many Requests` and the created-but-not-enqueued work item is marked `failed` with a status location in the response body:

```powershell
Invoke-RestMethod http://localhost:5000/support/queue/{workItemId}
```

The status moves through `pending`, `processing`, `completed`, or `failed`.
Channel delivery is deliberately separate from work-item state, and a
work-item failure does not stop the worker from processing later items.

To request the policy-gated side effect, provide a ticket, customer, and
explicit language such as "Please escalate this to a specialist." The
application records the idempotent specialist follow-up action and returns its
result in the buffered response. Repeating the same `idempotencyKey` does not
record a second effect.

## Test

```powershell
dotnet test --no-build
```

The tests cover state transitions, tool invocation, policy and idempotency,
complete and streaming persistence, cancellation, SSE framing, queue
acceptance/status, worker failure continuation, and provider configuration.

See [docs/application-patterns.md](docs/application-patterns.md) for the
boundary between ordinary .NET patterns, application-local support patterns,
and future extraction candidates.
