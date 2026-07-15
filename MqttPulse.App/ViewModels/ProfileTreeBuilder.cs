using MqttPulse.App.Models;

namespace MqttPulse.App.ViewModels;

public static class ProfileTreeBuilder
{
    public static IReadOnlyList<ProfileTreeNodeViewModel> Build(
        IEnumerable<BrokerProfile> profiles,
        IEnumerable<string> folderPaths)
    {
        var roots = new List<ProfileTreeNodeViewModel>();
        var foldersByPath = new Dictionary<string, ProfileTreeNodeViewModel>(StringComparer.OrdinalIgnoreCase);

        foreach (var folderPath in folderPaths.Select(NormalizeFolderPath).Where(x => x.Length > 0))
        {
            EnsureFolder(folderPath, roots, foldersByPath);
        }

        foreach (var profile in profiles.OrderBy(x => x.FolderPath, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
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

        SortChildren(roots);
        return roots;
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

    private static void SortChildren(IList<ProfileTreeNodeViewModel> nodes)
    {
        var sorted = nodes
            .OrderByDescending(x => x.IsFolder)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        nodes.Clear();
        foreach (var node in sorted)
        {
            if (node.Children.Count > 0)
            {
                SortChildren(node.Children);
            }

            nodes.Add(node);
        }
    }
}
