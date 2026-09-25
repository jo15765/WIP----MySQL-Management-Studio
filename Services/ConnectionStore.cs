using System.Text.Json;
using MySqlManagementStudio.Models;

namespace MySqlManagementStudio.Services;

public sealed class ConnectionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        // Compact JSON is faster to write/read; indentation is not needed at runtime.
        WriteIndented = false
    };

    private readonly string _filePath;

    public ConnectionStore()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MySqlManagementStudio");
        Directory.CreateDirectory(folder);
        _filePath = Path.Combine(folder, "connections.json");
    }

    public List<ConnectionProfile> Load()
    {
        if (!File.Exists(_filePath))
            return [];

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<List<ConnectionProfile>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public void Save(IEnumerable<ConnectionProfile> profiles)
    {
        var sanitized = profiles.Select(p => new ConnectionProfile
        {
            Id = p.Id,
            Name = p.Name,
            Host = p.Host,
            Port = p.Port,
            Username = p.Username,
            Password = p.SavePassword ? p.Password : null,
            DefaultDatabase = p.DefaultDatabase,
            SavePassword = p.SavePassword,
            UseSsl = p.UseSsl
        }).ToList();

        File.WriteAllText(_filePath, JsonSerializer.Serialize(sanitized, JsonOptions));
    }
}
