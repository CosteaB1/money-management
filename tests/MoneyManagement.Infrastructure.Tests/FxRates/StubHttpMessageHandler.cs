using System.Net;

namespace MoneyManagement.Infrastructure.Tests.FxRates;

/// <summary>
/// Test double for <see cref="HttpMessageHandler"/> that lets a single test
/// dictate the response (or the exception) for the next request. No live
/// network is ever touched — the production BNM URL is never reached.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;

    public Uri? LastRequestUri { get; private set; }

    private StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) =>
        _responder = responder;

    public static StubHttpMessageHandler ReturnsOk(string body) =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body),
        }));

    public static StubHttpMessageHandler ReturnsStatus(HttpStatusCode status) =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(string.Empty),
        }));

    /// <summary>
    /// Mimics an <see cref="HttpClient"/> request timeout: the BCL surfaces a
    /// <see cref="TaskCanceledException"/> whose token is NOT the caller's.
    /// </summary>
    public static StubHttpMessageHandler ThrowsTimeout() =>
        new((_, _) => throw new TaskCanceledException("The request timed out."));

    public static StubHttpMessageHandler ThrowsHttpRequestException() =>
        new((_, _) => throw new HttpRequestException("Name or service not known."));

    /// <summary>Honors real caller cancellation so the provider can rethrow it.</summary>
    public static StubHttpMessageHandler ThrowsOnCancellation() =>
        new((_, token) =>
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(string.Empty),
            });
        });

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        LastRequestUri = request.RequestUri;
        return _responder(request, cancellationToken);
    }
}
