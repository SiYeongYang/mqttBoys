using System.IO;
using System.Text.Json;
using MqttPulse.App.Models;
using MqttPulse.App.ViewModels;

namespace MqttPulse.App.Services;

public sealed class ProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _path;

    public ProfileStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MqttPulse",
            "profiles.json"))
    {
    }

    public ProfileStore(string path)
    {
        _path = path;
        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(folder))
        {
            Directory.CreateDirectory(folder);
        }
    }

    public ProfileLibrary LoadLibrary()
    {
        if (!File.Exists(_path))
        {
            return new ProfileLibrary(Array.Empty<BrokerProfile>(), Array.Empty<string>());
        }

        try
        {
            var json = File.ReadAllText(_path);
            using var document = JsonDocument.Parse(json);

            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                var legacyProfiles = JsonSerializer.Deserialize<List<BrokerProfile>>(json, JsonOptions) ?? new List<BrokerProfile>();
                return new ProfileLibrary(legacyProfiles, FolderPathsFromProfiles(legacyProfiles));
            }

            var dto = JsonSerializer.Deserialize<ProfileLibraryDto>(json, JsonOptions);
            var profiles = dto?.Profiles ?? new List<BrokerProfile>();
            var folders = (dto?.FolderPaths ?? new List<string>())
                .Concat(FolderPathsFromProfiles(profiles))
                .Select(ProfileTreeBuilder.NormalizeFolderPath)
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return new ProfileLibrary(profiles, folders);
        }
        catch (JsonException)
        {
            return new ProfileLibrary(Array.Empty<BrokerProfile>(), Array.Empty<string>());
        }
        catch (IOException)
        {
            return new ProfileLibrary(Array.Empty<BrokerProfile>(), Array.Empty<string>());
        }
    }

    public IReadOnlyList<BrokerProfile> Load() => LoadLibrary().Profiles;

    public void Save(IEnumerable<BrokerProfile> profiles) => Save(profiles, Array.Empty<string>());

    public void Save(IEnumerable<BrokerProfile> profiles, IEnumerable<string> folderPaths)
    {
        var snapshots = profiles.Select(x => x.Clone()).ToArray();
        var folders = folderPaths
            .Concat(FolderPathsFromProfiles(snapshots))
            .Select(ProfileTreeBuilder.NormalizeFolderPath)
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var dto = new ProfileLibraryDto
        {
            Profiles = snapshots.ToList(),
            FolderPaths = folders.ToList()
        };

        using var stream = File.Create(_path);
        JsonSerializer.Serialize(stream, dto, JsonOptions);
    }

    private static IReadOnlyList<string> FolderPathsFromProfiles(IEnumerable<BrokerProfile> profiles)
    {
        return profiles
            .Select(x => ProfileTreeBuilder.NormalizeFolderPath(x.FolderPath))
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private sealed class ProfileLibraryDto
    {
        public List<BrokerProfile> Profiles { get; set; } = new();

        public List<string> FolderPaths { get; set; } = new();
    }
}
