using System.Text.Json.Serialization;

namespace MySqlManagementStudio.Models;

public sealed class ConnectionProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "MySQL Server";
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 3306;
    public string Username { get; set; } = "root";
    public string? Password { get; set; }
    public string? DefaultDatabase { get; set; }
    public bool SavePassword { get; set; } = true;
    public bool UseSsl { get; set; }

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Name)
        ? $"{Username}@{Host}:{Port}"
        : Name;

    public string BuildConnectionString(string? database = null)
    {
        var builder = new MySqlConnector.MySqlConnectionStringBuilder
        {
            Server = Host,
            Port = (uint)Port,
            UserID = Username,
            Password = Password ?? string.Empty,
            Database = database ?? DefaultDatabase ?? string.Empty,
            SslMode = UseSsl
                ? MySqlConnector.MySqlSslMode.Required
                : MySqlConnector.MySqlSslMode.Preferred,
            AllowUserVariables = true,
            DefaultCommandTimeout = 60,
            ConnectionTimeout = 15
        };

        return builder.ConnectionString;
    }
}
