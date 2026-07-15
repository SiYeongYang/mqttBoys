namespace MqttPulse.App.Models;

public sealed record ProfileLibrary(
    IReadOnlyList<BrokerProfile> Profiles,
    IReadOnlyList<string> FolderPaths);
