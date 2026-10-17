using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace MicSomeNoise;

public sealed class PlaybackOverlay : Window
{
    private readonly TextBlock selection = new() { TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock state = new() { Margin = new Thickness(16, 0, 0, 0), FontWeight = FontWeights.SemiBold };
    private readonly TextBlock playback = new() { TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 4, 0, 0) };

    public PlaybackOverlay()
    {
        Title = "Mic Some Noise playback";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowActivated = false;
        ShowInTaskbar = false;
        Focusable = false;
        IsHitTestVisible = false;
        Width = 360;
        SizeToContent = SizeToContent.Height;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 12;

        var row = new DockPanel();
        DockPanel.SetDock(state, Dock.Right);
        row.Children.Add(state);
        row.Children.Add(selection);
        var lines = new StackPanel();
        lines.Children.Add(row);
        lines.Children.Add(playback);
        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(210, 30, 31, 36)),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(12, 8, 12, 8),
            Child = lines
        };

        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            // A layered window needs native transparency to pass clicks to other processes.
            const int extendedStyle = -20;
            const int transparent = 0x20, toolWindow = 0x80, noActivate = 0x08000000;
            SetWindowLongW(handle, extendedStyle, GetWindowLongW(handle, extendedStyle) | transparent | toolWindow | noActivate);
        };
    }

    public void Update(Track? selected, AudioEngine audio)
    {
        selection.Text = selected == null ? "No track selected" : "Selected: " + selected.Name;
        state.Text = audio.CurrentTrackId == null ? "Stopped" : audio.IsPaused ? "Paused" : audio.IsStarting ? "Starting" : "Playing";
        playback.Text = state.Text + ": " + audio.Current;
        playback.Visibility = audio.CurrentTrackId != null && audio.CurrentTrackId != selected?.Id ? Visibility.Visible : Visibility.Collapsed;
    }

    public void ShowAboveWindow(IntPtr window)
    {
        var screen = Forms.Screen.FromHandle(window);
        // Screen bounds use physical pixels; WPF coordinates use device-independent units.
        var handle = new WindowInteropHelper(this).EnsureHandle();
        var source = HwndSource.FromHwnd(handle);
        var bounds = screen.WorkingArea;
        var transform = source.CompositionTarget.TransformFromDevice;
        var origin = transform.Transform(new Point(bounds.Left, bounds.Top));
        var size = transform.Transform(new Point(bounds.Width, bounds.Height));
        Width = Math.Min(360, Math.Max(1, size.X - 24));
        Left = origin.X + (size.X - Width) / 2;
        Top = origin.Y + 12;
        Show();
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLongW(IntPtr window, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLongW(IntPtr window, int index, int value);
}
