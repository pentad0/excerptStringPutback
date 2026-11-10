namespace ExcerptStringPutback;

public sealed class ToolException : Exception
{
    public ToolException(string message)
        : base(message)
    {
    }
}

public sealed class UsageException : Exception
{
    public UsageException(string message)
        : base(message)
    {
    }
}
