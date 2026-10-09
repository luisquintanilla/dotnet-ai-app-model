# .NET AI Application Model support assistant

This repository is a small, runnable .NET 10 reference app for a support-assistant vertical slice. It uses the stable .NET/ASP.NET Core hosting model and Microsoft.Extensions.AI (`IChatClient`) directly:

- the default `scripted` provider is deterministic and requires no credentials;
- the ticket lookup tool is created with `AIFunctionFactory` and invoked by the MEAI `UseFunctionInvocation` pipeline;
- buffered, streaming SSE, and bounded in-memory queue endpoints invoke the same ordinary `SupportHandlers` methods;
- `SupportWorker` consumes queued requests with normal `BackgroundService` and dependency injection.

The app intentionally does not add a custom AI harness, universal response/event model, OpenAI-compatible `/responses` endpoints, durable run storage, or suspend/resume behavior.

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
$env:SupportAssistant__Provider = "openai"
$env:SupportAssistant__Model = "gpt-4o-mini"
$env:SupportAssistant__OpenAiApiKey = "<your-key>"
dotnet run --project src\DotnetAiAppModel.SupportAssistant
```

Selecting `openai` without `SupportAssistant__OpenAiApiKey` fails options validation at startup. The registration never falls back to the scripted client when `openai` is selected.

## Endpoints

Buffered response:

```powershell
Invoke-RestMethod http://localhost:5000/support `
  -Method Post -ContentType "application/json" `
  -Body '{"message":"What is the next step?","ticketId":"SUP-1001"}'
```

Streaming response (`text/event-stream`):

```powershell
curl.exe -N http://localhost:5000/support/stream `
  -H "Content-Type: application/json" `
  -d '{"message":"What is the next step?"}'
```

Queue acceptance:

```powershell
Invoke-RestMethod http://localhost:5000/support/queue `
  -Method Post -ContentType "application/json" `
  -Body '{"message":"Please check my ticket.","ticketId":"SUP-1001"}'
```

The queue endpoint returns `202 Accepted` with a correlation ID. The response is an acceptance receipt only; the worker logs processing and does not expose a durable result endpoint.

The HTTP endpoints and `SupportWorker` call the same application-owned handler
methods directly. `SupportHandlers` is sample application code, not a required
framework abstraction; a different application can map an endpoint directly to
`IChatClient` or use any ordinary delegate/service it needs.

## Test

```powershell
dotnet test --no-build
```

The tests cover the deterministic buffered and streaming paths, MEAI tool-call round trips, SSE framing, bounded queue behavior, worker consumption and failure continuation, cancellation, startup configuration validation, and focused `WebApplicationFactory` requests.
