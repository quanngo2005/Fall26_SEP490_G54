using System.Net.Http.Json;
using G54.BLL.Common.Exceptions;
using Microsoft.Extensions.Configuration;

namespace G54.BLL.Services;

internal sealed class QueueConnection(IHttpClientFactory factory, IConfiguration config)
{
    internal async Task<T> RequestAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        if (!Uri.TryCreate(config["Queue:Origin"], UriKind.Absolute, out var origin) ||
            string.IsNullOrWhiteSpace(config["Queue:ServiceKey"]))
            throw Unavailable();
        using var client = factory.CreateClient("G54Queue");
        using var request = new HttpRequestMessage(method, new Uri(origin, path));
        request.Headers.Add("X-Queue-Key", config["Queue:ServiceKey"]);
        if (body != null) request.Content = JsonContent.Create(body);
        try
        {
            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                if (status is not (400 or 404 or 409)) throw Unavailable();
                var error = await response.Content.ReadFromJsonAsync<RemoteError>(ct);
                throw new BusinessRuleException(error?.ErrorCode ?? "QUEUE_ERROR", statusCode: status);
            }
            return await response.Content.ReadFromJsonAsync<T>(ct) ?? throw Unavailable();
        }
        catch (HttpRequestException) { throw Unavailable(); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw Unavailable(); }
    }

    private static BusinessRuleException Unavailable() => new("QUEUE_UNAVAILABLE", statusCode: 503);
    private sealed record RemoteError(string ErrorCode);
}
