using FluentAssertions;
using WinUpdateManager.Core.Models;
using WinUpdateManager.Core.Services;
using Xunit;

namespace WinUpdateManager.Tests;

public class WingetParserTests
{
    [Fact]
    public void ParseTable_WithStandardUpgradeOutput_ReturnsPackages()
    {
        string sampleOutput = """
Name                              Id                     Version          Available        Source
-------------------------------------------------------------------------------------------------
WhatsApp                          9NKSQGP7F2NH           2.2405.3.0       2.2410.1.0       msstore
Google Chrome                     Google.Chrome          120.0.6099.130   121.0.6167.85    winget
Visual Studio Code                Microsoft.VisualStudio 1.85.0           1.86.0           winget
3 upgrades available.
""";

        var packages = WingetParser.ParseTable(sampleOutput, hasAvailableColumn: true);

        packages.Should().HaveCount(3);

        packages[0].Name.Should().Be("WhatsApp");
        packages[0].Id.Should().Be("9NKSQGP7F2NH");
        packages[0].CurrentVersion.Should().Be("2.2405.3.0");
        packages[0].AvailableVersion.Should().Be("2.2410.1.0");
        packages[0].Source.Should().Be("msstore");
        packages[0].HasUpdate.Should().BeTrue();

        packages[1].Name.Should().Be("Google Chrome");
        packages[1].Id.Should().Be("Google.Chrome");
        packages[1].CurrentVersion.Should().Be("120.0.6099.130");
        packages[1].AvailableVersion.Should().Be("121.0.6167.85");
        packages[1].Source.Should().Be("winget");
    }

    [Fact]
    public void ParseTable_WithSpacedDashSeparator_ReturnsPackages()
    {
        string sampleOutput = """
Name                           Id                      Version         Available       Source
-----------------------------  ----------------------  --------------  --------------  ------
WhatsApp                       9NKSQGP7F2NH            2.2405.3.0      2.2410.1.0      msstore
VNG Zalo                       VNG.Zalo                24.1.1          24.2.1          winget
2 upgrades available.
""";

        var packages = WingetParser.ParseTable(sampleOutput, hasAvailableColumn: true);

        packages.Should().HaveCount(2);
        packages[0].Name.Should().Be("WhatsApp");
        packages[0].Id.Should().Be("9NKSQGP7F2NH");
        packages[1].Name.Should().Be("VNG Zalo");
        packages[1].Id.Should().Be("VNG.Zalo");
    }

    [Fact]
    public void ParseTable_WithUnicodeAndSpecialCharacters_ReturnsPackages()
    {
        string sampleOutput = """
Name                              Id                     Version          Available        Source
-------------------------------------------------------------------------------------------------
Trình duyệt Cốc Cốc               CocCoc.CocCoc          118.0.120        119.0.124        winget
Unikey Bàn phím Tiếng Việt        PhamKimLong.UniKey     4.3.RC5          4.3.RC6          winget
2 upgrades available.
""";

        var packages = WingetParser.ParseTable(sampleOutput, hasAvailableColumn: true);

        packages.Should().HaveCount(2);
        packages[0].Name.Should().Be("Trình duyệt Cốc Cốc");
        packages[0].Id.Should().Be("CocCoc.CocCoc");
        packages[1].Name.Should().Be("Unikey Bàn phím Tiếng Việt");
        packages[1].Id.Should().Be("PhamKimLong.UniKey");
    }

    [Fact]
    public void ParseTable_WithCarriageReturnsAndSpinners_CleansCorrectly()
    {
        string sampleOutput = "-\rName                              Id                     Version          Available        Source\r\n" +
                              "-------------------------------------------------------------------------------------------------\r\n" +
                              "WhatsApp                          9NKSQGP7F2NH           2.2405.3.0       2.2410.1.0       msstore\r\n" +
                              "1 upgrade available.\r\n";

        var packages = WingetParser.ParseTable(sampleOutput, hasAvailableColumn: true);

        packages.Should().HaveCount(1);
        packages[0].Name.Should().Be("WhatsApp");
        packages[0].Id.Should().Be("9NKSQGP7F2NH");
    }

    [Fact]
    public void ParseTable_ForListOutput_WithoutAvailableColumn_ReturnsInstalledApps()
    {
        string sampleOutput = """
Name                              Id                     Version          Source
--------------------------------------------------------------------------------
WhatsApp                          9NKSQGP7F2NH           2.2405.3.0       msstore
Notepad++                         Notepad++.Notepad++    8.6              winget
Git                               Git.Git                2.43.0           winget
""";

        var packages = WingetParser.ParseTable(sampleOutput, hasAvailableColumn: false);

        packages.Should().HaveCount(3);
        packages[0].Name.Should().Be("WhatsApp");
        packages[0].Id.Should().Be("9NKSQGP7F2NH");
        packages[0].CurrentVersion.Should().Be("2.2405.3.0");
        packages[0].Source.Should().Be("msstore");
        packages[0].AvailableVersion.Should().BeEmpty();

        packages[1].Name.Should().Be("Notepad++");
        packages[1].Id.Should().Be("Notepad++.Notepad++");
    }

