using System.Text.RegularExpressions;
using Xunit;

namespace Shokarr.Tests;

public class DashboardThemeTests
{
    private static readonly string[] Variables =
        ["--bg", "--bg-raised", "--bg-inset", "--ink", "--ink-dim", "--ink-faint", "--rule", "--amber", "--amber-dim", "--teal", "--red"];

    private static string Read(string file) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "dashboard", file));

    [Fact]
    public void EveryThemeInThePicker_DefinesTheFullColourSet()
    {
        var themes = Regex.Matches(Read("dashboard.html"), "<option value=\"(shoko-[a-z]+)\"").Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(["shoko-gray", "shoko-aniblackle", "shoko-neonglass"], themes);

        var css = Read("dashboard.css");
        foreach (var theme in themes)
        {
            var block = Regex.Match(css, "\\[data-theme=\"" + theme + "\"\\] \\{(.*?)\\n\\}", RegexOptions.Singleline);
            Assert.True(block.Success, $"no colour block for {theme}");
            foreach (var variable in Variables)
                Assert.Contains(variable + ":", block.Groups[1].Value);
        }
    }
}
