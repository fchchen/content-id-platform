namespace ContentId.Api.Infrastructure;

public sealed class NoopStorageInitializer : IStorageInitializer
{
    public Task InitializeAsync() => Task.CompletedTask;
}