    [Fact]
    public void ParsePinnedIds_ReturnsPinnedPackageIds()
    {
        string sampleOutput = """
Name                              Id                     Version          Source
--------------------------------------------------------------------------------
Google Chrome                     Google.Chrome          120.0.6099.130   winget
Notepad++                         Notepad++.Notepad++    8.6              winget
""";

        var pinned = WingetParser.ParsePinnedIds(sampleOutput);

        pinned.Should().Contain("Google.Chrome");
        pinned.Should().Contain("Notepad++.Notepad++");
        pinned.Should().NotContain("9NKSQGP7F2NH");
    }

    [Fact]
    public void ParseVersions_ReturnsListOfAvailableVersions()
    {
        string sampleOutput = """
Found WhatsApp [9NKSQGP7F2NH]
Version
--------
2.2410.1.0
2.2405.3.0
2.2402.1.0
""";

        var versions = WingetParser.ParseVersions(sampleOutput);

        versions.Should().HaveCount(3);
        versions[0].Should().Be("2.2410.1.0");
        versions[1].Should().Be("2.2405.3.0");
        versions[2].Should().Be("2.2402.1.0");
    }

    [Fact]
    public void ParseTable_WithUnknownVersionInStorePackage_ParsesCorrectly()
    {
        string sampleOutput = """
Name                              Id                     Version          Available        Source
-------------------------------------------------------------------------------------------------
WhatsApp                          9NKSQGP7F2NH           <unknown>        2.2410.1.0       msstore
Microsoft Teams                   Microsoft.Teams        <unknown>        1.6.00           winget
2 upgrades available.
""";

        var packages = WingetParser.ParseTable(sampleOutput, hasAvailableColumn: true);

        packages.Should().HaveCount(2);
        packages[0].Name.Should().Be("WhatsApp");
        packages[0].Id.Should().Be("9NKSQGP7F2NH");
        packages[0].CurrentVersion.Should().Be("<unknown>");
        packages[0].AvailableVersion.Should().Be("2.2410.1.0");
        packages[0].Source.Should().Be("msstore");

        packages[1].Name.Should().Be("Microsoft Teams");
        packages[1].Id.Should().Be("Microsoft.Teams");
        packages[1].CurrentVersion.Should().Be("<unknown>");
        packages[1].AvailableVersion.Should().Be("1.6.00");
    }

    [Theory]
    [InlineData(0, "Thành công.")]
    [InlineData(-1, "Tiến trình đã bị người dùng hủy.")]
    [InlineData(3010, "Cập nhật thành công, cần khởi động lại máy tính.")]
    [InlineData(1618, "Một tiến trình cài đặt khác đang chạy. Vui lòng đợi tiến trình đó kết thúc rồi thử lại.")]
    [InlineData(unchecked((int)0x80190194), "404")]
    [InlineData(unchecked((int)0x80190190), "400")]
    [InlineData(87, "Mã lỗi 87")]
    [InlineData(unchecked((int)0x80070057), "Mã lỗi 87")]
    [InlineData(unchecked((int)0x8A150056), "tài khoản Microsoft")]
    [InlineData(unchecked((int)0x8A150044), "Tải gói cài đặt thất bại")]
    public void WingetExitCode_MapsCodesToVietnamese(int code, string expectedSubstring)
    {
        string desc = WingetExitCode.GetDescription(code);
        desc.Should().Contain(expectedSubstring);
    }

    [Theory]
    [InlineData("Microsoft.Office", "Microsoft Office", true)]
    [InlineData("Microsoft.Office.2021", "Microsoft Office LTSC Professional Plus 2021", true)]
    [InlineData("Microsoft.365Apps", "Microsoft 365 Apps", true)]
    [InlineData("VNG.Zalo", "Zalo", true)]
    [InlineData("Zalo.Zalo", "Zalo PC", true)]
    [InlineData("Google.Chrome", "Google Chrome", false)]
    [InlineData("9NKSQGP7F2NH", "WhatsApp", false)]
    [InlineData("WhatsApp.WhatsApp", "WhatsApp", false)]
    public void IsExcludedPackage_DetectsSensitiveApps(string id, string name, bool expectedExcluded)
    {
        var config = new ConfigService();
        var runner = new ProcessRunner(new LogService());
        var wingetService = new WingetService(runner, new LogService(), config);

        bool isExcluded = wingetService.IsExcludedPackage(id, name);
        isExcluded.Should().Be(expectedExcluded);
    }
}
