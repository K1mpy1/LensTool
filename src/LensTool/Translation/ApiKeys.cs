namespace LensTool.Translation;

/// <summary>API keys live in environment variables only (never in code or settings files).</summary>
public static class ApiKeys
{
    /// <summary>Reads the variable from the process environment, then from the user's persisted variables (setx).</summary>
    public static string? Read(string variable) =>
        NullIfBlank(Environment.GetEnvironmentVariable(variable))
        ?? NullIfBlank(Environment.GetEnvironmentVariable(variable, EnvironmentVariableTarget.User));

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
