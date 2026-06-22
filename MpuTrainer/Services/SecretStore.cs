using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace MpuTrainer.Services;

public interface ISecretStore
{
    void SaveApiKey(string apiKey);
    string? LoadApiKey();
    void Clear();

    /// <summary>Speichert ein benanntes Geheimnis (z. B. den Premium-Sprach-Key) verschluesselt.</summary>
    void Save(string name, string? value);

    /// <summary>Laedt ein benanntes Geheimnis oder gibt null zurueck.</summary>
    string? Load(string name);
}

/// <summary>
/// Speichert den API-Key verschluesselt mit der Windows-DPAPI (CurrentUser).
/// Die Datei kann nur vom selben Windows-Benutzer entschluesselt werden;
/// der Schluessel wird niemals im Klartext abgelegt.
/// </summary>
public class DpapiSecretStore : ISecretStore
{
    private readonly string _path =
        Path.Combine(App.DataDirectory, "secret.bin");

    // Zusaetzliche Entropie erschwert das Entschluesseln ausserhalb der App.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("MpuTrainer.v1");

    public void SaveApiKey(string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            Clear();
            return;
        }

        var plain = Encoding.UTF8.GetBytes(apiKey);
        var encrypted = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllBytes(_path, encrypted);
    }

    public string? LoadApiKey()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            var encrypted = File.ReadAllBytes(_path);
            var plain = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch
        {
            return null;
        }
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(_path)) File.Delete(_path);
        }
        catch { /* ignorieren */ }
    }

    // ---- Benannte Geheimnisse (z. B. Premium-Sprach-Key) ----

    private string NamedPath(string name) =>
        Path.Combine(App.DataDirectory, $"secret_{name}.bin");

    public void Save(string name, string? value)
    {
        var p = NamedPath(name);
        if (string.IsNullOrEmpty(value))
        {
            try { if (File.Exists(p)) File.Delete(p); } catch { /* ignorieren */ }
            return;
        }

        var plain = Encoding.UTF8.GetBytes(value);
        var encrypted = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllBytes(p, encrypted);
    }

    public string? Load(string name)
    {
        try
        {
            var p = NamedPath(name);
            if (!File.Exists(p)) return null;
            var encrypted = File.ReadAllBytes(p);
            var plain = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch
        {
            return null;
        }
    }
}
