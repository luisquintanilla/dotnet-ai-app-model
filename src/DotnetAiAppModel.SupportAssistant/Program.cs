using System.Text.Json;
using DotnetAiAppModel.SupportAssistant;
using DotnetAiAppModel.SupportAssistant.Providers;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails();
builder.Services.AddSupportAssistant(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();

app.MapPost(
    "/support",
    async (
        SupportRequest request,
        SupportAssistant assistant,
        CancellationToken cancellationToken) =>
    {
        var errors = SupportRequestValidation.Validate(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var response = await assistant.GetResponseAsync(request, cancellationToken);
        return Results.Ok(response);
    });

app.MapPost(
    "/support/stream",
    async (
        SupportRequest request,
        SupportAssistant assistant,
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
        await foreach (var update in assistant.GetStreamingResponseAsync(request, cancellationToken))
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
    (SupportRequest request, SupportRequestChannel queue) =>
    {
        var errors = SupportRequestValidation.Validate(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var correlationId = Guid.NewGuid().ToString("N");
        if (!queue.TryEnqueue(request with { CorrelationId = correlationId }))
        {
            return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        }

        return Results.Accepted(
            $"/support/queue/{correlationId}",
            new QueueAcceptedResponse(correlationId));
    });

app.Run();

public partial class Program
{
}
