namespace ElectricityExplorer.Core.Nem12;

public sealed class Nem12ParseException : Exception
{
    public Nem12ParseException(int lineNumber, string message)
        : base($"Line {lineNumber}: {message}")
    {
        LineNumber = lineNumber;
    }

    public Nem12ParseException(int lineNumber, string message, Exception innerException)
        : base($"Line {lineNumber}: {message}", innerException)
    {
        LineNumber = lineNumber;
    }

    public int LineNumber { get; }
}
