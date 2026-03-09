namespace CodeCompress.Core.Validation;

public sealed class PathValidatorService : IPathValidator
{
    public string ValidatePath(string inputPath, string projectRoot) =>
        PathValidator.ValidatePath(inputPath, projectRoot);

    public string ValidateRelativePath(string relativePath, string projectRoot) =>
        PathValidator.ValidateRelativePath(relativePath, projectRoot);

    public bool IsWithinRoot(string candidatePath, string projectRoot) =>
        PathValidator.IsWithinRoot(candidatePath, projectRoot);
}
