using System.Buffers;
using System.Diagnostics;
using System.Text;
using MQTTnet;
using MQTTnet.Protocol;
using MqttPulse.App.Models;
using MqttPulse.Core;

namespace MqttPulse.App.Services;

public sealed class MqttClientService : IDisposable
{
    private readonly MqttClientFactory _factory = new();
    private readonly SshTunnelProcessService _sshTunnelService = new();
    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private IMqttClient? _client;
    private SshTunnelProcessSession? _sshTunnel;
    private CancellationTokenSource? _connectionLifetime;
    private BrokerProfile? _activeProfile;
    private string _currentSubscribeTopic = string.Empty;
    private int _connectionVersion;
    private int _isReconnecting;
    private bool _isDisposed;

    public event Action<MqttMessageSnapshot>? MessageReceived;

    public event Action<string>? StatusChanged;

    public bool IsConnected => _client?.IsConnected == true;

    public async Task ConnectAsync(BrokerProfile profile, CancellationToken cancellationToken)
    {
        await DisconnectAsync(CancellationToken.None);

        var profileSnapshot = profile.Clone();
        var lifetime = new CancellationTokenSource();

        await _connectionGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            _connectionVersion++;
            _connectionLifetime = lifetime;
            _activeProfile = profileSnapshot;
            _currentSubscribeTopic = string.Empty;
            Interlocked.Exchange(ref _isReconnecting, 0);

            var endpoint = await PrepareEndpointAsync(profileSnapshot, cancellationToken);
            var client = CreateClient();
            _client = client;

            await ConnectClientAsync(client, profileSnapshot, endpoint, cancellationToken, "Connected");
        }
        catch
        {
            lifetime.Cancel();
            lifetime.Dispose();

            if (_client is not null)
            {
                DetachClientHandlers(_client);
                _client.Dispose();
            }

            _client = null;
            DisposeSshTunnel();
            _connectionLifetime = null;
            _activeProfile = null;
            _currentSubscribeTopic = string.Empty;
            Interlocked.Exchange(ref _isReconnecting, 0);
            throw;
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        IMqttClient? client;
        SshTunnelProcessSession? tunnel;
        CancellationTokenSource? lifetime;

        await _connectionGate.WaitAsync(CancellationToken.None);
        try
        {
            _connectionVersion++;
            Interlocked.Exchange(ref _isReconnecting, 0);

            lifetime = _connectionLifetime;
            _connectionLifetime = null;
            _activeProfile = null;
            _currentSubscribeTopic = string.Empty;

            client = _client;
            _client = null;

            tunnel = _sshTunnel;
            _sshTunnel = null;

            lifetime?.Cancel();
        }
        finally
        {
            _connectionGate.Release();
        }

        if (client is not null)
        {
            DetachClientHandlers(client);

            if (client.IsConnected)
            {
                await client.DisconnectAsync(_factory.CreateClientDisconnectOptionsBuilder().Build(), cancellationToken);
            }

            client.Dispose();
        }

        tunnel?.Dispose();
        lifetime?.Dispose();
        StatusChanged?.Invoke("Disconnected");
    }

    public async Task PublishAsync(string topic, string payload, int qos, bool retain, CancellationToken cancellationToken)
    {
        var client = _client;
        if (client is null || !client.IsConnected)
        {
            throw new InvalidOperationException("MQTT client is not connected.");
        }

        var message = _factory.CreateApplicationMessageBuilder()
            .WithTopic(topic.Trim())
            .WithPayload(payload)
            .WithQualityOfServiceLevel(ToQos(qos))
            .WithRetainFlag(retain)
            .Build();

        await client.PublishAsync(message, cancellationToken);
    }

    public async Task ReplaceSubscriptionAsync(string topic, int qos, CancellationToken cancellationToken)
    {
        var client = _client;
        if (client is null || !client.IsConnected)
        {
            throw new InvalidOperationException("MQTT client is not connected.");
        }

        await ReplaceSubscriptionAsync(client, topic, qos, cancellationToken);
    }

    public Task RestoreProfileSubscriptionAsync(BrokerProfile profile, CancellationToken cancellationToken)
    {
        return ReplaceSubscriptionAsync(GetSubscribeTopic(profile), profile.SubscribeQos, cancellationToken);
    }

    public static string BuildWebSocketUri(BrokerProfile profile)
    {
        return BuildWebSocketUri(profile, profile.Host.Trim(), profile.Port);
    }

    public static TimeSpan GetReconnectDelay(int attempt)
    {
        return attempt switch
        {
            <= 1 => TimeSpan.FromSeconds(1),
            2 => TimeSpan.FromSeconds(2),
            3 => TimeSpan.FromSeconds(5),
            _ => TimeSpan.FromSeconds(10)
        };
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _connectionVersion++;
        Interlocked.Exchange(ref _isReconnecting, 0);

        _connectionLifetime?.Cancel();
        _connectionLifetime?.Dispose();
        _connectionLifetime = null;
        _activeProfile = null;

        if (_client is not null)
        {
            DetachClientHandlers(_client);
            _client.Dispose();
            _client = null;
        }

        DisposeSshTunnel();
        _connectionGate.Dispose();
    }

