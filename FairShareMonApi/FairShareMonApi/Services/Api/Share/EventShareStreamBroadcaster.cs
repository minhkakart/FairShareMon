using System.Collections.Concurrent;
using System.Threading.Channels;
using DiDecoration.Attributes;

namespace FairShareMonApi.Services.Api.Share;

/// <summary>
/// Signal kinds pushed over the public-share SSE stream (planning/public-share-sse-updates.md).
/// <see cref="Updated"/> means "re-fetch the report/QR"; the other two are terminal - the stream
/// closes right after emitting one.
/// </summary>
public enum EventShareStreamSignalType { Updated, Revoked, Expired }

/// <summary>Trivial "something changed" signal - never carries the report payload itself (Decision 1).</summary>
public readonly record struct EventShareStreamSignal(EventShareStreamSignalType Type);

/// <summary>One subscriber's read side of the broadcast; disposing unregisters it from the broadcaster.</summary>
public interface IEventShareStreamSubscription : IDisposable
{
    ChannelReader<EventShareStreamSignal> Reader { get; }
}

/// <summary>
/// In-process fan-out of share-link change signals, keyed by token (planning/public-share-sse-updates.md
/// Step 1). Deliberately leaf-level - no dependency on any other service, so this Singleton has no
/// scoped dependency for DiDecoration to reject.
/// </summary>
public interface IEventShareStreamBroadcaster
{
    /// <summary>Subscribes to signals for one token; multiple subscribers per token fan out independently.</summary>
    IEventShareStreamSubscription Subscribe(string token);

    /// <summary>The shared event's settled/outstanding overlay changed.</summary>
    void PublishUpdated(string token);

    /// <summary>The owner explicitly revoked/regenerated the link (OQ2a).</summary>
    void PublishRevoked(string token);

    /// <summary>A heartbeat re-check found the link naturally expired (OQ2a).</summary>
    void PublishExpired(string token);
}

[SingletonService(typeof(IEventShareStreamBroadcaster))]
public sealed class EventShareStreamBroadcaster : IEventShareStreamBroadcaster
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, ChannelWriter<EventShareStreamSignal>>> _subscribersByToken = new();

    public IEventShareStreamSubscription Subscribe(string token)
    {
        var channel = Channel.CreateBounded<EventShareStreamSignal>(new BoundedChannelOptions(4)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

        var subscriptionId = Guid.NewGuid();
        var bucket = _subscribersByToken.GetOrAdd(token, static _ => new ConcurrentDictionary<Guid, ChannelWriter<EventShareStreamSignal>>());
        bucket[subscriptionId] = channel.Writer;

        return new Subscription(this, token, subscriptionId, channel.Reader);
    }

    public void PublishUpdated(string token) => Publish(token, EventShareStreamSignalType.Updated, terminal: false);

    public void PublishRevoked(string token) => Publish(token, EventShareStreamSignalType.Revoked, terminal: true);

    public void PublishExpired(string token) => Publish(token, EventShareStreamSignalType.Expired, terminal: true);

    private void Publish(string token, EventShareStreamSignalType type, bool terminal)
    {
        if (!_subscribersByToken.TryGetValue(token, out var bucket))
            return;

        var signal = new EventShareStreamSignal(type);
        foreach (var writer in bucket.Values)
        {
            writer.TryWrite(signal); // DropOldest under a full/stalled reader - never blocks the publisher.
            if (terminal)
                writer.TryComplete(); // Resource hygiene only - the controller's read loop breaks on its own.
        }
    }

    private void Unsubscribe(string token, Guid subscriptionId)
    {
        if (!_subscribersByToken.TryGetValue(token, out var bucket))
            return;

        bucket.TryRemove(subscriptionId, out _);
        if (bucket.IsEmpty)
            _subscribersByToken.TryRemove(token, out _);
    }

    private sealed class Subscription(EventShareStreamBroadcaster owner, string token, Guid subscriptionId, ChannelReader<EventShareStreamSignal> reader)
        : IEventShareStreamSubscription
    {
        private bool _disposed;

        public ChannelReader<EventShareStreamSignal> Reader { get; } = reader;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            owner.Unsubscribe(token, subscriptionId);
        }
    }
}
