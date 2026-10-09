using DotnetAiAppModel.SupportAssistant;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenAIChatClient = OpenAI.Chat.ChatClient;

namespace DotnetAiAppModel.SupportAssistant.Providers;

public static class ChatClientRegistration
{
    public static IServiceCollection AddSupportApplication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<SupportModelOptions>()
            .Bind(configuration.GetSection(SupportModelOptions.SectionName))
            .Validate(
                options => string.Equals(options.Provider, "scripted", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(options.Provider, "openai", StringComparison.OrdinalIgnoreCase),
                "SupportModel:Provider must be either 'scripted' or 'openai'.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.Model),
                "SupportModel:Model is required.")
            .Validate(
                options => options.MaxToolIterations > 0,
                "SupportModel:MaxToolIterations must be greater than zero.")
            .Validate(
                options => !string.Equals(options.Provider, "openai", StringComparison.OrdinalIgnoreCase)
                    || !string.IsNullOrWhiteSpace(options.OpenAiApiKey),
                "SupportModel:OpenAiApiKey is required when SupportModel:Provider is 'openai'.")
            .ValidateOnStart();
        services.AddOptions<SupportWorkOptions>()
            .Bind(configuration.GetSection(SupportWorkOptions.SectionName))
            .Validate(
                options => options.QueueCapacity > 0,
                "SupportWork:QueueCapacity must be greater than zero.")
            .ValidateOnStart();

        services.AddSingleton<ITicketStore, InMemoryTicketStore>();
        services.AddSingleton<IChatClient>(CreateChatClient);
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<ISupportConversationStore, InMemorySupportConversationStore>();
        services.AddSingleton<SupportActionPolicy>();
        services.AddSingleton<ISupportActionStore, InMemorySupportActionStore>();
        services.AddSingleton<ISupportWorkStore, InMemorySupportWorkStore>();
        services.AddSingleton(serviceProvider =>
            new SupportWorkChannel(
                serviceProvider.GetRequiredService<IOptions<SupportWorkOptions>>().Value.QueueCapacity));
        services.AddScoped<SupportConversationService>();
        services.AddScoped<SupportSpecialistFollowUpService>();
        services.AddScoped<SupportActionToolFactory>();
        services.AddScoped<SupportModelContextFactory>();
        services.AddScoped<SupportApplication>();
        services.AddScoped<SupportWorkSubmissionService>();
        services.AddScoped<SupportWorkItemProcessor>();
        services.AddHostedService<SupportWorker>();

        return services;
    }

    [Obsolete("Use AddSupportApplication instead.")]
    public static IServiceCollection AddSupportAssistant(
        this IServiceCollection services,
        IConfiguration configuration) =>
        services.AddSupportApplication(configuration);

    private static IChatClient CreateChatClient(IServiceProvider serviceProvider)
    {
        var options = serviceProvider.GetRequiredService<IOptions<SupportModelOptions>>().Value;
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

    private static IChatClient CreateOpenAiClient(SupportModelOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.OpenAiApiKey))
        {
            throw new InvalidOperationException(
                "SupportModel:OpenAiApiKey is required when SupportModel:Provider is 'openai'.");
        }

        return new OpenAIChatClient(options.Model, options.OpenAiApiKey).AsIChatClient();
    }
}
