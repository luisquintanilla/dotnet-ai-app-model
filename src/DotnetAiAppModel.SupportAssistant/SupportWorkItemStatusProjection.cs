namespace DotnetAiAppModel.SupportAssistant;

public static class SupportWorkItemStatusProjection
{
    public static string GetStatusUrl(string workItemId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workItemId);
        return $"/support/queue/{workItemId}";
    }

    public static SupportWorkItemStatusResponse ToResponse(
        SupportWorkItem workItem)
    {
        ArgumentNullException.ThrowIfNull(workItem);

        return new SupportWorkItemStatusResponse(
            workItem.WorkItemId,
            workItem.CorrelationId,
            workItem.Status.ToString().ToLowerInvariant(),
            workItem.CreatedAt,
            workItem.StartedAt,
            workItem.CompletedAt,
            workItem.Response,
            workItem.Error);
    }
}
