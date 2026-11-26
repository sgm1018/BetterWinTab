using System.ComponentModel;
using System.Security.Cryptography;
using BetterWinTab.Models;
using BetterWinTab.Services;

namespace BetterWinTab.Views;

public sealed partial class MainPage
{
    private bool _openingSshTerminal;

    private void InitializeSshWorkspace()
    {
        SshWorkspaceView.Initialize(ServiceContainer.Resolve<SshMachineService>());
        SshWorkspaceView.ConnectRequested += OpenSshTerminal;
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsSshFolderSelected))
            {
                if (ViewModel.IsSshFolderSelected)
                {
                    SshWorkspaceView.Search(ViewModel.SearchQuery);
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (ViewModel.IsSshFolderSelected) SshWorkspaceView.FocusSearch();
                    });
                }
                else
                {
                    SshWorkspaceView.Deactivate();
                }
            }
        };
    }

    private async void OpenSshTerminal(object? sender, SshMachine machine)
    {
        if (_openingSshTerminal) return;
        _openingSshTerminal = true;
        try
        {
            var password = ServiceContainer.Resolve<SshMachineService>().GetPassword(machine);
            await ServiceContainer.Resolve<SshTerminalService>().OpenAsync(machine, password);
            App.Current.HideOverlay();
        }
        catch (CryptographicException)
        {
            SshWorkspaceView.ShowConnectionError("The saved password could not be decrypted.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception
            or InvalidOperationException or TimeoutException)
        {
            SshWorkspaceView.ShowConnectionError(ex.Message);
        }
        finally
        {
            _openingSshTerminal = false;
        }
    }
}
