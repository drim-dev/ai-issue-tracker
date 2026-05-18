namespace Api.Tests.Fixtures;

/// <summary>Convenience cancellation tokens for component tests.</summary>
public static class TestCancellation
{
    public static CancellationToken CreateCancellationToken(int timeoutSeconds = 30) =>
        new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds)).Token;
}
