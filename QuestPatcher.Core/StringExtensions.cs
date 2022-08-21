using System.Text.RegularExpressions;

namespace QuestPatcher.Core;

public static class StringExtensions
{
    /// <summary>
    /// Escapes an arbitrary string as an argument to a bash command.
    /// </summary>
    /// <param name="arg">The string to escape</param>
    /// <returns>The escaped string</returns>
    public static string EscapeBash(this string arg)
    {
        return $"'{arg.Replace("'", "'\\''")}'";
    }

    /// <summary>
    /// Escapes an arbitrary string as an argument for <see cref="System.Diagnostics.Process.Start"/>.
    /// </summary>
    /// <param name="arg">The string to escape</param>
    /// <returns>The escaped string</returns>
    public static string EscapeProc(this string arg)
    {
        return $"\"{Regex.Replace(arg, @"(\\+)$", @"$1$1")}\"";
    }
    
    /// <summary>
    /// Replaces all of the backslashes in <paramref name="arg"/> with forward slashes.
    /// </summary>
    /// <param name="arg">The string to convert to using forward slashes</param>
    /// <returns>The string with the backslashes replaced with forward slashes</returns>
    public static string WithForwardSlashes(this string arg)
    {
        return arg.Replace('\\', '/');
    }
}
