using DotnetAiAppModel.SupportAssistant.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DotnetAiAppModel.SupportAssistant.Tests;

internal sealed class TestSupportApplicationHost : IDisposable
{
    private readonly ServiceProvider _services;
    private readonly IServiceScope _applicationScope;

    private TestSupportApplicationHost(ServiceProvider services)
    {
        _services = services;
        _applicationScope = services.CreateScope();
        var scopedServices = _applicationScope.ServiceProvider;
        Application = scopedServices.GetRequiredService<SupportApplication>();
        ConversationStore = scopedServices.GetRequiredService<InMemorySupportConversationStore>();
        ActionStore = scopedServices.GetRequiredService<InMemorySupportActionStore>();
        WorkStore = scopedServices.GetRequiredService<InMemorySupportWorkStore>();
        WorkChannel = scopedServices.GetRequiredService<SupportWorkChannel>();
        WorkSubmission = scopedServices.GetRequiredService<SupportWorkSubmissionService>();
        TimeProvider = scopedServices.GetRequiredService<TimeProvider>();
    }

    public SupportApplication Application { get; }

    public InMemorySupportConversationStore ConversationStore { get; }

    public InMemorySupportActionStore ActionStore { get; }

    public InMemorySupportWorkStore WorkStore { get; }

    public SupportWorkChannel WorkChannel { get; }

    public SupportWorkSubmissionService WorkSubmission { get; }

    public TimeProvider TimeProvider { get; }

    public IServiceProvider Services => _services;

    public static TestSupportApplicationHost Create(
        IChatClient? client = null,
        ITicketStore? ticketStore = null,
        TimeProvider? timeProvider = null,
        int queueCapacity = 8)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(timeProvider ?? TimeProvider.System);
        services.AddSingleton<IChatClient>(serviceProvider =>
        {
            var innerClient = client ?? new ScriptedChatClient("scripted-test");
            return innerClient
                .AsBuilder()
                .UseFunctionInvocation(
                    serviceProvider.GetRequiredService<ILoggerFactory>(),
                    functionClient => functionClient.MaximumIterationsPerRequest = 4)
                .Build(serviceProvider);
        });
        services.AddSingleton(ticketStore ?? new InMemoryTicketStore());
        services.AddSingleton<ISupportConversationStore, InMemorySupportConversationStore>();
        services.AddSingleton<InMemorySupportConversationStore>(
            serviceProvider => (InMemorySupportConversationStore)
                serviceProvider.GetRequiredService<ISupportConversationStore>());
        services.AddSingleton<SupportActionPolicy>();
        services.AddSingleton<ISupportActionStore, InMemorySupportActionStore>(
            serviceProvider => new InMemorySupportActionStore(
                serviceProvider.GetRequiredService<TimeProvider>()));
        services.AddSingleton<InMemorySupportActionStore>(
            serviceProvider => (InMemorySupportActionStore)
                serviceProvider.GetRequiredService<ISupportActionStore>());
        services.AddSingleton<ISupportWorkStore, InMemorySupportWorkStore>();
        services.AddSingleton<InMemorySupportWorkStore>(
            serviceProvider => (InMemorySupportWorkStore)
                serviceProvider.GetRequiredService<ISupportWorkStore>());
        services.AddSingleton(new SupportWorkChannel(queueCapacity));
        services.AddSingleton<IOptions<SupportModelOptions>>(
            Options.Create(new SupportModelOptions
            {
                Provider = "scripted",
                Model = "scripted-test",
                MaxToolIterations = 4
            }));
        services.AddSingleton<IOptions<SupportWorkOptions>>(
            Options.Create(new SupportWorkOptions
            {
                QueueCapacity = queueCapacity
            }));
        services.AddScoped<SupportConversationService>();
        services.AddScoped<SupportSpecialistFollowUpService>();
        services.AddScoped<SupportActionToolFactory>();
        services.AddScoped<SupportModelContextFactory>();
        services.AddScoped<SupportApplication>();
        services.AddScoped<SupportWorkSubmissionService>();
        services.AddScoped<SupportWorkItemProcessor>();

        return new TestSupportApplicationHost(
            services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true
            }));
    }

    public void Dispose()
    {
        _applicationScope.Dispose();
        _services.Dispose();
    }
}

internal sealed class TestTimeProvider : TimeProvider, IDisposable
{
    private DateTimeOffset _now;

    public TestTimeProvider(
        DateTimeOffset? initial = null)
    {
        _now = initial ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan duration) => _now = _now.Add(duration);

    public void Dispose()
    {
    }
}
