namespace CodeCompress.Core.Registry;

public interface IRegistryService
{
    public Task<IReadOnlyList<RepositoryRecord>> ListAsync();
    public Task UpdateStatsAsync(string projectRoot, int fileCount, int symbolCount, DateTimeOffset lastIndexed);
    public Task DeregisterAsync(string projectRoot);
}
