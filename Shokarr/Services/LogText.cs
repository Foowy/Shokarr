namespace Shokarr.Services;

public static class LogText
{
    /// <summary>Strips line breaks so remote-supplied text can't forge extra log lines.</summary>
    public static string ForLog(this string text) => text.Replace("\r", "").Replace("\n", " ");
}
