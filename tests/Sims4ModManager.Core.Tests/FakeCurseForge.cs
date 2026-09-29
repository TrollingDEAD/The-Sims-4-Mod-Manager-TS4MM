using System.Net;
using System.Net.Http;
using System.Text;

namespace Sims4ModManager.Core.Tests;

/// <summary>Scripted HTTP handler for CurseForgeClient tests - avoids any real network call.</summary>
internal sealed class FakeCurseForge : HttpMessageHandler
{
    public readonly List<string> Requests = new();
    public Func<HttpRequestMessage, string> Respond { get; set; } = _ => "{}";
    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add($"{request.Method} {request.RequestUri!.PathAndQuery} key={string.Join(",", request.Headers.GetValues("x-api-key"))}");
        return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Respond(request), Encoding.UTF8, "application/json") });
    }
}
