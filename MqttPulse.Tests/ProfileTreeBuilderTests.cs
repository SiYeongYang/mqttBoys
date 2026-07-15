using MqttPulse.App.Models;
using MqttPulse.App.ViewModels;

namespace MqttPulse.Tests;

[TestClass]
public sealed class ProfileTreeBuilderTests
{
    [TestMethod]
    public void BuildGroupsProfilesUnderNestedFoldersAndKeepsEmptyFolders()
    {
        var profiles = new[]
        {
            Profile("MQTT 입력", "현장A/라인1"),
            Profile("MQTT 출력", "현장A/라인2"),
            Profile("로컬", string.Empty)
        };

        var roots = ProfileTreeBuilder.Build(profiles, new[] { "현장B/대기" });

        var local = roots.Single(x => x.Profile?.Name == "로컬");
        var siteA = roots.Single(x => x.Name == "현장A");
        var line1Profile = siteA.Children.Single(x => x.Name == "라인1").Children.Single();
        var emptyFolder = roots.Single(x => x.Name == "현장B").Children.Single(x => x.Name == "대기");

        Assert.IsEmpty(local.FullPath);
        Assert.AreEqual("MQTT 입력", line1Profile.Profile!.Name);
        Assert.IsTrue(emptyFolder.IsFolder);
        Assert.IsEmpty(emptyFolder.Children);
    }

    [TestMethod]
    public void NormalizeFolderPathTrimsSeparatorsAndBackslashes()
    {
        var normalized = ProfileTreeBuilder.NormalizeFolderPath(@" / 현장A \ 라인1 / ");

        Assert.AreEqual("현장A/라인1", normalized);
    }

    private static BrokerProfile Profile(string name, string folderPath)
    {
        return new BrokerProfile
        {
            Name = name,
            FolderPath = folderPath,
            Host = "127.0.0.1"
        };
    }
}
