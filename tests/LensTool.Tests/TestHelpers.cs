using System.Net;
using System.Net.Http;
using System.Text;

namespace LensTool.Tests;

/// <summary>Records the request and returns a canned response (or throws).</summary>
internal sealed class FakeHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _status;
    private readonly string _response = "";
    private readonly Exception? _error;

    public FakeHandler(HttpStatusCode status, string response) => (_status, _response) = (status, response);

    public FakeHandler(Exception error) => _error = error;

    public HttpRequestMessage? Request { get; private set; }
    public string? Body { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Request = request;
        Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        if (_error is not null)
            throw _error;
        return new HttpResponseMessage(_status) { Content = new StringContent(_response, Encoding.UTF8, "application/json") };
    }
}

/// <summary>A fact that is skipped unless the given environment variable is set.</summary>
public sealed class EnvironmentFactAttribute : FactAttribute
{
    public EnvironmentFactAttribute(string variable)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable))
            && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable, EnvironmentVariableTarget.User)))
            Skip = $"{variable} is not set";
    }
}
