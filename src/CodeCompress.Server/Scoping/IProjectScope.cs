using CodeCompress.Core.Indexing;
using CodeCompress.Core.Storage;

namespace CodeCompress.Server.Scoping;

internal interface IProjectScope : IAsyncDisposable
{
    internal string RepoId { get; }
    internal ISymbolStore Store { get; }
    internal IIndexEngine Engine { get; }
}
