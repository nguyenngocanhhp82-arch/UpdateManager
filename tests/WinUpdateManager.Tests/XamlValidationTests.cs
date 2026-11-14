using System.Xml;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace WinUpdateManager.Tests;

public class XamlValidationTests
{
    private static readonly HashSet<string> SingleChildElementNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "ScrollViewer",
        "Border",
        "UserControl",
        "Window",
        "FluentWindow",
        "Card",
        "Viewbox",
        "Frame",
        "Popup",
        "ContentControl"
    };

    [Fact]
    public void AllXamlFiles_MustBeValidXml_AndObeySingleChildContentControlRules()
    {
        // 1. Locate repository root and src/WinUpdateManager.App
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WinUpdateManager.sln")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("Repository root containing WinUpdateManager.sln must be found");

        var appDir = Path.Combine(dir!.FullName, "src", "WinUpdateManager.App");
        Directory.Exists(appDir).Should().BeTrue($"{appDir} directory must exist");

        var xamlFiles = Directory.GetFiles(appDir, "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToArray();

        xamlFiles.Should().NotBeEmpty("At least one XAML file should be present in WinUpdateManager.App");

        var csprojPath = Path.Combine(appDir, "WinUpdateManager.App.csproj");
        var csprojContent = File.ReadAllText(csprojPath);
        var violations = new List<string>();

        // Verify each XAML file is declared in csproj
        foreach (var file in xamlFiles)
        {
            var relativeToProj = Path.GetRelativePath(appDir, file).Replace('/', '\\');
            if (!csprojContent.Contains(relativeToProj))
            {
                violations.Add($"[Missing from csproj] {relativeToProj} is not declared in WinUpdateManager.App.csproj");
            }
        }

        // 2. Validate XML syntax and single-child constraints
        foreach (var file in xamlFiles)
        {
            var relativePath = Path.GetRelativePath(dir.FullName, file);
            XDocument doc;
            try
            {
                doc = XDocument.Load(file, LoadOptions.SetLineInfo);
            }
            catch (Exception ex)
            {
                violations.Add($"[XML Syntax Error] {relativePath}: {ex.Message}");
                continue;
            }

            foreach (var elem in doc.Descendants())
            {
                var localName = elem.Name.LocalName;
                var lineInfo = (IXmlLineInfo)elem;
                var line = lineInfo.HasLineInfo() ? lineInfo.LineNumber : 0;

                if (SingleChildElementNames.Contains(localName))
                {
                    // Exclude property elements like <Window.Resources> or <Border.Style>
                    var contentChildren = elem.Elements()
                        .Where(e => !e.Name.LocalName.Contains('.'))
                        .ToList();

                    if (contentChildren.Count > 1)
                    {
                        var childrenNames = string.Join(", ", contentChildren.Select(c => $"<{c.Name.LocalName}>"));
                        violations.Add($"[WPF MC3089 Error] {relativePath}(line {line}): '{localName}' has {contentChildren.Count} direct content children ({childrenNames}), but can accept only ONE child.");
                    }
                }
            }
        }

        violations.Should().BeEmpty(
            "All XAML files must be valid XML and obey WPF single-child ContentControl constraints.\n" +
            string.Join("\n", violations));
    }
}
