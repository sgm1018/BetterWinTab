using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Core;

namespace BetterWinTab.Views;

public sealed partial class MainPage
{
    private bool _isCallsOptionsOpen;

    private bool IsCallsControlFocused()
    {
        if (XamlRoot == null) return false;
        var focused = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
        while (focused != null)
        {
            if (ReferenceEquals(focused, CallsPanel)) return true;
            focused = VisualTreeHelper.GetParent(focused);
        }
        return false;
    }

    private void CallsComposer_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        var compact = args.NewSize.Width < 720;
        Grid.SetRow(CallsPrimaryActions, compact ? 1 : 0);
        Grid.SetColumn(CallsPrimaryActions, compact ? 0 : 1);
        Grid.SetColumnSpan(CallsPrimaryActions, compact ? 2 : 1);
    }

    private async void CallsOptions_Click(object sender, RoutedEventArgs args) => await ShowCallsOptionsAsync(0);
    private async void CallsTranscriptionOptions_Click(object sender, RoutedEventArgs args) => await ShowCallsOptionsAsync(1);

    private async Task ShowCallsOptionsAsync(int section)
    {
        if (_isCallsOptionsOpen || XamlRoot == null) return;
        _isCallsOptionsOpen = true;
        try
        {
            var dialog = new CallsOptionsDialog(ViewModel.Calls, section) { XamlRoot = XamlRoot };
            await dialog.ShowAsync();
        }
        finally { _isCallsOptionsOpen = false; }
    }

    private async void CallsComposer_KeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Enter ||
            (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & CoreVirtualKeyStates.Down) == 0) return;
        args.Handled = true;
        if (ViewModel.Calls.CanGenerateNotes)
            await ViewModel.Calls.GenerateReportCommand.ExecuteAsync(null);
    }

    private void CopyCallNotes_Click(object sender, RoutedEventArgs args)
    {
        if (!ViewModel.Calls.CanCopyNotes) return;
        try
        {
            var data = new DataPackage();
            data.SetText(ViewModel.Calls.ReportText);
            Clipboard.SetContent(data);
            ViewModel.Calls.Status = "Notes copied as Markdown.";
        }
        catch (Exception error) { ViewModel.Calls.Status = $"Could not copy notes: {error.Message}"; }
    }
}
