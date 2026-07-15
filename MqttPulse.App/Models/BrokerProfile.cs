using MqttPulse.App.Infrastructure;

namespace MqttPulse.App.Models;

public sealed class BrokerProfile : ObservableObject
{
    private string _id = Guid.NewGuid().ToString("N");
    private string _name = "Local broker";
    private string _folderPath = string.Empty;
    private string _transport = "mqtt";
    private string _host = "localhost";
    private int _port = 1883;
    private string _webSocketPath = string.Empty;
    private string _clientId = string.Empty;
    private string _username = string.Empty;
    private string _password = string.Empty;
    private bool _useTls;
    private bool _validateCertificate = true;
    private bool _enableSshTunnel;
    private string _sshHost = string.Empty;
    private int _sshPort = 22;
    private string _sshUsername = string.Empty;
    private string _sshPrivateKeyPath = string.Empty;
    private int _sshLocalPort;
    private string _subscribeTopic = "#";
    private int _subscribeQos;
    private int _maxHistoryPerTopic = 300;

    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string FolderPath
    {
        get => _folderPath;
        set => SetProperty(ref _folderPath, value ?? string.Empty);
    }

    public string Transport
    {
        get => _transport;
        set => SetProperty(ref _transport, NormalizeTransport(value));
    }

    public string Host
    {
        get => _host;
        set => SetProperty(ref _host, value);
    }

    public int Port
    {
        get => _port;
        set => SetProperty(ref _port, value);
    }

    public string WebSocketPath
    {
        get => _webSocketPath;
        set => SetProperty(ref _webSocketPath, value ?? string.Empty);
    }

    public string ClientId
    {
        get => _clientId;
        set => SetProperty(ref _clientId, value);
    }

    public string Username
    {
        get => _username;
        set => SetProperty(ref _username, value);
    }

    public string Password
    {
        get => _password;
        set => SetProperty(ref _password, value);
    }

    public bool UseTls
    {
        get => _useTls;
        set => SetProperty(ref _useTls, value);
    }

    public bool ValidateCertificate
    {
        get => _validateCertificate;
        set => SetProperty(ref _validateCertificate, value);
    }

    public bool EnableSshTunnel
    {
        get => _enableSshTunnel;
        set => SetProperty(ref _enableSshTunnel, value);
    }

    public string SshHost
    {
        get => _sshHost;
        set => SetProperty(ref _sshHost, value ?? string.Empty);
    }

    public int SshPort
    {
        get => _sshPort;
        set => SetProperty(ref _sshPort, Math.Clamp(value, 1, 65535));
    }

    public string SshUsername
    {
        get => _sshUsername;
        set => SetProperty(ref _sshUsername, value ?? string.Empty);
    }

    public string SshPrivateKeyPath
    {
        get => _sshPrivateKeyPath;
        set => SetProperty(ref _sshPrivateKeyPath, value ?? string.Empty);
    }

    public int SshLocalPort
    {
        get => _sshLocalPort;
        set => SetProperty(ref _sshLocalPort, Math.Clamp(value, 0, 65535));
    }

    public string SubscribeTopic
    {
        get => _subscribeTopic;
        set => SetProperty(ref _subscribeTopic, value);
    }

    public int SubscribeQos
    {
        get => _subscribeQos;
        set => SetProperty(ref _subscribeQos, value);
    }

    public int MaxHistoryPerTopic
    {
        get => _maxHistoryPerTopic;
        set => SetProperty(ref _maxHistoryPerTopic, Math.Clamp(value, 10, 20_000));
    }

    public static BrokerProfile CreateDefault() => new();

    public BrokerProfile Clone()
    {
        return new BrokerProfile
        {
            Id = Id,
            Name = Name,
            FolderPath = FolderPath,
            Transport = Transport,
            Host = Host,
            Port = Port,
            WebSocketPath = WebSocketPath,
            ClientId = ClientId,
            Username = Username,
            Password = Password,
            UseTls = UseTls,
            ValidateCertificate = ValidateCertificate,
            EnableSshTunnel = EnableSshTunnel,
            SshHost = SshHost,
            SshPort = SshPort,
            SshUsername = SshUsername,
            SshPrivateKeyPath = SshPrivateKeyPath,
            SshLocalPort = SshLocalPort,
            SubscribeTopic = SubscribeTopic,
            SubscribeQos = SubscribeQos,
            MaxHistoryPerTopic = MaxHistoryPerTopic
        };
    }

    private static string NormalizeTransport(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "ws" => "ws",
            "wss" => "wss",
            _ => "mqtt"
        };
    }
}