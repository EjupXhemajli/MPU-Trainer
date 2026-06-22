using MpuTrainer.Models;
using MpuTrainer.Services;

namespace MpuTrainer.AI;

public interface IAiClientFactory
{
    /// <summary>Erstellt einen Client aus den gespeicherten Einstellungen und dem hinterlegten Key.</summary>
    IAiClient Create();

    /// <summary>Erstellt einen Client aus uebergebenen (ggf. ungespeicherten) Werten - fuer den Verbindungstest.</summary>
    IAiClient Create(AppSettings settings, string apiKey);
}

/// <summary>
/// Erzeugt den passenden KI-Client je nach gewaehltem Anbieter. Trennt damit die
/// uebrige Anwendung von Anbieter-spezifischen Details.
/// </summary>
public class AiClientFactory : IAiClientFactory
{
    private readonly ISettingsService _settings;
    private readonly ISecretStore _secrets;

    public AiClientFactory(ISettingsService settings, ISecretStore secrets)
    {
        _settings = settings;
        _secrets = secrets;
    }

    public IAiClient Create()
    {
        var key = _secrets.LoadApiKey()
                  ?? throw new InvalidOperationException(
                      "Kein API-Key hinterlegt. Bitte in den Einstellungen eintragen.");
        return Create(_settings.Current, key);
    }

    public IAiClient Create(AppSettings settings, string apiKey)
    {
        return settings.Provider switch
        {
            AiProvider.OpenAiCompatible =>
                new OpenAiCompatibleClient(apiKey, settings.Model, settings.BaseUrl),
            _ =>
                new AnthropicClient(apiKey, settings.Model, settings.BaseUrl)
        };
    }
}
