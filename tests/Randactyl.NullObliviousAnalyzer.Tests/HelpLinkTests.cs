using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Randactyl.NullObliviousAnalyzer.Tests;

public class HelpLinkTests
{
    private const string HelpLinkBase = "https://github.com/Randactyl/Randactyl.NullObliviousAnalyzer#";

    [Test]
    public async Task Every_rule_links_to_its_heading_in_the_README()
    {
        var anchors = Regex
            .Matches(File.ReadAllText(FindReadme()), @"^#+ (.+)$", RegexOptions.Multiline)
            .Select(m => GitHubAnchor(m.Groups[1].Value.Trim()))
            .ToHashSet();

        foreach (var descriptor in new NullObliviousAnalyzer().SupportedDiagnostics)
        {
            await Assert.That(descriptor.HelpLinkUri).StartsWith(HelpLinkBase);
            var anchor = descriptor.HelpLinkUri.Substring(HelpLinkBase.Length);
            await Assert.That(anchor).StartsWith(descriptor.Id.ToLowerInvariant() + "-");
            await Assert.That(anchors.Contains(anchor)).IsTrue();
        }
    }

    // GitHub lowercases the heading, drops punctuation other than '-' and '_', and turns spaces into '-'.
    private static string GitHubAnchor(string heading) =>
        Regex.Replace(heading.ToLowerInvariant(), @"[^\p{L}\p{Nd}\s_-]", "").Replace(' ', '-');

    private static string FindReadme()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "README.md");
            if (File.Exists(path) && File.Exists(Path.Combine(dir.FullName, "Randactyl.NullObliviousAnalyzer.slnx")))
            {
                return path;
            }
        }

        throw new FileNotFoundException("README.md not found above " + AppContext.BaseDirectory);
    }
}
