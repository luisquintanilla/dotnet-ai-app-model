namespace DotnetAiAppModel.SupportAssistant.Tests;

public sealed class SupportActionTests
{
    [Fact]
    public void SpecialistFollowUpPolicyRequiresTicketCustomerReasonAndExplicitRequest()
    {
        var policy = new SupportActionPolicy();

        Assert.False(
            policy.EvaluateSpecialistFollowUp(
                new SupportRequest("Please help.", CustomerId: "CUS-42"),
                "reason").Allowed);
        Assert.False(
            policy.EvaluateSpecialistFollowUp(
                new SupportRequest("Please help.", "SUP-1001"),
                "reason").Allowed);
        Assert.False(
            policy.EvaluateSpecialistFollowUp(
                new SupportRequest("Please escalate.", "SUP-1001", "CUS-42"),
                string.Empty).Allowed);
        Assert.False(
            policy.EvaluateSpecialistFollowUp(
                new SupportRequest("Please help.", "SUP-1001", "CUS-42"),
                "reason").Allowed);
        Assert.True(
            policy.EvaluateSpecialistFollowUp(
                new SupportRequest("Please escalate to a specialist.", "SUP-1001", "CUS-42"),
                "reason").Allowed);
    }

    [Fact]
    public async Task ActionStoreRecordsOneEffectPerIdempotencyKey()
    {
        using var time = new TestTimeProvider();
        var store = new InMemorySupportActionStore(time);
        var request = new SupportActionRequest(
            "conversation-42",
            "correlation-42",
            "SUP-1001",
            "CUS-42",
            "Please escalate.",
            "follow-up-42");

        var applied = await store.RequestSpecialistFollowUpAsync(request);
        var duplicate = await store.RequestSpecialistFollowUpAsync(request);

        Assert.Equal("applied", applied.Status);
        Assert.Equal("already_applied", duplicate.Status);
        Assert.True(duplicate.AlreadyApplied);
        Assert.Single(store.GetRecordedActions());
    }
}
