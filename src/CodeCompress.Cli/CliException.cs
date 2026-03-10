namespace CodeCompress.Cli;

#pragma warning disable CA1515 // S3871 requires exceptions to be public; CA1515 conflicts in Exe projects
public sealed class CliException : Exception
#pragma warning restore CA1515
{
    public CliException() : base()
    {
    }

    public CliException(string message) : base(message)
    {
    }

    public CliException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
