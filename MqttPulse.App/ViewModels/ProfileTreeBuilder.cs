using MqttPulse.App.Models;

namespace MqttPulse.App.ViewModels;

public static class ProfileTreeBuilder
{
    private const string FolderOrderPrefix = "folder:";
    private const string BrokerOrderPrefix = "broker:";

    public static IReadOnlyList<ProfileTreeNodeViewModel> Build(
        IEnumerable<BrokerProfile> profiles,
        IEnumerable<string> folderPaths,
        IEnumerable<string>? treeOrder = null)
    {
        var roots = new List<ProfileTreeNodeViewModel>();
        var foldersByPath = new Dictionary<string, ProfileTreeNodeViewModel>(StringComparer.OrdinalIgnoreCase);

        foreach (var folderPath in folderPaths.Select(NormalizeFolderPath).Where(x => x.Length > 0))
        {
            EnsureFolder(folderPath, roots, foldersByPath);
        }

        foreach (var profile in profiles)
        {
            var folderPath = NormalizeFolderPath(profile.FolderPath);
            profile.FolderPath = folderPath;

            var profileNode = new ProfileTreeNodeViewModel(profile.Name, folderPath, profile);
            if (folderPath.Length == 0)
            {
                roots.Add(profileNode);
                continue;
            }

            EnsureFolder(folderPath, roots, foldersByPath).Children.Add(profileNode);
        }

        var orderIndex = (treeOrder ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select((key, index) => new { Key = key, Index = index })
            .ToDictionary(x => x.Key, x => x.Index, StringComparer.OrdinalIgnoreCase);
        SortChildren(roots, orderIndex);
        return roots;
    }

    public static IReadOnlyList<string> CaptureOrder(IEnumerable<ProfileTreeNodeViewModel> nodes)
    {
        var order = new List<string>();
        CaptureOrder(nodes, order);
        return order;
    }

    public static string GetOrderKey(ProfileTreeNodeViewModel node)
    {
        return node.IsFolder
            ? GetFolderOrderKey(node.FullPath)
            : GetBrokerOrderKey(node.Profile!.Id);
    }

    public static string GetFolderOrderKey(string folderPath)
    {
        return FolderOrderPrefix + NormalizeFolderPath(folderPath);
    }

    public static string GetBrokerOrderKey(string profileId)
    {
        return BrokerOrderPrefix + profileId;
    }

    public static bool TryGetFolderPathFromOrderKey(string key, out string folderPath)
    {
        if (key.StartsWith(FolderOrderPrefix, StringComparison.OrdinalIgnoreCase))
        {
            folderPath = NormalizeFolderPath(key[FolderOrderPrefix.Length..]);
            return folderPath.Length > 0;
        }

        folderPath = string.Empty;
        return false;
    }

    public static string NormalizeFolderPath(string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return string.Empty;
        }

        var segments = folderPath
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return string.Join('/', segments);
    }

    private static ProfileTreeNodeViewModel EnsureFolder(
        string folderPath,
        ICollection<ProfileTreeNodeViewModel> roots,
        IDictionary<string, ProfileTreeNodeViewModel> foldersByPath)
    {
        if (foldersByPath.TryGetValue(folderPath, out var existing))
        {
            return existing;
        }

        var currentPath = string.Empty;
        ProfileTreeNodeViewModel? parent = null;

        foreach (var segment in folderPath.Split('/'))
        {
            currentPath = currentPath.Length == 0 ? segment : $"{currentPath}/{segment}";
            if (!foldersByPath.TryGetValue(currentPath, out var current))
            {
                current = new ProfileTreeNodeViewModel(segment, currentPath, profile: null);
                foldersByPath.Add(currentPath, current);

                if (parent is null)
                {
                    roots.Add(current);
                }
                else
                {
                    parent.Children.Add(current);
                }
            }

            parent = current;
        }

        return foldersByPath[folderPath];
    }

    private static void CaptureOrder(
        IEnumerable<ProfileTreeNodeViewModel> nodes,
        ICollection<string> order)
    {
        foreach (var node in nodes)
        {
            order.Add(GetOrderKey(node));
            CaptureOrder(node.Children, order);
        }
    }

    private static void SortChildren(
        IList<ProfileTreeNodeViewModel> nodes,
        IReadOnlyDictionary<string, int> orderIndex)
    {
        var sorted = nodes
            .Select((node, index) => new
            {
                Node = node,
                OriginalIndex = index,
                HasOrder = orderIndex.TryGetValue(GetOrderKey(node), out var order),
                Order = order
            })
            .OrderBy(x => x.HasOrder ? 0 : 1)
            .ThenBy(x => x.Order)
            .ThenByDescending(x => x.Node.IsFolder)
            .ThenBy(x => x.Node.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.OriginalIndex)
            .ToArray();

        nodes.Clear();
        foreach (var item in sorted)
        {
            var node = item.Node;
            if (node.Children.Count > 0)
            {
                SortChildren(node.Children, orderIndex);
            }

            nodes.Add(node);
        }
    }
}
