using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace DotnetAiAppModel.SupportAssistant;

public sealed class SupportModelContextFactory
{
    private readonly SupportActionToolFactory _actionToolFactory;
    private readonly SupportModelOptions _options;

    public SupportModelContextFactory(
        SupportActionToolFactory actionToolFactory,
        IOptions<SupportModelOptions> options)
    {
        _actionToolFactory = actionToolFactory;
        _options = options.Value;
    }

    public SupportModelContext Create(SupportConversationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var messages = context.Conversation.Turns
            .Select(turn => new ChatMessage(
                turn.Role == SupportConversationRole.Customer
                    ? ChatRole.User
                    : ChatRole.Assistant,
                turn.Text))
            .ToList();

        var currentMessage = context.Request.TicketId is null
            ? context.Request.Message
            : $"{context.Request.Message}{Environment.NewLine}Ticket ID: {context.Request.TicketId}";
        messages.Add(new ChatMessage(ChatRole.User, currentMessage));

        var tools = _actionToolFactory.Create(context);
        var options = new ChatOptions
        {
            Instructions = _options.SystemPrompt,
            Tools = tools.Cast<AITool>().ToList(),
            AllowMultipleToolCalls = false
        };

        return new SupportModelContext(messages, options, context.ActionResults);
    }
}

public sealed record SupportModelContext(
    IReadOnlyList<ChatMessage> Messages,
    ChatOptions Options,
    IReadOnlyList<SupportActionResult> ActionResults);
