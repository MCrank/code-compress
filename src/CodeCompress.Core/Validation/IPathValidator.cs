namespace CodeCompress.Core.Validation;

public interface IPathValidator
{
    public string ValidatePath(string inputPath, string projectRoot);
    public string ValidateRelativePath(string relativePath, string projectRoot);
    public bool IsWithinRoot(string candidatePath, string projectRoot);
}
