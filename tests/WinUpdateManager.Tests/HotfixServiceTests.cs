using FluentAssertions;
using WinUpdateManager.Core.Services;
using Xunit;

namespace WinUpdateManager.Tests;

public class HotfixServiceTests
{
    [Fact]
    public void ParseDismPackagesTable_ReturnsListOfPackages()
    {
        string sampleOutput = """
Deployment Image Servicing and Management tool
Version: 10.0.22621.1

Package Identity                                                            | State      | Release Type    | Install Time
---------------------------------------------------------------------------- | ---------- | --------------- | -------------------
Package_for_DotNetRollup~31bf3856ad364e35~amd64~~10.0.9023.1                 | Installed  | Security Update | 1/15/2024 10:20 AM
Package_for_RollupFix~31bf3856ad364e35~amd64~~22621.3007.1.4                | Installed  | Update          | 1/12/2024 8:45 AM
The operation completed successfully.
""";

        var packages = HotfixService.ParseDismPackagesTable(sampleOutput);

        packages.Should().HaveCount(2);

        packages[0].PackageName.Should().Contain("Package_for_DotNetRollup");
        packages[0].State.Should().Be("Installed");
        packages[0].ReleaseType.Should().Be("Security Update");

        packages[1].PackageName.Should().Contain("Package_for_RollupFix");
        packages[1].State.Should().Be("Installed");
        packages[1].IsRollupFix.Should().BeTrue();
    }

    [Fact]
    public void ParseDismPackagesTable_RealDismOutput_WithoutPipes_ReturnsListOfPackages()
    {
        string sampleOutput = """
Deployment Image Servicing and Management tool
Version: 10.0.22621.1

Image Version: 10.0.22626.3007

Packages corresponding to the image :

Package Identity                                                                                             State             Release Type       Install Time          
------------------------------------------------------------------------------------------------------------ ----------------- ------------------ -----------------     
Package_for_DotNetRollup~31bf3856ad364e35~amd64~~10.0.9023.1                                                Installed         Security Update    1/15/2024 10:20 AM
Package_for_RollupFix~31bf3856ad364e35~amd64~~22621.3007.1.4                                                Installed         Update             1/12/2024 8:45 AM     
The operation completed successfully.
""";

        var packages = HotfixService.ParseDismPackagesTable(sampleOutput);

        packages.Should().HaveCount(2);
        packages[0].PackageName.Should().Be("Package_for_DotNetRollup~31bf3856ad364e35~amd64~~10.0.9023.1");
        packages[0].State.Should().Be("Installed");
        packages[0].ReleaseType.Should().Be("Security Update");

        packages[1].PackageName.Should().Be("Package_for_RollupFix~31bf3856ad364e35~amd64~~22621.3007.1.4");
        packages[1].State.Should().Be("Installed");
        packages[1].IsRollupFix.Should().BeTrue();
    }
}
