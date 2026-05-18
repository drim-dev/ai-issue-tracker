using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Api.Tests.Harnesses;

/// <summary>Creates HTTP clients bound to the in-memory test server.</summary>
public class HttpClientHarness<TProgram> : IHarness<TProgram>
    where TProgram : class
{
    private WebApplicationFactory<TProgram>? _factory;

    public void ConfigureWebHostBuilder(IWebHostBuilder builder)
    {
    }

    public Task Start(WebApplicationFactory<TProgram> factory, CancellationToken cancellationToken)
    {
        _factory = factory;
        return Task.CompletedTask;
    }

    public Task Stop(CancellationToken cancellationToken) => Task.CompletedTask;

    public HttpClient CreateClient()
    {
        if (_factory is null)
        {
            throw new InvalidOperationException(
                $"HttpClient harness is not started. Call {nameof(Start)} first.");
        }

        return _factory.CreateClient();
    }
}
