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
    }

    [TestMethod]
    public void DragOrderPersistsAfterReload()
    {
        var path = TempProfilePath();
        try
        {
            var store = new ProfileStore(path);
            store.Save(new[]
            {
                Profile("Broker A", string.Empty, 1883),
                Profile("Broker B", string.Empty, 11883),
                Profile("Broker C", string.Empty, 21883)
            });

            using (var viewModel = new MainViewModel(store))
            {
                var source = viewModel.ProfileTree.Single(x => x.Profile?.Name == "Broker C");
                var target = viewModel.ProfileTree.Single(x => x.Profile?.Name == "Broker A");
                viewModel.MoveProfileNode(source, target, ProfileNodeDropPosition.Before);

                CollectionAssert.AreEqual(
                    new[] { "Broker C", "Broker A", "Broker B" },
                    RootBrokerNames(viewModel));
            }

            using var reloaded = new MainViewModel(store);
            CollectionAssert.AreEqual(
                new[] { "Broker C", "Broker A", "Broker B" },
                RootBrokerNames(reloaded));
            Assert.HasCount(3, store.LoadLibrary().TreeOrder);
        }
        finally
        {
            DeleteProfileDirectory(path);
        }
    }

    [TestMethod]
    public void MultipleBrokersCanBeDraggedIntoSameFolderAndStayThere()
    {
        var path = TempProfilePath();
        try
        {
            var store = new ProfileStore(path);
            store.Save(new[]
            {
                Profile("Broker A", string.Empty, 1883),
                Profile("Broker B", string.Empty, 11883),
                Profile("Broker C", string.Empty, 21883)
            }, new[] { "Factory" });

            using (var viewModel = new MainViewModel(store))
            {
                MoveBrokerIntoFolder(viewModel, "Broker B", "Factory");
                MoveBrokerIntoFolder(viewModel, "Broker C", "Factory");

                var folder = viewModel.ProfileTree.Single(x => x.Name == "Factory");
                CollectionAssert.AreEqual(
                    new[] { "Broker B", "Broker C" },
                    folder.Children.Select(x => x.Name).ToArray());
            }

            using var reloaded = new MainViewModel(store);
            var reloadedFolder = reloaded.ProfileTree.Single(x => x.Name == "Factory");
            CollectionAssert.AreEqual(
                new[] { "Broker B", "Broker C" },
                reloadedFolder.Children.Select(x => x.Name).ToArray());
            Assert.IsTrue(reloaded.Profiles
                .Where(x => x.Name is "Broker B" or "Broker C")
                .All(x => x.FolderPath == "Factory"));
        }
        finally
        {
            DeleteProfileDirectory(path);
        }
    }

    [TestMethod]
    public void FoldersCanBeReorderedAndNestedByDrag()
    {
        var path = TempProfilePath();
        try
        {
            var store = new ProfileStore(path);
            store.Save(
                new[] { Profile("Broker A", string.Empty, 1883) },
                new[] { "Folder A", "Folder B" });

            using (var viewModel = new MainViewModel(store))
            {
                var folderB = viewModel.ProfileTree.Single(x => x.Name == "Folder B");
                var folderA = viewModel.ProfileTree.Single(x => x.Name == "Folder A");
                viewModel.MoveProfileNode(folderB, folderA, ProfileNodeDropPosition.Before);

                CollectionAssert.AreEqual(
                    new[] { "Folder B", "Folder A", "Broker A" },
                    viewModel.ProfileTree.Select(x => x.Name).ToArray());
            }

            using (var reordered = new MainViewModel(store))
            {
                CollectionAssert.AreEqual(
                    new[] { "Folder B", "Folder A", "Broker A" },
                    reordered.ProfileTree.Select(x => x.Name).ToArray());

                var folderB = reordered.ProfileTree.Single(x => x.Name == "Folder B");
                var folderA = reordered.ProfileTree.Single(x => x.Name == "Folder A");
                reordered.MoveProfileNode(folderB, folderA, ProfileNodeDropPosition.Into);
            }

            using var nested = new MainViewModel(store);
            var parent = nested.ProfileTree.Single(x => x.Name == "Folder A");
            Assert.AreEqual("Folder A/Folder B", parent.Children.Single().FullPath);
        }
        finally
        {
            DeleteProfileDirectory(path);
        }
    }

    [TestMethod]
    public void SaveChangesRenamesSelectedFolderAndItsBrokerPaths()
    {
        var path = TempProfilePath();
        try
        {
            var store = new ProfileStore(path);
            store.Save(
                new[] { Profile("Broker A", "Factory", 1883) },
                new[] { "Factory" });

            using var viewModel = new MainViewModel(store);
            viewModel.SelectedProfileNode = viewModel.ProfileTree.Single(x => x.Name == "Factory");
            viewModel.SelectedFolderName = "Production";
            viewModel.SaveProfilesCommand.Execute(null);

            var library = store.LoadLibrary();
            Assert.AreEqual("Production", library.Profiles.Single().FolderPath);
            Assert.IsTrue(library.FolderPaths.Contains("Production"));
            Assert.IsNotNull(viewModel.ProfileTree.SingleOrDefault(x => x.Name == "Production"));
        }
        finally
        {
            DeleteProfileDirectory(path);
        }
    }

    [TestMethod]
    public void DeleteBrokerRequiresConfirmation()
    {
        var path = TempProfilePath();
        try
        {
            var store = new ProfileStore(path);
            store.Save(new[]
            {
                Profile("Broker A", string.Empty, 1883),
                Profile("Broker B", string.Empty, 11883)
            });
            var confirmation = new StubConfirmationService(false);
            using var viewModel = new MainViewModel(store, confirmation);
            viewModel.SelectedProfileNode = viewModel.ProfileTree.Single(x => x.Profile?.Name == "Broker B");

            viewModel.DeleteProfileCommand.Execute(null);

            Assert.HasCount(2, viewModel.Profiles);
            Assert.AreEqual(1, confirmation.CallCount);

            confirmation.Result = true;
            viewModel.DeleteProfileCommand.Execute(null);

            Assert.HasCount(1, viewModel.Profiles);
            Assert.AreEqual("Broker A", viewModel.Profiles.Single().Name);
            Assert.AreEqual(2, confirmation.CallCount);
        }
        finally
        {
            DeleteProfileDirectory(path);
        }
    }

    [TestMethod]
    public void DeleteEmptyFolderRequiresConfirmation()
    {
        var path = TempProfilePath();
        try
        {
            var store = new ProfileStore(path);
            store.Save(
                new[] { Profile("Broker A", string.Empty, 1883) },
                new[] { "Empty" });
            var confirmation = new StubConfirmationService(false);
            using var viewModel = new MainViewModel(store, confirmation);
            viewModel.SelectedProfileNode = viewModel.ProfileTree.Single(x => x.Name == "Empty");

            viewModel.DeleteFolderCommand.Execute(null);

            Assert.IsNotNull(viewModel.ProfileTree.SingleOrDefault(x => x.Name == "Empty"));
            Assert.AreEqual(1, confirmation.CallCount);

            confirmation.Result = true;
            viewModel.DeleteFolderCommand.Execute(null);

            Assert.IsNull(viewModel.ProfileTree.SingleOrDefault(x => x.Name == "Empty"));
            Assert.AreEqual(2, confirmation.CallCount);
        }
        finally
        {
            DeleteProfileDirectory(path);
        }
    }

    private static void MoveBrokerIntoFolder(
        MainViewModel viewModel,
        string brokerName,
        string folderName)
    {
        var source = FindNodes(viewModel.ProfileTree)
            .Single(x => x.Profile?.Name == brokerName);
        var target = FindNodes(viewModel.ProfileTree)
            .Single(x => x.IsFolder && x.Name == folderName);
        viewModel.MoveProfileNode(source, target, ProfileNodeDropPosition.Into);
    }

    private static IEnumerable<ProfileTreeNodeViewModel> FindNodes(
        IEnumerable<ProfileTreeNodeViewModel> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in FindNodes(node.Children))
            {
                yield return child;
            }
        }
    }

    private static string[] RootBrokerNames(MainViewModel viewModel)
    {
        return viewModel.ProfileTree
            .Where(x => !x.IsFolder)
            .Select(x => x.Name)
            .ToArray();
    }

    private static string TempProfilePath()
    {
        return Path.Combine(
            Path.GetTempPath(),
            $"mqttboys-{Guid.NewGuid():N}",
            "profiles.json");
    }

    private static void DeleteProfileDirectory(string path)
    {
        var root = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static BrokerProfile Profile(string name, string folderPath, int port)
    {
        return new BrokerProfile
        {
            Name = name,
            FolderPath = folderPath,
            Host = "192.0.2.10",
            Port = port
        };
    }

    private sealed class StubConfirmationService(bool result) : IConfirmationService
    {
        public bool Result { get; set; } = result;

        public int CallCount { get; private set; }

        public bool Confirm(string title, string message)
        {
            CallCount++;
            return Result;
        }
    }
}
