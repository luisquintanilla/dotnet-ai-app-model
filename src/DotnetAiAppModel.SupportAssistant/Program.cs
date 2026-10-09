using System.Text.Json;
using DotnetAiAppModel.SupportAssistant;
using DotnetAiAppModel.SupportAssistant.Providers;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails();
builder.Services.AddSupportApplication(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();

app.MapPost(
    "/support",
    async (
        SupportRequest request,
        SupportApplication supportApplication,
        CancellationToken cancellationToken) =>
    {
        var errors = SupportRequestValidation.Validate(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var response = await supportApplication.HandleAsync(request, cancellationToken);

        return Results.Ok(response);
    });

app.MapPost(
    "/support/stream",
    async (
        SupportRequest request,
        SupportApplication supportApplication,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
    {
        var errors = SupportRequestValidation.Validate(request);
        if (errors.Count > 0)
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await httpContext.Response.WriteAsJsonAsync(new { errors }, cancellationToken);
            return;
        }

        httpContext.Response.ContentType = "text/event-stream";
        httpContext.Response.Headers.CacheControl = "no-cache";

        var wroteText = false;
        await foreach (var update in supportApplication.StreamAsync(request, cancellationToken))
        {
            if (string.IsNullOrEmpty(update.Text))
            {
                continue;
            }

            wroteText = true;
            var payload = JsonSerializer.Serialize(new SupportStreamEvent(update.Text));
            await httpContext.Response.WriteAsync($"data: {payload}\n\n", cancellationToken);
            await httpContext.Response.Body.FlushAsync(cancellationToken);
        }

        if (!wroteText)
        {
            throw new InvalidOperationException("The chat client returned no text updates.");
        }

        await httpContext.Response.WriteAsync("event: complete\ndata: {}\n\n", cancellationToken);
        await httpContext.Response.Body.FlushAsync(cancellationToken);
    });

app.MapPost(
    "/support/queue",
    async (
        SupportRequest request,
        SupportWorkChannel queue,
        ISupportWorkStore workStore,
        TimeProvider timeProvider,
        CancellationToken cancellationToken) =>
    {
        var errors = SupportRequestValidation.Validate(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var correlationId = Guid.NewGuid().ToString("N");
        var queuedRequest = request with { CorrelationId = correlationId };
        var workItem = await workStore.CreateAsync(
            queuedRequest,
            correlationId,
            timeProvider.GetUtcNow(),
            cancellationToken);

        if (!queue.TryEnqueue(workItem.WorkItemId))
        {
            await workStore.MarkFailedAsync(
                workItem.WorkItemId,
                "The support work queue is full.",
                timeProvider.GetUtcNow(),
                CancellationToken.None);
            var rejectedStatusUrl = $"/support/queue/{workItem.WorkItemId}";
            return Results.Json(
                new QueueAcceptedResponse(
                    correlationId,
                    workItem.WorkItemId,
                    rejectedStatusUrl,
                    "failed"),
                statusCode: StatusCodes.Status429TooManyRequests);
        }

        var statusUrl = $"/support/queue/{workItem.WorkItemId}";
        return Results.Accepted(
            statusUrl,
            new QueueAcceptedResponse(
                correlationId,
                workItem.WorkItemId,
                statusUrl));
    });

app.MapGet(
    "/support/queue/{workItemId}",
    async (
        string workItemId,
        ISupportWorkStore workStore,
        CancellationToken cancellationToken) =>
    {
        var workItem = await workStore.GetAsync(workItemId, cancellationToken);
        return workItem is null
            ? Results.NotFound()
            : Results.Ok(new SupportWorkItemStatusResponse(
                workItem.WorkItemId,
                workItem.CorrelationId,
                workItem.Status.ToString().ToLowerInvariant(),
                workItem.CreatedAt,
                workItem.StartedAt,
                workItem.CompletedAt,
                workItem.Response,
                workItem.Error));
    });

app.Run();

public partial class Program
{
}
