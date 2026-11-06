using Builder;

namespace Revare.Tests.BuildSystem;

/// <summary>
/// config.toml refuses a section or key it does not read, so a misspelled key (<c>module-paths</c> for
/// <c>module_paths</c>) never passes without a word.
/// </summary>
public sealed class ManifestUnknownKeyTests
{
    private static string Load(string target)
    {
        string root = Path.Combine(path1: Path.GetTempPath(), path2: "rv-manifest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path: root);
        try
        {
            File.WriteAllText(path: Path.Combine(path1: root, path2: "config.toml"),
                contents: "[package]\nname = \"test\"\n\n[target]\nmode = \"debug\"\n" + target);
            ProjectManifest manifest = ManifestLoader.Load(tomlPath: Path.Combine(path1: root, path2: "config.toml"),
                resolveExecutable: false);
            return manifest.Target.Mode;
        }
        finally
        {
            Directory.Delete(path: root, recursive: true);
        }
    }

    /// <summary>The hyphenated spelling of an underscore key is an error that names the key meant.</summary>
    [Theory]
    [InlineData("module-paths = []", "module_paths")]
    [InlineData("c-libraries = []", "c_libraries")]
    [InlineData("library-paths = []", "library_paths")]
    [InlineData("use_daemon = true", "use-daemon")]
    public void WrongSeparator_NamesTheKeyMeant(string line, string meant)
    {
        var error = Assert.Throws<InvalidOperationException>(() => Load(target: line));
        Assert.Contains(expectedSubstring: $"Did you mean {meant}?", actualString: error.Message);
    }

    /// <summary>A key nothing reads is refused, close to a known one or not.</summary>
    [Fact]
    public void UnknownKey_IsRefused()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Load(target: "colour = \"red\""));
        Assert.Contains(expectedSubstring: "[target] has no key named colour", actualString: error.Message);
    }

    /// <summary>The keys the loader reads still load.</summary>
    [Fact]
    public void KnownKeys_Load()
    {
        Assert.Equal(expected: "debug", actual: Load(target: "use-daemon = false\nincremental = false\n"));
    }
}
