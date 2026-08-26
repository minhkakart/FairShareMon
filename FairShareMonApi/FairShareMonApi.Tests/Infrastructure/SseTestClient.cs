using System.Text;

namespace FairShareMonApi.Tests.Infrastructure;

/// <summary>One dispatched Server-Sent Event frame: an <c>event:</c> name (null when the server sent
/// a bare <c>data:</c>-only frame - not used by this feature, but the SSE spec allows it) plus the
/// concatenated <c>data:</c> payload.</summary>
public sealed record SseFrame(string? EventName, string Data);

/// <summary>
/// Minimal Server-Sent Events client for tests that hold a streaming HTTP response open
/// (planning/public-share-sse-updates.md) - a genuinely new technique for this test suite: every
/// existing endpoint test does a single request/response round trip. Sends the request with
/// <see cref="HttpCompletionOption.ResponseHeadersRead"/> so headers return without waiting for the
/// body, then parses the wire format incrementally with a <see cref="StreamReader"/>: a blank line
/// dispatches the buffered frame, a line starting with <c>:</c> is a comment/heartbeat, <c>event:</c>/
/// <c>data:</c> lines accumulate into the next dispatch. Every read is bounded by a caller-supplied
/// timeout so a regression that stops publishing hangs the TEST (a clear timeout failure), never the
/// whole CI run.
/// </summary>
public sealed class SseTestClient : IAsyncDisposable
{
    private readonly HttpResponseMessage _response;
    private readonly Stream _stream;
    private readonly StreamReader _reader;

    private SseTestClient(HttpResponseMessage response, Stream stream, StreamReader reader)
    {
        _response = response;
        _stream = stream;
        _reader = reader;
    }

    public HttpResponseMessage Response => _response;

    /// <summary>Opens the stream request. Headers are available immediately on the returned client's <see cref="Response"/>;
    /// the body is read incrementally via <see cref="ReadFrameAsync"/> / <see cref="ReadCommentAsync"/>.</summary>
    public static async Task<SseTestClient> ConnectAsync(HttpClient client, string requestUri, CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return new SseTestClient(response, stream, new StreamReader(stream, Encoding.UTF8));
    }

    /// <summary>
    /// Reads lines until a full frame dispatches (a blank line after at least one <c>event:</c>/<c>data:</c>
    /// line), skipping/reporting comment lines via <paramref name="onComment"/> along the way. Throws
    /// <see cref="TimeoutException"/> if no frame dispatches within <paramref name="timeout"/> - this is the
    /// expected outcome for a deliberate "nothing should arrive" negative assertion.
    /// </summary>
    public async Task<SseFrame> ReadFrameAsync(TimeSpan timeout, Action<string>? onComment = null)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            string? eventName = null;
            var data = new StringBuilder();
            var sawField = false;
            while (true)
            {
                var line = await _reader.ReadLineAsync(cts.Token);
                if (line is null)
                    throw new IOException("SSE stream ended before a frame was dispatched.");

                if (line.Length == 0)
                {
                    if (sawField)
                        return new SseFrame(eventName, data.ToString());
                    continue; // stray blank line before any field - ignore
                }

                if (line.StartsWith(':'))
                {
                    onComment?.Invoke(line);
                    continue;
                }

                sawField = true;
                if (line.StartsWith("event:"))
                    eventName = line["event:".Length..].Trim();
                else if (line.StartsWith("data:"))
                    data.Append(line["data:".Length..].Trim());
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            throw new TimeoutException($"No SSE frame dispatched within {timeout}.");
        }
    }

    /// <summary>Reads raw lines until a <c>:</c>-prefixed comment (heartbeat) line arrives. Throws
    /// <see cref="TimeoutException"/> if none arrives within <paramref name="timeout"/>.</summary>
    public async Task<string> ReadCommentAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            while (true)
            {
                var line = await _reader.ReadLineAsync(cts.Token)
                    ?? throw new IOException("SSE stream ended before a comment line arrived.");
                if (line.StartsWith(':'))
                    return line;
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            throw new TimeoutException($"No SSE comment line within {timeout}.");
        }
    }

    /// <summary>Asserts nothing at all is written within <paramref name="timeout"/> (neither a frame nor a
    /// comment) - used for the "no active link, no signal" negative assertion, bounded like every other read.</summary>
    public async Task AssertSilentAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            var line = await _reader.ReadLineAsync(cts.Token);
            if (line is not null)
                throw new InvalidOperationException($"Expected silence but read a line from the SSE stream: \"{line}\".");
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // Expected: nothing arrived within the bound.
        }
    }

    public async ValueTask DisposeAsync()
    {
        _reader.Dispose();
        await _stream.DisposeAsync();
        _response.Dispose();
    }
}
