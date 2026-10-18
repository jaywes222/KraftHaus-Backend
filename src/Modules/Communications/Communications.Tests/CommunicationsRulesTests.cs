using Common.Domain.Communications;
using Communications.Application;
using Communications.Application.Abstractions;
using Communications.Application.Messages;
using Communications.Domain.Broadcasts;
using Communications.Domain.Common;
using Communications.Domain.Messages;
using Communications.Domain.OptOuts;
using Communications.Domain.Templates;
using Xunit;

namespace Communications.Tests;

public sealed class CommunicationsRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);

    private static Broadcast Draft(BroadcastAudience audience = BroadcastAudience.WaitlistMarketing, string body = "Hi {firstName}, sales are open!") =>
        Broadcast.Create(new BroadcastId(Guid.CreateVersion7()), "Sales open", Guid.CreateVersion7(), audience, body).Value!;

    [Fact]
    public void ABroadcastNeedsANameAnEventAnAudienceAndABodyOfAtMostThreeSms()
    {
        var invalid = Broadcast.Create(new BroadcastId(Guid.CreateVersion7()), " ", Guid.Empty, (BroadcastAudience)99, new string('x', 460));

        Assert.True(invalid.IsFailure);
        Assert.Equal("Communications.BroadcastInvalid", invalid.Error!.Key);
        Assert.Equal(4, invalid.Error.SubErrors!.Count);
    }

    [Fact]
    public void ADraftIsScheduledSentAndCompletedInOrder()
    {
        var broadcast = Draft();

        Assert.False(broadcast.Schedule(null, Now, 90).IsFailure);
        Assert.Equal(BroadcastStatus.Scheduled, broadcast.Status);
        Assert.Equal(Now, broadcast.ScheduledFor);
        Assert.True(broadcast.IsDue(Now));

        // Only a draft can be edited.
        Assert.Equal(CommunicationsErrors.NotEditable.Key, broadcast.Update("x", BroadcastAudience.TicketHolders, "y").Error!.Key);

        Assert.False(broadcast.Start(10, 2, Now).IsFailure);
        Assert.Equal(BroadcastStatus.Sending, broadcast.Status);
        Assert.Equal(CommunicationsErrors.NotCancellable.Key, broadcast.Cancel(Now).Error!.Key);

        broadcast.RecordBatch(7, 1);
        broadcast.RecordBatch(2, 0);
        broadcast.Complete(Now.AddMinutes(1));

        Assert.Equal(BroadcastStatus.Completed, broadcast.Status);
        Assert.Equal((10, 2, 9, 1), (broadcast.Recipients, broadcast.Skipped, broadcast.Sent, broadcast.Failed));
        Assert.Equal(CommunicationsErrors.AlreadyFinished.Key, broadcast.Schedule(null, Now, 90).Error!.Key);
    }

    [Fact]
    public void ALaterTimeWaitsAndTooFarAheadIsRefused()
    {
        var broadcast = Draft();

        Assert.False(broadcast.Schedule(Now.AddHours(2), Now, 90).IsFailure);
        Assert.False(broadcast.IsDue(Now));
        Assert.True(broadcast.Start(1, 0, Now).IsFailure);
        Assert.True(broadcast.IsDue(Now.AddHours(2)));

        Assert.True(Draft().Schedule(Now.AddDays(91), Now, 90).IsFailure);
    }

    [Fact]
    public void ScheduledBroadcastsCanBeCancelled()
    {
        var broadcast = Draft();
        broadcast.Schedule(Now.AddDays(1), Now, 90);

        Assert.False(broadcast.Cancel(Now).IsFailure);
        Assert.Equal(BroadcastStatus.Cancelled, broadcast.Status);
        Assert.False(broadcast.IsDue(Now.AddDays(2)));
    }

    [Fact]
    public void RenderingFillsTheNameAndAddsTheFooterToMarketingOnly()
    {
        var marketing = Draft();
        Assert.Equal("Hi Wanjiru, sales are open! Reply STOP to opt out", marketing.RenderFor(" Wanjiru ", "Reply STOP to opt out"));
        Assert.Equal("Hi there, sales are open!", marketing.RenderFor(null, null));

        var info = Draft(BroadcastAudience.TicketHolders, "Gates open at 6pm, {FIRSTNAME}.");
        Assert.False(info.IsMarketing);
        Assert.Equal("Gates open at 6pm, Otieno.", info.RenderFor("Otieno", "Reply STOP to opt out"));
    }

    [Fact]
    public void TheLogMasksAnythingThatLooksLikeACode()
    {
        Assert.Equal("Your KraftHaus verification code is ••••••.", MessageBody.Mask("Your KraftHaus verification code is 482913."));
        Assert.Equal("Paid: 2 tickets. Order ••••••••. Total KES 1,500", MessageBody.Mask("Paid: 2 tickets. Order KH7Q2M9X. Total KES 1,500"));
        Assert.Equal("See you at KRAFTHAUS tonight", MessageBody.Mask("See you at KRAFTHAUS tonight"));
    }

    [Fact]
    public void SmsPartsFollowGsmAndUnicodeLimits()
    {
        Assert.Equal(1, MessageBody.SmsParts(new string('a', 160)));
        Assert.Equal(2, MessageBody.SmsParts(new string('a', 161)));
        Assert.Equal(3, MessageBody.SmsParts(new string('a', 459)));
        Assert.Equal(2, MessageBody.SmsParts(new string('a', 69) + "🎶"));
    }

    [Fact]
    public void DeliveryReportsAreIdempotentAndDeliveredIsFinal()
    {
        var message = Message.Transactional(new MessageId(Guid.CreateVersion7()), MessageChannel.Sms, "+254712345678", null, "Hello", "abc", null, Now);
        Assert.Equal(MessageStatus.Sent, message.Status);

        Assert.True(message.ApplyDeliveryReport(true, "DeliveredToTerminal", Now));
        Assert.False(message.ApplyDeliveryReport(true, "DeliveredToTerminal", Now));
        Assert.False(message.ApplyDeliveryReport(false, "Failed", Now));
        Assert.Equal(MessageStatus.Delivered, message.Status);

        var failed = Message.Transactional(new MessageId(Guid.CreateVersion7()), MessageChannel.Sms, "+254712345678", null, "Hello", null, "HttpRequestException", Now);
        Assert.Equal(MessageStatus.Failed, failed.Status);
        Assert.False(failed.ApplyDeliveryReport(false, "Failed", Now));
    }

    [Theory]
    [InlineData("DeliveredToTerminal", true)]
    [InlineData("delivered", true)]
    [InlineData("DELIVRD", true)]
    [InlineData("Success", true)]
    [InlineData("Failed", false)]
    [InlineData("Undelivered", false)]
    [InlineData("Rejected", false)]
    [InlineData("Expired", false)]
    [InlineData("NotDelivered", false)]
    [InlineData("Submitted", null)]
    [InlineData("Buffered", null)]
    [InlineData("", null)]
    public void ProviderStatusesReduceToDeliveredFailedOrUnknown(string status, bool? expected) =>
        Assert.Equal(expected, DeliveryStatus.Parse(status));

    [Fact]
    public void OptOutsToggleWithoutLosingTheRow()
    {
        var optOut = OptOut.Create(new OptOutId(Guid.CreateVersion7()), "+254712345678", OptOutSource.Customer, Now);
        Assert.True(optOut.IsActive);

        optOut.Deactivate(OptOutSource.Customer, Now.AddDays(1));
        optOut.Deactivate(OptOutSource.Customer, Now.AddDays(2));
        Assert.False(optOut.IsActive);
        Assert.Equal(Now.AddDays(1), optOut.OptedInAt);

        optOut.Activate(OptOutSource.Staff, Now.AddDays(3));
        Assert.True(optOut.IsActive);
        Assert.Equal(OptOutSource.Staff, optOut.Source);
        Assert.Equal(3, optOut.Events.Count);
    }

    [Fact]
    public void TemplatesValidateAndArchive()
    {
        Assert.True(MessageTemplate.Create(new MessageTemplateId(Guid.CreateVersion7()), "", "").IsFailure);

        var template = MessageTemplate.Create(new MessageTemplateId(Guid.CreateVersion7()), "Gates open", "Gates open at 6pm.").Value!;
        template.Archive();
        template.Archive();

        Assert.True(template.IsArchived);
        Assert.Equal(2, template.Events.Count);
    }

    [Fact]
    public void SummaryFailureRateIgnoresQueuedMessages()
    {
        var summary = MessageLogService.Summary(Now, new MessageCounts(Queued: 50, Sent: 30, Delivered: 60, Failed: 10));

        Assert.Equal(0.1m, summary.FailureRate);
        Assert.Equal(0m, MessageLogService.Summary(Now, new MessageCounts(5, 0, 0, 0)).FailureRate);
    }

    [Fact]
    public void OptionsRefuseAShortWebhookToken()
    {
        var result = new CommunicationsOptionsValidator().Validate(null, new CommunicationsOptions { DeliveryReportToken = "short" });
        Assert.True(result.Failed);

        Assert.True(new CommunicationsOptionsValidator().Validate(null, new CommunicationsOptions()).Succeeded);
    }
}
