using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using BetterWinTab.Models;
using BetterWinTab.Services;
using BetterWinTab.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace BetterWinTab.Views;

public sealed partial class SshWorkspace : UserControl
{
    private SshMachineService? _service;
    private SshMachine? _editing;
    private ContentDialog? _removeDialog;
    private bool _listeningForChanges;

    public SshWorkspaceViewModel ViewModel { get; } = new();
    public bool IsEditingMachine => EditorPanel.Visibility == Visibility.Visible || _removeDialog != null;
    public event EventHandler<SshMachine>? ConnectRequested;

    public SshWorkspace()
    {
        InitializeComponent();
    }

    public void Initialize(SshMachineService service)
    {
        if (_service != null) return;
        _service = service;
        if (IsLoaded) ListenForChanges();
    }

    public void FocusSearch()
    {
        if (IsEditingMachine) return;
        RefreshList();
        SearchBox.Focus(FocusState.Programmatic);
    }

    public void Search(string query) => SearchBox.Text = query;

    public bool HasKeyboardFocus()
    {
        if (XamlRoot == null) return false;

        for (var element = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
             element != null;
             element = VisualTreeHelper.GetParent(element))
        {
            if (ReferenceEquals(element, this)) return true;
        }

        return false;
    }

    public void Deactivate()
    {
        _removeDialog?.Hide();
        CloseEditor();
        WorkspaceStatus.IsOpen = false;
    }

    public void ShowConnectionError(string message)
    {
        WorkspaceStatus.Message = message;
        WorkspaceStatus.IsOpen = true;
    }

    private void Workspace_Loaded(object sender, RoutedEventArgs e) => ListenForChanges();

    private void Workspace_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_service != null && _listeningForChanges)
        {
            _service.MachinesChanged -= MachinesChanged;
            _listeningForChanges = false;
        }
        Deactivate();
    }

    private void ListenForChanges()
    {
        if (_service == null || _listeningForChanges) return;
        _service.MachinesChanged += MachinesChanged;
        _listeningForChanges = true;
        RefreshList();
    }

    private void MachinesChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_listeningForChanges) RefreshList();
        });
    }

    private void RefreshList()
    {
        if (_service == null) return;
        TryCatalogOperation(() => ViewModel.Refresh(_service), ShowConnectionError);
        UpdateEmptyState();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox searchBox) return;
        ViewModel.Search(searchBox.Text);
        if (EmptyState != null) UpdateEmptyState();
    }

    private void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && ViewModel.Results.FirstOrDefault() is { } match)
        {
            RequestConnection(match.Machine);
            e.Handled = true;
        }
    }

    private void UpdateEmptyState()
    {
        EmptyState.Visibility = ViewModel.Results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyTitle.Text = ViewModel.HasMachines ? "No matching machines" : "No SSH machines";
        EmptyDescription.Text = ViewModel.HasMachines
            ? "Try another address, name, or user."
            : "Add a machine to connect from BetterWinTab.";
    }

    private void AddButton_Click(object sender, RoutedEventArgs e) => OpenEditor(null);

    private void EditMachine_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SshMachine machine }) OpenEditor(machine);
    }

    private void OpenEditor(SshMachine? machine)
    {
        _editing = machine;
        EditorTitle.Text = machine == null ? "Add SSH machine" : "Edit SSH machine";
        NameBox.Text = machine?.Name ?? string.Empty;
        HostBox.Text = machine?.Host ?? string.Empty;
        PortBox.Text = (machine?.Port ?? 22).ToString(CultureInfo.InvariantCulture);
        UsernameBox.Text = machine?.Username ?? string.Empty;
        MachinePasswordBox.Password = string.Empty;
        MachinePasswordBox.Header = machine == null ? "Password" : "New password (optional)";
        MachinePasswordBox.PlaceholderText = machine == null ? string.Empty : "Leave empty to keep the saved password";
        EditorStatus.IsOpen = false;
        BrowsePanel.Visibility = Visibility.Collapsed;
        EditorPanel.Visibility = Visibility.Visible;
        HostBox.Focus(FocusState.Programmatic);
    }

    private void SaveMachine_Click(object sender, RoutedEventArgs e)
    {
        if (_service == null) return;
        if (!int.TryParse(PortBox.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int port)
            || port is < 1 or > 65535)
        {
            ShowEditorError("Enter a port from 1 to 65535.");
            PortBox.Focus(FocusState.Programmatic);
            return;
        }

        var saved = TryCatalogOperation(() =>
        {
            var password = MachinePasswordBox.Password;
            if (_editing == null)
                _service.Create(NameBox.Text, HostBox.Text, port, UsernameBox.Text, password);
            else
                _service.Update(_editing.Id, NameBox.Text, HostBox.Text, port, UsernameBox.Text,
                    password.Length == 0 ? null : password);
        }, ShowEditorError);

        if (!saved) return;
        WorkspaceStatus.IsOpen = false;
        CloseEditor();
        SearchBox.Text = string.Empty;
        FocusSearch();
    }

    private void CancelMachine_Click(object sender, RoutedEventArgs e)
    {
        CloseEditor();
        FocusSearch();
    }

    private void CloseEditor()
    {
        _editing = null;
        MachinePasswordBox.Password = string.Empty;
        EditorStatus.IsOpen = false;
        EditorPanel.Visibility = Visibility.Collapsed;
        BrowsePanel.Visibility = Visibility.Visible;
    }

    private void ShowEditorError(string message)
    {
        EditorStatus.Message = message;
        EditorStatus.IsOpen = true;
    }

    private async void RemoveMachine_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: SshMachine machine }
            || _service == null || XamlRoot == null || _removeDialog != null)
            return;

        var dialog = new ContentDialog
        {
            Title = "Remove SSH machine?",
            Content = $"Remove {new SshMachineListItem(machine).Title} and its saved password from BetterWinTab?",
            PrimaryButtonText = "Remove",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        _removeDialog = dialog;
        try
        {
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            WorkspaceStatus.IsOpen = false;
            TryCatalogOperation(() =>
            {
                if (!_service.Delete(machine.Id))
                    ShowConnectionError("This machine was already removed.");
            }, ShowConnectionError);
            RefreshList();
        }
        finally
        {
            _removeDialog = null;
        }
    }

    private void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SshMachine machine }) RequestConnection(machine);
    }

    private void RequestConnection(SshMachine machine)
    {
        WorkspaceStatus.IsOpen = false;
        ConnectRequested?.Invoke(this, machine);
    }

    private static bool TryCatalogOperation(Action operation, Action<string> onError)
    {
        try
        {
            operation();
            return true;
        }
        catch (ArgumentException error)
        {
            onError(error.Message);
        }
        catch (InvalidOperationException error)
        {
            onError(error.Message);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or CryptographicException)
        {
            onError("Could not access the saved SSH machines. Check that the BetterWinTab data folder is available.");
        }

        return false;
    }
}
