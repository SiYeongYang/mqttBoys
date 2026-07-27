namespace MqttPulse.App.Services;

public interface IConfirmationService
{
    bool Confirm(string title, string message);
}
