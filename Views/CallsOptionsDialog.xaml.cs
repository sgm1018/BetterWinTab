using System.ComponentModel;
using BetterWinTab.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BetterWinTab.Views;

public sealed partial class CallsOptionsDialog : ContentDialog
{
    private readonly CallsViewModel _viewModel;

    public CallsOptionsDialog(CallsViewModel viewModel, int selectedSection = 0)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        Sections.SelectedIndex = Math.Clamp(selectedSection, 0, 2);
        Opened += OnOpened;
        Closed += OnClosed;
    }

    private void OnOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        if (XamlRoot != null)
        {
            OptionsContent.Width = Math.Clamp(XamlRoot.Size.Width - 112, 240, 704);
            OptionsContent.MaxHeight = Math.Clamp(XamlRoot.Size.Height - 200, 120, 480);
        }
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnClosed(ContentDialog sender, ContentDialogClosedEventArgs args)
        => _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

    private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs args)
        => _viewModel.ApiKey = ((PasswordBox)sender).Password;

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        // A successful save clears the entry without ever reading a stored credential.
        if (args.PropertyName == nameof(CallsViewModel.ApiKey) && string.IsNullOrEmpty(_viewModel.ApiKey))
            ApiKeyBox.Password = string.Empty;
    }
}
