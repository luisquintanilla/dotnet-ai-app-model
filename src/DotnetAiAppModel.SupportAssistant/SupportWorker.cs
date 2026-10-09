using System.Threading.Channels;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace DotnetAiAppModel.SupportAssistant;

public sealed class SupportRequestChannel
{
    private readonly Channel<SupportRequest> _channel;

    public SupportRequestChannel(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Queue capacity must be greater than zero.");
        }

        _channel = Channel.CreateBounded<SupportRequest>(
            new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });
    }

    public bool TryEnqueue(SupportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _channel.Writer.TryWrite(request);
    }

    public IAsyncEnumerable<SupportRequest> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);

    public void Complete(Exception? error = null) => _channel.Writer.TryComplete(error);
}

public sealed partial class SupportWorker : BackgroundService
{
    private readonly SupportRequestChannel _queue;
    private readonly IChatClient _chatClient;
    private readonly AIFunction _ticketLookup;
    private readonly SupportAssistantOptions _options;
    private readonly ILogger<SupportWorker> _logger;

    public SupportWorker(
        SupportRequestChannel queue,
        IChatClient chatClient,
        AIFunction ticketLookup,
        IOptions<SupportAssistantOptions> options,
        ILogger<SupportWorker> logger)
    {
        _queue = queue;
        _chatClient = chatClient;
        _ticketLookup = ticketLookup;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var request in _queue.ReadAllAsync(stoppingToken))
            {
                using var scope = _logger.BeginScope(new Dictionary<string, object?>
                {
                    ["SupportCorrelationId"] = request.CorrelationId ?? "queue",
                    ["SupportTicketId"] = request.TicketId
                });

                try
                {
                    await SupportHandlers.CompleteAsync(
                        request,
                        _chatClient,
                        _ticketLookup,
                        _options,
                        _logger,
                        stoppingToken);
                    LogRequestProcessed(_logger);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    LogRequestCanceled(_logger);
                    return;
                }
                catch (Exception exception)
                {
                    LogRequestFailed(_logger, exception);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            LogWorkerStopping(_logger);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Queued support request processed.")]
    private static partial void LogRequestProcessed(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Queued support request canceled during shutdown.")]
    private static partial void LogRequestCanceled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Queued support request failed.")]
    private static partial void LogRequestFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Support worker stopping.")]
    private static partial void LogWorkerStopping(ILogger logger);
}
