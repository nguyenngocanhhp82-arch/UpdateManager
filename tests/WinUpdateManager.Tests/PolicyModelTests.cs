using WinUpdateManager.Core.Models;
using Xunit;

namespace WinUpdateManager.Tests;

public class PolicyModelTests
{
    [Fact]
    public void UpdatePolicyModel_DefaultValues_AreSensible()
    {
        var model = new UpdatePolicyModel();

        Assert.Equal(AuOptionMode.CompletelyDisabled, model.AuMode);
        Assert.False(model.DeferQualityUpdates);
        Assert.Equal(7, model.DeferQualityDays);
        Assert.False(model.DeferFeatureUpdates);
        Assert.Equal(30, model.DeferFeatureDays);
        Assert.False(model.TargetReleaseVersionEnabled);
        Assert.Equal("Windows 11", model.ProductVersion);
        Assert.Equal("23H2", model.TargetReleaseVersionInfo);
        Assert.True(model.ExcludeDriversInQualityUpdate);
        Assert.True(model.NoAutoRebootWithLoggedOnUsers);
        Assert.True(model.ActiveHoursEnabled);
        Assert.Equal(8, model.ActiveHoursStart);
        Assert.Equal(18, model.ActiveHoursEnd);
        Assert.False(model.DisableStoreAutoDownload);
        Assert.False(model.IsWuauservDisabled);
    }
}
