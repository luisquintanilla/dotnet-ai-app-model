using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenAIChatClient = OpenAI.Chat.ChatClient;

namespace DotnetAiAppModel.SupportAssistant.Providers;

public static class ChatClientRegistration
{
    public static IServiceCollection AddSupportAssistant(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<SupportAssistantOptions>()
            .Bind(configuration.GetSection(SupportAssistantOptions.SectionName))
            .Validate(
                options => string.Equals(options.Provider, "scripted", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(options.Provider, "openai", StringComparison.OrdinalIgnoreCase),
                "SupportAssistant:Provider must be either 'scripted' or 'openai'.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.Model),
                "SupportAssistant:Model is required.")
            .Validate(
                options => options.QueueCapacity > 0,
                "SupportAssistant:QueueCapacity must be greater than zero.")
            .Validate(
                options => options.MaxToolIterations > 0,
                "SupportAssistant:MaxToolIterations must be greater than zero.")
            .Validate(
                options => !string.Equals(options.Provider, "openai", StringComparison.OrdinalIgnoreCase)
                    || !string.IsNullOrWhiteSpace(options.OpenAiApiKey),
                "SupportAssistant:OpenAiApiKey is required when SupportAssistant:Provider is 'openai'.")
            .ValidateOnStart();

        services.AddSingleton<ITicketStore, InMemoryTicketStore>();
        services.AddSingleton<AIFunction>(serviceProvider =>
            SupportTools.CreateTicketLookupFunction(
                serviceProvider.GetRequiredService<ITicketStore>()));
        services.AddSingleton<IChatClient>(CreateChatClient);
        services.AddSingleton(serviceProvider =>
            new SupportRequestChannel(
                serviceProvider.GetRequiredService<IOptions<SupportAssistantOptions>>().Value.QueueCapacity));
        services.AddSingleton<SupportAssistant>();
        services.AddHostedService<SupportWorker>();

        return services;
    }

    private static IChatClient CreateChatClient(IServiceProvider serviceProvider)
    {
        var options = serviceProvider.GetRequiredService<IOptions<SupportAssistantOptions>>().Value;
        var innerClient = options.Provider.ToLowerInvariant() switch
        {
            "scripted" => new ScriptedChatClient(options.Model),
            "openai" => CreateOpenAiClient(options),
            _ => throw new InvalidOperationException(
                $"Unsupported support assistant provider '{options.Provider}'.")
        };

        return innerClient
            .AsBuilder()
            .UseFunctionInvocation(
                serviceProvider.GetRequiredService<ILoggerFactory>(),
                functionClient => functionClient.MaximumIterationsPerRequest = options.MaxToolIterations)
            .Build(serviceProvider);
    }

    private static IChatClient CreateOpenAiClient(SupportAssistantOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.OpenAiApiKey))
        {
            throw new InvalidOperationException(
                "SupportAssistant:OpenAiApiKey is required when SupportAssistant:Provider is 'openai'.");
        }

        return new OpenAIChatClient(options.Model, options.OpenAiApiKey).AsIChatClient();
    }
}
