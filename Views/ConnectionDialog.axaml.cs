using Avalonia.Controls;
using Avalonia.Interactivity;
using MySqlManagementStudio.ViewModels;

namespace MySqlManagementStudio.Views;

public partial class ConnectionDialog : Window
{
    public ConnectionDialog()
    {
        InitializeComponent();
    }

    private void OnConnectClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ConnectionDialogViewModel vm)
            return;

        if (!vm.TryConfirm(out var error))
        {
            vm.StatusMessage = error ?? "Invalid connection details.";
            return;
        }

        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
