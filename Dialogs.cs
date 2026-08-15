using Avalonia.Controls;
using Avalonia.Layout;

namespace Alexandreia;

/// <summary>
/// Avalonia has no built-in confirmation dialog. Twenty lines instead of a dependency.
/// </summary>
public static class Dialogs
{
    public static Task<bool> Confirm(Window? owner, string text, string ok = "Conferma")
    {
        if (owner is null) return Task.FromResult(false);

        var result = new TaskCompletionSource<bool>();
        var yes = new Button { Content = ok, IsDefault = true };
        var no = new Button { Content = "Annulla", IsCancel = true };

        var dialog = new Window
        {
            Title = "Alexandreia",
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(24),
                Spacing = 20,
                Children =
                {
                    new TextBlock { Text = text, MaxWidth = 420, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 10,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children = { no, yes },
                    },
                },
            },
        };

        yes.Click += (_, _) => { result.TrySetResult(true); dialog.Close(); };
        no.Click += (_, _) => { result.TrySetResult(false); dialog.Close(); };
        dialog.Closed += (_, _) => result.TrySetResult(false);

        dialog.ShowDialog(owner);
        return result.Task;
    }
}
