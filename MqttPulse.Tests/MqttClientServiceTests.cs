using MqttPulse.App.Models;
using MqttPulse.App.Services;

namespace MqttPulse.Tests;

[TestClass]
public sealed class MqttClientServiceTests
{
    [TestMethod]
    public void BuildWebSocketUriUsesTransportHostPortAndPath()
    {
        var profile = new BrokerProfile
        {
            Transport = "wss",
            Host = "172.16.1.224",
            Port = 9001,
            WebSocketPath = "ws"
        };

        Assert.AreEqual("wss://172.16.1.224:9001/ws", MqttClientService.BuildWebSocketUri(profile));
    }

    [TestMethod]
    public void BrokerProfileClonesValidateCertificate()
    {
        var profile = new BrokerProfile
        {
            UseTls = true,
            ValidateCertificate = false
        };

        var clone = profile.Clone();

        Assert.IsTrue(clone.UseTls);
        Assert.IsFalse(clone.ValidateCertificate);
    }

    [TestMethod]
    public void BrokerProfileClonesSshTunnelSettingsWithoutPasswordStorage()
    {
        var profile = new BrokerProfile
        {
            EnableSshTunnel = true,
            SshHost = "jump.example.com",
            SshPort = 2202,
            SshUsername = "edge",
            SshPrivateKeyPath = @"~\.ssh\id_ed25519",
            SshLocalPort = 28883,
            Host = "10.10.0.5",
            Port = 1883
        };

        var clone = profile.Clone();

        Assert.IsTrue(clone.EnableSshTunnel);
        Assert.AreEqual("jump.example.com", clone.SshHost);
        Assert.AreEqual(2202, clone.SshPort);
        Assert.AreEqual("edge", clone.SshUsername);
        Assert.AreEqual(@"~\.ssh\id_ed25519", clone.SshPrivateKeyPath);
        Assert.AreEqual(28883, clone.SshLocalPort);
    }

    [TestMethod]
    public void BrokerProfileDefaultsToPlainMqttTransport()
    {
        var profile = BrokerProfile.CreateDefault();

        Assert.AreEqual("mqtt", profile.Transport);
    }

    [TestMethod]
    public void ReconnectDelayBacksOffAndCapsAtTenSeconds()
    {
        Assert.AreEqual(TimeSpan.FromSeconds(1), MqttClientService.GetReconnectDelay(1));
        Assert.AreEqual(TimeSpan.FromSeconds(2), MqttClientService.GetReconnectDelay(2));
        Assert.AreEqual(TimeSpan.FromSeconds(5), MqttClientService.GetReconnectDelay(3));
        Assert.AreEqual(TimeSpan.FromSeconds(10), MqttClientService.GetReconnectDelay(4));
        Assert.AreEqual(TimeSpan.FromSeconds(10), MqttClientService.GetReconnectDelay(99));
    }

    [TestMethod]
    public void SshForwardSummaryShowsLocalAndBrokerTarget()
    {
        var profile = new BrokerProfile
        {
            Host = "10.10.0.5",
            Port = 1883
        };

        Assert.AreEqual("127.0.0.1:28883 -> 10.10.0.5:1883", SshTunnelProcessService.BuildForwardSummary(profile, 28883));
    }
}