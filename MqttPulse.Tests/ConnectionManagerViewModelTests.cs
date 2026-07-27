using MqttPulse.App.Models;
using MqttPulse.App.Services;
using MqttPulse.App.ViewModels;
using System.IO;
using System.Windows;

namespace MqttPulse.Tests;

[TestClass]
public sealed class ConnectionManagerViewModelTests
{
    [TestMethod]
    public void MultipleProfilesRemainInSameFolderAndSelectedBrokerShowsFolder()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mqttboys-{Guid.NewGuid():N}", "profiles.json");
        try
        {
            var store = new ProfileStore(path);
            store.Save(new[]
            {
                Profile("Broker A", string.Empty, 1883),
                Profile("Broker B", string.Empty, 11883)
            }, new[] { "Factory" });

            using var viewModel = new MainViewModel(store);
            var brokerA = viewModel.Profiles.Single(x => x.Name == "Broker A");
            var brokerB = viewModel.Profiles.Single(x => x.Name == "Broker B");

            viewModel.SelectedProfile = brokerA;
            viewModel.SelectedProfileFolderPath = "Factory";
            viewModel.SaveProfilesCommand.Execute(null);

            viewModel.SelectedProfile = brokerB;
            viewModel.SelectedProfileFolderPath = "Factory";
            viewModel.SaveProfilesCommand.Execute(null);

            var saved = store.LoadLibrary().Profiles;
            Assert.AreEqual("Factory", saved.Single(x => x.Name == "Broker A").FolderPath);
            Assert.AreEqual("Factory", saved.Single(x => x.Name == "Broker B").FolderPath);

            viewModel.SelectedProfile = viewModel.Profiles.Single(x => x.Name == "Broker B");
            Assert.AreEqual("Factory", viewModel.SelectedProfileFolderPath);

            var folder = ProfileTreeBuilder.Build(saved, store.LoadLibrary().FolderPaths).Single(x => x.Name == "Factory");
            Assert.AreEqual(2, folder.Children.Count(x => x.Profile is not null));
        }
        finally
        {
            var root = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [TestMethod]
    public void SelectingFolderSwitchesRightPaneToFolderEditor()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mqttboys-{Guid.NewGuid():N}", "profiles.json");
        try
        {
            var store = new ProfileStore(path);
            store.Save(new[] { Profile("Broker A", "Factory", 1883) }, new[] { "Factory" });

            using var viewModel = new MainViewModel(store);
            var folder = viewModel.ProfileTree.Single(x => x.Name == "Factory");

            viewModel.SelectedProfileNode = folder;

            Assert.AreEqual(Visibility.Visible, viewModel.FolderEditorVisibility);
            Assert.AreEqual(Visibility.Collapsed, viewModel.BrokerEditorVisibility);
            Assert.AreEqual("Factory", viewModel.SelectedFolderName);
            Assert.AreEqual("Factory", viewModel.SelectedFolderFullPath);
        }
        finally
        {
            var root = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }


    [TestMethod]
    public void MoveProfileNodeMovesBrokerIntoTargetFolder()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mqttboys-{Guid.NewGuid():N}", "profiles.json");
        try
        {
            var store = new ProfileStore(path);
            store.Save(new[]
            {
                Profile("Broker A", string.Empty, 1883),
                Profile("Broker B", string.Empty, 11883)
            }, new[] { "Factory" });

            using var viewModel = new MainViewModel(store);
            var broker = viewModel.ProfileTree.Single(x => x.Profile?.Name == "Broker B");
            var folder = viewModel.ProfileTree.Single(x => x.Name == "Factory");

            viewModel.MoveProfileNode(broker, folder);

            var saved = store.LoadLibrary().Profiles;
            Assert.AreEqual("Factory", saved.Single(x => x.Name == "Broker B").FolderPath);
            Assert.AreEqual("Factory", viewModel.Profiles.Single(x => x.Name == "Broker B").FolderPath);
        }
        finally
        {
            var root = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }    private static BrokerProfile Profile(string name, string folderPath, int port)
    {
        return new BrokerProfile
        {
            Name = name,
            FolderPath = folderPath,
            Host = "192.0.2.10",
            Port = port
        };
    }
}
