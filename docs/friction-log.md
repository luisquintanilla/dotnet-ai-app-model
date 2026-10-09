# Friction log

This file records the implementation details that are easy to miss when reducing the sample to a small reference app.

## Package and API glue

- `Microsoft.Extensions.AI` and `Microsoft.Extensions.AI.OpenAI` are centrally pinned at `10.5.0`. The bridge requires an `OpenAI` package version of at least `2.10.0`; the solution pins `2.14.0`.
- The OpenAI path uses the official `OpenAI.Chat.ChatClient` and `AsIChatClient()` from `Microsoft.Extensions.AI.OpenAI`. Provider selection is explicit through `SupportModel:Provider`; a missing key is an options-validation failure rather than an implicit fallback. `SupportModelOptions` and `SupportWorkOptions` keep model/provider settings separate from queue capacity.
- MEAI function invocation is a chat-client pipeline concern. The app passes an `AIFunction` in `ChatOptions.Tools` and adds `UseFunctionInvocation`; no application-owned tool-call loop is needed.
- A function result can arrive at a scripted inner client as a serialized `JsonElement` after the MEAI invocation pipeline performs the round trip. The scripted provider accepts both its typed result and that JSON representation so tests stay deterministic while exercising the real invocation path.
- The application uses one domain-named `SupportApplication` for the buffered and streaming use cases. The methods stay separate because MEAI exposes `ChatResponse` and `ChatResponseUpdate`, while request-scoped `AIFunctionFactory` closures carry support policy and correlation context.

## Hosting and test environment

- The parent `C:\Dev` checkout supplies Arcade `Directory.Build.props`/`Directory.Build.targets` files. Those files are not part of this repository and import missing analyzer projects when inherited. The repository-local `Directory.Build.props` and empty `Directory.Build.targets` establish the sample's own build boundary.
- `ActivitySource` instrumentation is emitted by `SupportApplication`, but this reference app does not register an exporter. Applications embedding the slice can connect the source to their existing OpenTelemetry configuration.

## Intentional limitations

- The queue is a bounded in-memory `Channel<string>` carrying support work-item IDs. A full queue returns `429` after the created work item is marked failed; queued work is lost on process restart and the status store is not durable.
- The streaming endpoint emits only text-bearing `ChatResponseUpdate` values as SSE `data` records and a final `complete` event. Function-call updates are consumed by MEAI's invocation pipeline and are not exposed as a second application-level event model.
- The scripted provider is deterministic demonstration glue, not a model emulator. It recognizes the sample ticket IDs and returns fixed text; real deployments should select `openai` (or register another `IChatClient`) explicitly.
- Conversation state, action effects, and work-item lifecycle are deterministic in-memory adapters. They are deliberately support-specific and do not imply a generic session, run, or workflow engine.

## Application-shape evidence

- Stateful streaming requires persisting the customer turn before model execution
  and the assistant turn only after the stream completes. A canceled stream
  therefore leaves the input turn recorded but does not claim a partial answer
  was complete.
- Tool policy belongs around the tool closure, not in the transport. The
  specialist follow-up action checks ticket/customer identity, explicit
  customer intent, correlation, and idempotency before calling the action port.
- Work-item status is not channel state. A status record is created before
  enqueueing, transitions independently of channel delivery, and records an
  individual failure while the worker continues.
- The required glue is small and ordinary: one application service, three
  support-specific ports, in-memory adapters, and endpoint/worker projections.
  There is not yet enough repeated evidence to extract an AI handler or
  universal event model.