    private async Task<ConnectionEndpoint> PrepareEndpointAsync(BrokerProfile profile, CancellationToken cancellationToken)
    {
        if (!profile.EnableSshTunnel)
        {
            DisposeSshTunnel();
            return new ConnectionEndpoint(profile.Host.Trim(), profile.Port, profile.Host.Trim());
        }

        if (_sshTunnel is null || !_sshTunnel.IsActive || !_sshTunnel.Matches(profile))
        {
            DisposeSshTunnel();
            StatusChanged?.Invoke($"Opening SSH tunnel: {profile.SshHost.Trim()} -> {profile.Host.Trim()}:{profile.Port}");
            _sshTunnel = await _sshTunnelService.StartAsync(profile, cancellationToken);
            StatusChanged?.Invoke($"SSH tunnel: {SshTunnelProcessService.BuildForwardSummary(profile, _sshTunnel.LocalPort)}");
        }

        return new ConnectionEndpoint("127.0.0.1", _sshTunnel.LocalPort, profile.Host.Trim());
    }

    private async Task ConnectClientAsync(
        IMqttClient client,
        BrokerProfile profile,
        ConnectionEndpoint endpoint,
        CancellationToken cancellationToken,
        string connectedLabel)
    {
        var connectResult = await client.ConnectAsync(BuildClientOptions(profile, endpoint), cancellationToken);
        StatusChanged?.Invoke($"{connectedLabel}: {connectResult.ResultCode}");

        await ReplaceSubscriptionAsync(client, GetSubscribeTopic(profile), profile.SubscribeQos, cancellationToken);
    }

    private IMqttClient CreateClient()
    {
        var client = _factory.CreateMqttClient();
        client.ApplicationMessageReceivedAsync += OnApplicationMessageReceivedAsync;
        client.DisconnectedAsync += OnDisconnectedAsync;
        return client;
    }

    private void DetachClientHandlers(IMqttClient client)
    {
        client.ApplicationMessageReceivedAsync -= OnApplicationMessageReceivedAsync;
        client.DisconnectedAsync -= OnDisconnectedAsync;
    }

    private MqttClientOptions BuildClientOptions(BrokerProfile profile, ConnectionEndpoint endpoint)
    {
        var optionsBuilder = _factory.CreateClientOptionsBuilder()
            .WithClientId(BuildClientId(profile))
            .WithCleanSession(true)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(15))
            .WithTimeout(TimeSpan.FromSeconds(15));

        if (profile.Transport == "ws" || profile.Transport == "wss")
        {
            optionsBuilder.WithWebSocketServer(webSocket =>
            {
                webSocket.WithUri(BuildWebSocketUri(profile, endpoint.ConnectHost, endpoint.ConnectPort));
                webSocket.WithSubProtocols(new[] { "mqtt" });
            });
        }
        else
        {
            optionsBuilder.WithTcpServer(endpoint.ConnectHost, endpoint.ConnectPort);
        }

        if (!string.IsNullOrWhiteSpace(profile.Username))
        {
            optionsBuilder.WithCredentials(profile.Username, profile.Password);
        }

        if (profile.UseTls || profile.Transport == "wss")
        {
            optionsBuilder.WithTlsOptions(tls =>
            {
                tls.UseTls(true);
                tls.WithTargetHost(endpoint.TlsTargetHost);
                tls.WithIgnoreCertificateChainErrors(!profile.ValidateCertificate);
                tls.WithIgnoreCertificateRevocationErrors(!profile.ValidateCertificate);
                tls.WithAllowUntrustedCertificates(!profile.ValidateCertificate);
            });
        }

