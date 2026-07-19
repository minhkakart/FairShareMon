using System.Net;

namespace FairShareMonApi.Tests.Infrastructure;

/// <summary>
/// A deterministic <see cref="HttpMessageHandler"/> for the bank-directory-provider tests: it never touches
/// the network, records every outbound request (so header/URL assertions are possible), and returns
/// whatever the supplied responder produces (which may itself throw to simulate a transport failure). Used
/// to drive the typed <c>VietQrApiClient</c> in unit tests and to override its primary handler inside the
/// endpoint tests' <see cref="WebApplicationFactory{TEntryPoint}"/>.
/// </summary>
public sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    private readonly List<HttpRequestMessage> _requests = [];

    /// <summary>Every request the handler was asked to send, in order.</summary>
    public IReadOnlyList<HttpRequestMessage> Requests => _requests;

    /// <summary>Convenience factory: always reply with the given JSON body and 200 OK.</summary>
    public static StubHttpMessageHandler Json(string json) =>
        new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        });

    /// <summary>Convenience factory: always reply with the given status and an empty body.</summary>
    public static StubHttpMessageHandler Status(HttpStatusCode status) =>
        new(_ => new HttpResponseMessage(status));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _requests.Add(request);
        return Task.FromResult(responder(request));
    }
}
