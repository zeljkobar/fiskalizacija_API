using System.Net;
using System.Text;

internal sealed class FakePuHttpMessageHandler(
    string responseXml,
    HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(responseXml, Encoding.UTF8, "text/xml"),
            RequestMessage = request
        };

        return Task.FromResult(response);
    }
}

internal sealed class FailingPuHttpMessageHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        throw new HttpRequestException("Simulirana nedostupnost PU servisa.");
}
