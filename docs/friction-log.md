# Friction log

This file records the implementation details that are easy to miss when reducing the sample to a small reference app.

## Package and API glue

- `Microsoft.Extensions.AI` and `Microsoft.Extensions.AI.OpenAI` are centrally pinned at `10.5.0`. The bridge requires an `OpenAI` package version of at least `2.10.0`; the solution pins `2.14.0`.
- The OpenAI path uses the official `OpenAI.Chat.ChatClient` and `AsIChatClient()` from `Microsoft.Extensions.AI.OpenAI`. Provider selection is explicit through `SupportAssistant:Provider`; a missing key is an options-validation failure rather than an implicit fallback.
- MEAI function invocation is a chat-client pipeline concern. The app passes an `AIFunction` in `ChatOptions.Tools` and adds `UseFunctionInvocation`; no application-owned tool-call loop is needed.
- A function result can arrive at a scripted inner client as a serialized `JsonElement` after the MEAI invocation pipeline performs the round trip. The scripted provider accepts both its typed result and that JSON representation so tests stay deterministic while exercising the real invocation path.

## Hosting and test environment

- The parent `C:\Dev` checkout supplies Arcade `Directory.Build.props`/`Directory.Build.targets` files. Those files are not part of this repository and import missing analyzer projects when inherited. The repository-local `Directory.Build.props` and empty `Directory.Build.targets` establish the sample's own build boundary.
- `ActivitySource` instrumentation is emitted by `SupportAssistant`, but this reference app does not register an exporter. Applications embedding the slice can connect the source to their existing OpenTelemetry configuration.

## Intentional limitations

- The queue is a bounded in-memory `Channel<SupportRequest>`. A full queue returns `429`; queued work is lost on process restart and there is no durable status/result store.
- The streaming endpoint emits only text-bearing `ChatResponseUpdate` values as SSE `data` records and a final `complete` event. Function-call updates are consumed by MEAI's invocation pipeline and are not exposed as a second application-level event model.
- The scripted provider is deterministic demonstration glue, not a model emulator. It recognizes the sample ticket IDs and returns fixed text; real deployments should select `openai` (or register another `IChatClient`) explicitly.
