using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DotnetAiAppModel.SupportAssistant;

public delegate Task<SupportResponse> SupportRequestHandler(
    SupportRequest request,
    CancellationToken cancellationToken = default);

public delegate IAsyncEnumerable<ChatResponseUpdate> SupportStreamHandler(
    SupportRequest request,
    CancellationToken cancellationToken = default);

public static class SupportHandlerComposition
{
    public static IServiceCollection AddSupportHandlers(
        this IServiceCollection services)
    {
        services.AddSingleton<SupportRequestHandler>(serviceProvider =>
        {
            var chatClient = serviceProvider.GetRequiredService<IChatClient>();
            var ticketLookup = serviceProvider.GetRequiredService<AIFunction>();
            var options = serviceProvider.GetRequiredService<IOptions<SupportAssistantOptions>>().Value;
            var logger = serviceProvider
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("SupportHandlers");

            return (request, cancellationToken) =>
                SupportHandlers.CompleteAsync(
                    request,
                    chatClient,
                    ticketLookup,
                    options,
                    logger,
                    cancellationToken);
        });

        services.AddSingleton<SupportStreamHandler>(serviceProvider =>
        {
            var chatClient = serviceProvider.GetRequiredService<IChatClient>();
            var ticketLookup = serviceProvider.GetRequiredService<AIFunction>();
            var options = serviceProvider.GetRequiredService<IOptions<SupportAssistantOptions>>().Value;
            var logger = serviceProvider
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("SupportHandlers");

            return (request, cancellationToken) =>
                SupportHandlers.StreamAsync(
                    request,
                    chatClient,
                    ticketLookup,
                    options,
                    logger,
                    cancellationToken);
        });

        return services;
    }
}
