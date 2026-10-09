using DotnetAiAppModel.SupportAssistant.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DotnetAiAppModel.SupportAssistant.Tests;

public sealed class SupportCompositionTests
{
    [Fact]
    public void ApplicationServicesAreScopedAndHostedWorkerUsesScopeBridge()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSupportApplication(
            new ConfigurationBuilder()
                .AddInMemoryCollection()
                .Build());

        AssertScoped<SupportConversationService>(services);
        AssertScoped<SupportSpecialistFollowUpService>(services);
        AssertScoped<SupportActionToolFactory>(services);
        AssertScoped<SupportModelContextFactory>(services);
        AssertScoped<SupportApplication>(services);
        AssertScoped<SupportWorkSubmissionService>(services);
        AssertScoped<SupportWorkItemProcessor>(services);

        var workerDescriptor = services.Single(descriptor =>
            descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType == typeof(SupportWorker));
        Assert.Equal(ServiceLifetime.Singleton, workerDescriptor.Lifetime);

        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateScopes = true
            });
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();

        Assert.NotSame(
            firstScope.ServiceProvider.GetRequiredService<SupportApplication>(),
            secondScope.ServiceProvider.GetRequiredService<SupportApplication>());
        Assert.NotSame(
            firstScope.ServiceProvider.GetRequiredService<SupportWorkItemProcessor>(),
            secondScope.ServiceProvider.GetRequiredService<SupportWorkItemProcessor>());

        var hostedServices = provider.GetServices<IHostedService>();
        Assert.IsType<SupportWorker>(Assert.Single(hostedServices));
    }

    private static void AssertScoped<T>(IServiceCollection services)
        where T : class
    {
        var descriptor = services.Single(service => service.ServiceType == typeof(T));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }
}
