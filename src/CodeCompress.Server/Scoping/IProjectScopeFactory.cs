namespace CodeCompress.Server.Scoping;

internal interface IProjectScopeFactory
{
    internal Task<IProjectScope> CreateAsync(string projectRoot, CancellationToken cancellationToken = default);
}
