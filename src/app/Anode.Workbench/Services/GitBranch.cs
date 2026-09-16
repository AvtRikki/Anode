namespace Anode.Workbench.Services;

/// <summary>
/// Which branch the project sits on, read straight from <c>.git</c>. No process is started and no library is taken
/// on: the name is in <c>HEAD</c>, one line of text, and a workbench that shows it must never be the reason a board
/// fails to open — anything unexpected simply means "no branch to show".
/// </summary>
public static class GitBranch
{
    private const string RefPrefix = "ref: refs/heads/";

    /// <summary>Branch of the repository containing <paramref name="directory"/>, or null when there is none.</summary>
    public static string? Of(string directory)
    {
        try
        {
            for (var dir = new DirectoryInfo(directory); dir is not null; dir = dir.Parent)
            {
                if (HeadFile(Path.Combine(dir.FullName, ".git")) is { } head && File.Exists(head))
                {
                    return Parse(File.ReadAllText(head));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // A repository we cannot read is the same as no repository.
        }

        return null;
    }

    /// <summary>The path of HEAD: <c>.git</c> is a directory in a clone and a file pointing elsewhere in a worktree.</summary>
    private static string? HeadFile(string dotGit)
    {
        if (Directory.Exists(dotGit))
        {
            return Path.Combine(dotGit, "HEAD");
        }

        if (!File.Exists(dotGit))
        {
            return null;
        }

        string line = File.ReadAllText(dotGit).Trim();
        return line.StartsWith("gitdir:", StringComparison.Ordinal)
            ? Path.Combine(line["gitdir:".Length..].Trim(), "HEAD")
            : null;
    }

    /// <summary>A branch reads as "ref: refs/heads/name"; a detached head is a bare commit, shown short.</summary>
    internal static string? Parse(string head)
    {
        string text = head.Trim();
        if (text.StartsWith(RefPrefix, StringComparison.Ordinal))
        {
            return text[RefPrefix.Length..] is { Length: > 0 } name ? name : null;
        }

        return text.Length >= 7 && text.All(Uri.IsHexDigit) ? text[..7] : null;
    }
}