        return optionsBuilder.Build();
    }

    private async Task ReplaceSubscriptionAsync(IMqttClient client, string topic, int qos, CancellationToken cancellationToken)
    {
        var normalizedTopic = NormalizeTopic(topic);
        if (!string.IsNullOrWhiteSpace(_currentSubscribeTopic))
        {
            var unsubscribeOptions = _factory.CreateUnsubscribeOptionsBuilder()
                .WithTopicFilter(_currentSubscribeTopic)
                .Build();
            await client.UnsubscribeAsync(unsubscribeOptions, cancellationToken);
        }

        await SubscribeAsync(client, normalizedTopic, qos, cancellationToken);
    }

    private Task OnApplicationMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs args)
    {
        var receivedTimestamp = Stopwatch.GetTimestamp();
        var payload = DecodePayload(args.ApplicationMessage.Payload);
        var snapshot = new MqttMessageSnapshot(
            args.ApplicationMessage.Topic,
            payload,
            DateTimeOffset.Now,
            (int)args.ApplicationMessage.QualityOfServiceLevel,
            args.ApplicationMessage.Retain,
            receivedTimestamp);

        // Keep the MQTT callback light; heavy tree/history work happens on the UI batch timer.
        MessageReceived?.Invoke(snapshot);
        return Task.CompletedTask;
    }

    private Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs args)
    {
        StatusChanged?.Invoke($"Disconnected: {args.Reason}");

        if (_isDisposed || _activeProfile is null || _connectionLifetime is null || _connectionLifetime.IsCancellationRequested)
        {
            return Task.CompletedTask;
        }

        if (Interlocked.Exchange(ref _isReconnecting, 1) == 0)
        {
            var version = _connectionVersion;
            var token = _connectionLifetime.Token;
            _ = Task.Run(() => RunReconnectLoopAsync(version, token), CancellationToken.None);
        }

        return Task.CompletedTask;
    }

    private async Task RunReconnectLoopAsync(int version, CancellationToken cancellationToken)
    {
        try
        {
            for (var attempt = 1; !cancellationToken.IsCancellationRequested; attempt++)
            {
                var delay = GetReconnectDelay(attempt);
                StatusChanged?.Invoke($"Reconnect in {delay.TotalSeconds:0}s (attempt {attempt})");
                await Task.Delay(delay, cancellationToken);

                await _connectionGate.WaitAsync(cancellationToken);
                try
                {
                    if (cancellationToken.IsCancellationRequested || version != _connectionVersion || _activeProfile is null)
                    {
                        return;
                    }

                    if (_client?.IsConnected == true)
                    {
                        StatusChanged?.Invoke("Connected");
                        return;
                    }

                    if (_client is not null)
                    {
                        DetachClientHandlers(_client);
                        _client.Dispose();
                        _client = null;
                    }

                    var endpoint = await PrepareEndpointAsync(_activeProfile, cancellationToken);
                    var client = CreateClient();
                    _client = client;
                    _currentSubscribeTopic = string.Empty;

                    StatusChanged?.Invoke($"Reconnecting... attempt {attempt}");
                    await ConnectClientAsync(client, _activeProfile, endpoint, cancellationToken, "Reconnected");
                    return;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    if (!cancellationToken.IsCancellationRequested && version == _connectionVersion)
                    {
                        StatusChanged?.Invoke($"Reconnect failed: {ex.Message}");
                    }
                }
                finally
                {
                    _connectionGate.Release();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal path when the user disconnects while a reconnect delay is pending.
        }
        finally
        {
            Interlocked.Exchange(ref _isReconnecting, 0);
        }
    }

    private async Task SubscribeAsync(IMqttClient client, string topic, int qos, CancellationToken cancellationToken)
    {
        if (!client.IsConnected)
        {
            throw new InvalidOperationException("MQTT client is not connected.");
        }

        var subscribeOptions = _factory.CreateSubscribeOptionsBuilder()
            .WithTopicFilter(
                topic,
                ToQos(qos),
                noLocal: false,
                retainAsPublished: false,
                retainHandling: MqttRetainHandling.SendAtSubscribe)
            .Build();

        await client.SubscribeAsync(subscribeOptions, cancellationToken);
        _currentSubscribeTopic = topic;
        StatusChanged?.Invoke($"Subscribed: {topic}");
    }

    private static string BuildWebSocketUri(BrokerProfile profile, string host, int port)
    {
        var scheme = profile.Transport == "wss" ? "wss" : "ws";
        var path = profile.WebSocketPath.Trim();
        if (path.Length > 0 && !path.StartsWith('/'))
        {
            path = "/" + path;
        }

        return $"{scheme}://{host}:{port}{path}";
    }

    private static string DecodePayload(ReadOnlySequence<byte> payload)
    {
        if (payload.IsEmpty)
        {
            return string.Empty;
        }

        if (payload.IsSingleSegment)
        {
            return Encoding.UTF8.GetString(payload.FirstSpan);
        }

        var buffer = new byte[checked((int)payload.Length)];
        payload.CopyTo(buffer);
        return Encoding.UTF8.GetString(buffer);
    }

    private static string BuildClientId(BrokerProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.ClientId))
        {
            return profile.ClientId.Trim();
        }

        return $"mqttBoys-{Environment.MachineName}-{Guid.NewGuid():N}"[..32];
    }

    private static MqttQualityOfServiceLevel ToQos(int qos)
    {
        return qos switch
        {
            1 => MqttQualityOfServiceLevel.AtLeastOnce,
            2 => MqttQualityOfServiceLevel.ExactlyOnce,
            _ => MqttQualityOfServiceLevel.AtMostOnce
        };
    }

    private static string GetSubscribeTopic(BrokerProfile profile) =>
        NormalizeTopic(profile.SubscribeTopic);

    private static string NormalizeTopic(string topic)
    {
        var normalized = topic.Trim();
        return normalized.Length == 0 ? "#" : normalized;
    }

    private void DisposeSshTunnel()
    {
        _sshTunnel?.Dispose();
        _sshTunnel = null;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }

    private readonly record struct ConnectionEndpoint(string ConnectHost, int ConnectPort, string TlsTargetHost);
}