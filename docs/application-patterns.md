# Application patterns

This sample is intentionally a support application, not a framework proposal.
The useful boundary is the behavior owned by `SupportApplication`, not a
renamed `IChatClient` wrapper.

## Ordinary .NET patterns

These responsibilities already have stable .NET or ASP.NET Core mechanisms:

- dependency injection and options validation;
- `IChatClient`, `AIFunction`, `ChatResponse`, and `ChatResponseUpdate` for the
  model/tool interaction;
- `BackgroundService` and `Channel<T>` for hosted queue consumption;
- `IAsyncEnumerable<T>` and request cancellation for streaming;
- `TimeProvider`, logging scopes, `ActivitySource`, and structured logs;
- endpoint binding, validation problems, `202 Accepted`, status locations, and
  `text/event-stream` framing.

The sample does not standardize a second abstraction over these mechanisms.

## Application-local support patterns

These semantics belong to the support case because they describe its state and
policy:

- `SupportApplication` is the use-case coordinator for buffered and streaming
  support responses;
- `SupportConversationService` prepares correlation and conversation state and
  persists the customer turn before model execution and the assistant turn
  after a complete response;
- `SupportModelContextFactory` constructs the MEAI messages and `ChatOptions`;
  `SupportActionToolFactory` creates request-scoped tools without hiding
  `IChatClient`;
- `SupportSpecialistFollowUpService` owns the policy-gated action closure,
  correlation, idempotency, and action-result collection;
- `ISupportConversationStore` and `ISupportActionStore` are small ports for
  support state and support effects;
- `SupportActionPolicy` requires a ticket, customer identity, reason, and an
  explicit customer request before specialist follow-up can be recorded;
- `InMemorySupportActionStore` makes the at-least-once boundary explicit with
  an idempotency key and an audit record;
- `ISupportWorkStore` models the support-specific `pending` -> `processing` ->
  `completed`/`failed` lifecycle while `SupportWorkChannel` only delivers work
  item IDs;
- `SupportWorkSubmissionService` owns work-item creation, enqueueing, and the
  queue-full rejection transition;
- `SupportWorkItemProcessor` owns one work item's status transitions, failure
  isolation, and processing logs;
- HTTP endpoints are thin adapters over the application and submission
  services. `SupportWorker` is a singleton channel consumer that uses
  `IServiceScopeFactory` to resolve a scoped processor for each ID.

These services are ordinary DI composition, not a new framework. The
application-local boundary is the support behavior they coordinate; it is not
an abstraction over `IChatClient`, a universal session/run type, or a generic
work-item engine. Services are concrete where the sample has no independent
port to substitute, while stores and the model client remain interfaces at
the boundaries that need replacement or testing.

Model/provider settings live in `SupportModelOptions`, while queue capacity
lives in `SupportWorkOptions`; the split keeps deployment/model configuration
separate from deferred-work capacity without inventing a general configuration
framework.

The request-scoped action closure is important: a model-provided argument
cannot bypass the application's policy, correlation, idempotency, or audit
context. The model context factory and tool factory make that boundary
explicit while leaving MEAI types visible at the application edge.

## What is not standardized yet

This pass deliberately does not add a universal AI harness, operation base
class, session/run abstraction, response event hierarchy, provider-compatible
endpoint, middleware package, durable workflow engine, or generic work-item
contract. Those names would hide semantics that are still specific to support.

The first plausible reusable candidate is a small transport-neutral deferred
work lifecycle contract, but only after a second vertical slice demonstrates
the same status, idempotency, and failure-continuation semantics. The current
support work-item store is evidence for that experiment, not the abstraction
itself.
