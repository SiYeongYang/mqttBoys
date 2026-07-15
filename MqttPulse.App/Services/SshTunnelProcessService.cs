using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using MqttPulse.App.Models;

namespace MqttPulse.App.Services;

public sealed class SshTunnelProcessService
{
    private const int StartTimeoutMilliseconds = 15_000;

    public async Task<SshTunnelProcessSession> StartAsync(BrokerProfile profile, CancellationToken cancellationToken)
    {
        ValidateProfile(profile);

        var localPort = profile.SshLocalPort == 0
            ? AllocateFreeLocalPort()
            : profile.SshLocalPort;

        var process = StartSshProcess(profile, localPort);
        try
        {
            await WaitUntilReadyAsync(process, localPort, cancellationToken);
            return new SshTunnelProcessSession(
                process,
                profile.SshHost.Trim(),
                profile.SshPort,
                profile.SshUsername.Trim(),
                profile.SshPrivateKeyPath.Trim(),
                profile.Host.Trim(),
                profile.Port,
                profile.SshLocalPort,
                localPort);
        }
        catch
        {
            StopProcess(process);
            throw;
        }
    }

    public static string BuildForwardSummary(BrokerProfile profile, int localPort)
    {
        return $"127.0.0.1:{localPort} -> {profile.Host.Trim()}:{profile.Port}";
    }

    private static Process StartSshProcess(BrokerProfile profile, int localPort)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "ssh.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        startInfo.ArgumentList.Add("-N");
        startInfo.ArgumentList.Add("-T");
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add("ExitOnForwardFailure=yes");
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add("ServerAliveInterval=15");
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add("ServerAliveCountMax=2");
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add("BatchMode=yes");
        startInfo.ArgumentList.Add("-L");
        startInfo.ArgumentList.Add($"127.0.0.1:{localPort}:{profile.Host.Trim()}:{profile.Port}");
        startInfo.ArgumentList.Add("-p");
        startInfo.ArgumentList.Add(profile.SshPort.ToString(System.Globalization.CultureInfo.InvariantCulture));

        if (!string.IsNullOrWhiteSpace(profile.SshPrivateKeyPath))
        {
            startInfo.ArgumentList.Add("-i");
            startInfo.ArgumentList.Add(ExpandPath(profile.SshPrivateKeyPath.Trim()));
        }

        startInfo.ArgumentList.Add(BuildSshDestination(profile));

        return Process.Start(startInfo)
               ?? throw new InvalidOperationException("Failed to start ssh.exe.");
    }

    private static async Task WaitUntilReadyAsync(Process process, int localPort, CancellationToken cancellationToken)
    {
        var deadline = Environment.TickCount64 + StartTimeoutMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (process.HasExited)
            {
                var error = await process.StandardError.ReadToEndAsync(cancellationToken);
                throw new InvalidOperationException($"SSH tunnel failed: {TrimProcessOutput(error)}");
            }

            if (await CanConnectLocalPortAsync(localPort, cancellationToken))
            {
                return;
            }

            await Task.Delay(150, cancellationToken);
        }

        throw new TimeoutException("SSH tunnel did not become ready within 15 seconds.");
    }

    private static async Task<bool> CanConnectLocalPortAsync(int localPort, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, localPort, cancellationToken);
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static void ValidateProfile(BrokerProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.SshHost))
        {
            throw new InvalidOperationException("SSH host is required.");
        }

        if (string.IsNullOrWhiteSpace(profile.Host))
        {
            throw new InvalidOperationException("Broker host is required.");
        }

        if (profile.Port is < 1 or > 65535)
        {
            throw new InvalidOperationException("Broker port must be between 1 and 65535.");
        }

        if (profile.SshLocalPort is < 0 or > 65535)
        {
            throw new InvalidOperationException("SSH local port must be 0 or between 1 and 65535.");
        }
    }

    private static string BuildSshDestination(BrokerProfile profile)
    {
        var host = profile.SshHost.Trim();
        var user = profile.SshUsername.Trim();
        return user.Length == 0 ? host : $"{user}@{host}";
    }

    private static int AllocateFreeLocalPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static string ExpandPath(string path)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path);
        if (expanded == "~")
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        if (expanded.StartsWith("~/", StringComparison.Ordinal) || expanded.StartsWith(@"~\", StringComparison.Ordinal))
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                expanded[2..]);
        }

        return expanded;
    }

    private static string TrimProcessOutput(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length == 0 ? "ssh.exe exited without details." : trimmed;
    }

    private static void StopProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            process.Dispose();
        }
    }
}

public sealed class SshTunnelProcessSession : IDisposable
{
    private readonly Process _process;

    public SshTunnelProcessSession(
        Process process,
        string sshHost,
        int sshPort,
        string sshUsername,
        string sshPrivateKeyPath,
        string brokerHost,
        int brokerPort,
        int requestedLocalPort,
        int localPort)
    {
        _process = process;
        SshHost = sshHost;
        SshPort = sshPort;
        SshUsername = sshUsername;
        SshPrivateKeyPath = sshPrivateKeyPath;
        BrokerHost = brokerHost;
        BrokerPort = brokerPort;
        RequestedLocalPort = requestedLocalPort;
        LocalPort = localPort;
    }

    public string SshHost { get; }

    public int SshPort { get; }

    public string SshUsername { get; }

    public string SshPrivateKeyPath { get; }

    public string BrokerHost { get; }

    public int BrokerPort { get; }

    public int RequestedLocalPort { get; }

    public int LocalPort { get; }

    public bool IsActive => !_process.HasExited;

    public bool Matches(BrokerProfile profile)
    {
        return string.Equals(SshHost, profile.SshHost.Trim(), StringComparison.OrdinalIgnoreCase)
               && SshPort == profile.SshPort
               && string.Equals(SshUsername, profile.SshUsername.Trim(), StringComparison.Ordinal)
               && string.Equals(SshPrivateKeyPath, profile.SshPrivateKeyPath.Trim(), StringComparison.OrdinalIgnoreCase)
               && string.Equals(BrokerHost, profile.Host.Trim(), StringComparison.OrdinalIgnoreCase)
               && BrokerPort == profile.Port
               && RequestedLocalPort == profile.SshLocalPort;
    }

    public void Dispose()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            _process.Dispose();
        }
    }
}