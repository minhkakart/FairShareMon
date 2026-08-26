using System.Threading.Channels;
using FairShareMonApi.Services.Api.Share;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Pure unit tests for <see cref="EventShareStreamBroadcaster"/> (planning/public-share-sse-updates.md
/// Step 1) - no DB, no Redis, no HTTP. Proves subscribe+publish delivery, multi-subscriber fan-out with
/// cross-token isolation, that <see cref="IEventShareStreamBroadcaster.PublishRevoked"/> /
/// <see cref="IEventShareStreamBroadcaster.PublishExpired"/> complete the channel (Decision 5's terminal
/// signals), that the bounded capacity-4 drop-oldest channel never blocks/throws for a publisher even
/// far past capacity, and that disposing a subscription is a silent no-op for later publishes.
/// </summary>
public class EventShareStreamBroadcasterTests
{
    private const string TokenA = "token-a";
    private const string TokenB = "token-b";

    private readonly EventShareStreamBroadcaster _broadcaster = new();

    [Fact]
    public async Task Subscribe_ThenPublishUpdated_SubscriberReceivesExactlyOneUpdatedSignal()
    {
        using var subscription = _broadcaster.Subscribe(TokenA);

        _broadcaster.PublishUpdated(TokenA);

        var signal = await subscription.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(EventShareStreamSignalType.Updated, signal.Type);
        Assert.False(subscription.Reader.TryRead(out _)); // exactly one - nothing else queued
    }

    [Fact]
    public async Task PublishUpdated_TwoSubscribersSameToken_BothReceiveTheSignal()
    {
        using var first = _broadcaster.Subscribe(TokenA);
        using var second = _broadcaster.Subscribe(TokenA);

        _broadcaster.PublishUpdated(TokenA);

        var firstSignal = await first.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        var secondSignal = await second.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(EventShareStreamSignalType.Updated, firstSignal.Type);
        Assert.Equal(EventShareStreamSignalType.Updated, secondSignal.Type);
    }

    [Fact]
    public async Task PublishUpdated_DifferentToken_SubscriberNeverReceivesIt()
    {
        using var subscriptionOnA = _broadcaster.Subscribe(TokenA);
        using var subscriptionOnB = _broadcaster.Subscribe(TokenB);

        _broadcaster.PublishUpdated(TokenA);

        var signal = await subscriptionOnA.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(EventShareStreamSignalType.Updated, signal.Type);
        Assert.False(subscriptionOnB.Reader.TryRead(out _)); // isolation: token B's subscriber saw nothing
    }

    [Fact]
    public async Task PublishRevoked_SubscriberReceivesRevokedSignalAndChannelCompletesAfterward()
    {
        using var subscription = _broadcaster.Subscribe(TokenA);

        _broadcaster.PublishRevoked(TokenA);

        var signal = await subscription.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(EventShareStreamSignalType.Revoked, signal.Type);

        // Channel completed (TryComplete after the terminal write) - a further ReadAsync must not hang;
        // it throws ChannelClosedException instead of blocking forever.
        await Assert.ThrowsAsync<ChannelClosedException>(() =>
            subscription.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task PublishExpired_SubscriberReceivesExpiredSignalAndChannelCompletesAfterward()
    {
        using var subscription = _broadcaster.Subscribe(TokenA);

        _broadcaster.PublishExpired(TokenA);

        var signal = await subscription.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(EventShareStreamSignalType.Expired, signal.Type);

        await Assert.ThrowsAsync<ChannelClosedException>(() =>
            subscription.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void PublishUpdated_FarMoreThanBoundedCapacityWithoutDraining_NeverThrowsAndDropsOldest()
    {
        using var subscription = _broadcaster.Subscribe(TokenA);

        // Capacity is 4 (Decision 5); publish 50 without ever reading - a publisher (a mutation-service
        // request thread) must never block or throw on a slow/stalled reader.
        for (var i = 0; i < 50; i++)
            _broadcaster.PublishUpdated(TokenA);

        var drained = 0;
        while (subscription.Reader.TryRead(out _))
            drained++;

        Assert.True(drained <= 4, $"Expected at most the bounded capacity (4) to survive DropOldest, got {drained}.");
    }

    [Fact]
    public void Dispose_RemovesSubscription_LaterPublishIsSilentNoOpWithNoDeliveryToTheDisposedReader()
    {
        var subscription = _broadcaster.Subscribe(TokenA);
        subscription.Dispose();

        _broadcaster.PublishUpdated(TokenA); // must not throw even though nobody is subscribed anymore

        Assert.False(subscription.Reader.TryRead(out _)); // no delivery to the disposed reader
    }

    [Fact]
    public async Task Dispose_OneOfTwoSubscribers_TheOtherStillReceivesPublishes()
    {
        var toDispose = _broadcaster.Subscribe(TokenA);
        using var remaining = _broadcaster.Subscribe(TokenA);
        toDispose.Dispose();

        _broadcaster.PublishUpdated(TokenA);

        var signal = await remaining.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(EventShareStreamSignalType.Updated, signal.Type);
        Assert.False(toDispose.Reader.TryRead(out _));
    }
}
