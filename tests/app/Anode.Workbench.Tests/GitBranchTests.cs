using Anode.Workbench.Services;

namespace Anode.Workbench.Tests;

/// <summary>Reading the branch out of <c>.git</c>: the shapes HEAD comes in, and the ways it can be absent.</summary>
public class GitBranchTests
{
    [Theory]
    [InlineData("ref: refs/heads/main\n", "main")]
    [InlineData("ref: refs/heads/feature/usb-c\n", "feature/usb-c")]
    [InlineData("  ref: refs/heads/main  ", "main")]
    [InlineData("3f2a1b7c9d4e5f60718293a4b5c6d7e8f9012345\n", "3f2a1b7")]
    [InlineData("ref: refs/heads/", null)]
    [InlineData("not a head", null)]
    [InlineData("", null)]
    public void Head_reads_as_a_branch_or_a_short_commit(string head, string? expected) =>
        Assert.Equal(expected, GitBranch.Parse(head));

    [Fact]
    public void A_project_inside_a_repository_reports_its_branch()
    {
        string root = Directory.CreateTempSubdirectory("anode-git-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, ".git"));
            File.WriteAllText(Path.Combine(root, ".git", "HEAD"), "ref: refs/heads/trunk\n");

            string nested = Path.Combine(root, "boards", "rev-d");
            Directory.CreateDirectory(nested);

            Assert.Equal("trunk", GitBranch.Of(root));

            // A board usually sits some folders below the repository root.
            Assert.Equal("trunk", GitBranch.Of(nested));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_worktree_points_at_its_own_head()
    {
        string root = Directory.CreateTempSubdirectory("anode-git-").FullName;
        try
        {
            string gitDir = Path.Combine(root, "store", "worktrees", "rev-d");
            Directory.CreateDirectory(gitDir);
            File.WriteAllText(Path.Combine(gitDir, "HEAD"), "ref: refs/heads/rev-d\n");

            string work = Path.Combine(root, "work");
            Directory.CreateDirectory(work);
            File.WriteAllText(Path.Combine(work, ".git"), $"gitdir: {gitDir}\n");

            Assert.Equal("rev-d", GitBranch.Of(work));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_folder_outside_any_repository_has_no_branch()
    {
        string root = Directory.CreateTempSubdirectory("anode-plain-").FullName;
        try
        {
            // The temp folder has no repository above it on any platform the tests run on.
            Assert.Null(GitBranch.Of(Path.Combine(root, "nowhere")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
