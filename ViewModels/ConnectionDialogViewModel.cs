using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MySqlManagementStudio.Models;
using MySqlManagementStudio.Services;

namespace MySqlManagementStudio.ViewModels;

public partial class ConnectionDialogViewModel : ViewModelBase
{
    private readonly MySqlDatabaseService _db;

    [ObservableProperty]
    private string _name = "MySQL Server";

    [ObservableProperty]
    private string _host = "localhost";

    [ObservableProperty]
    private string _portText = "3306";

    [ObservableProperty]
    private string _username = "root";

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _defaultDatabase = string.Empty;

    [ObservableProperty]
    private bool _savePassword = true;

    [ObservableProperty]
    private bool _useSsl;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _testSucceeded;

    public Guid? ExistingId { get; private set; }
    public bool Confirmed { get; private set; }
    public ConnectionProfile? Result { get; private set; }

    public ConnectionDialogViewModel(MySqlDatabaseService db, ConnectionProfile? existing = null)
    {
        _db = db;
        if (existing is null)
            return;

        ExistingId = existing.Id;
        Name = existing.Name;
        Host = existing.Host;
        PortText = existing.Port.ToString();
        Username = existing.Username;
        Password = existing.Password ?? string.Empty;
        DefaultDatabase = existing.DefaultDatabase ?? string.Empty;
        SavePassword = existing.SavePassword;
        UseSsl = existing.UseSsl;
    }

    public ConnectionProfile ToProfile()
    {
        if (!int.TryParse(PortText, out var port) || port is < 1 or > 65535)
            port = 3306;

        return new ConnectionProfile
        {
            Id = ExistingId ?? Guid.NewGuid(),
            Name = string.IsNullOrWhiteSpace(Name) ? $"{Username}@{Host}" : Name.Trim(),
            Host = Host.Trim(),
            Port = port,
            Username = Username.Trim(),
            Password = Password,
            DefaultDatabase = string.IsNullOrWhiteSpace(DefaultDatabase) ? null : DefaultDatabase.Trim(),
            SavePassword = SavePassword,
            UseSsl = UseSsl
        };
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        IsBusy = true;
        TestSucceeded = false;
        StatusMessage = "Testing connection...";
        try
        {
            await _db.TestConnectionAsync(ToProfile());
            TestSucceeded = true;
            StatusMessage = "Connection succeeded.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Connection failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public bool TryConfirm(out string? error)
    {
        if (string.IsNullOrWhiteSpace(Host))
        {
            error = "Host is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Username))
        {
            error = "Username is required.";
            return false;
        }

        if (!int.TryParse(PortText, out var port) || port is < 1 or > 65535)
        {
            error = "Port must be between 1 and 65535.";
            return false;
        }

        Result = ToProfile();
        Confirmed = true;
        error = null;
        return true;
    }
}
