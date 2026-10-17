using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using NAudio.CoreAudioApi;
using Forms = System.Windows.Forms;

namespace MicSomeNoise;

public sealed class MainWindow : Window
{
    private readonly Store store = new();
    private readonly AudioEngine audio = new();
    private readonly PushToTalk pushToTalk = new(KeyboardInput.Send);
    private readonly TextBox search = new();
    private readonly ListBox results = new();
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock current = new();
    private readonly TextBlock libraryCount = new();
    private readonly TextBlock selected = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold };
    private readonly Forms.NotifyIcon tray;
    private readonly System.Drawing.Icon trayIcon;
    private readonly Button playButton;
    private Hotkeys? hotkeys;
    private IntPtr previous;
    private CancellationTokenSource? importCancellation;
    private bool exiting;
    private bool refreshing;
    private long selectionRevision;
    private readonly TextBlock importStatus = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button importButton;
    private readonly Button cancelImportButton;
    private Window? managementWindow;
    private int trackColumns = 1;
    private readonly Stack<(Track Track, int Index)> removedTracks = new();
    private readonly TextBlock removalMessage = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button undoButton;
    private readonly StackPanel renamePanel = new() { Visibility = Visibility.Collapsed };
    private readonly TextBox renameInput = new();
    private readonly TextBlock renameError = new() { TextWrapping = TextWrapping.Wrap };
    private Track? renamingTrack;
    private string? pendingTrackId;
    private long playbackRevision;
    private volatile ushort microphoneHoldKey;

    public MainWindow()
    {
        Style = (Style)FindResource(typeof(Window));
        Title = "Mic Some Noise 0.1.13"; Width = 1040; Height = 820; MinWidth = 560; MinHeight = 600;
        MinHeight = Math.Min(MinHeight, SystemParameters.WorkArea.Height);
        MinWidth = Math.Min(MinWidth, SystemParameters.WorkArea.Width);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height);
        Width = Math.Min(Width, SystemParameters.WorkArea.Width);
        var iconUri = new Uri("pack://application:,,,/Assets/AppIcon.ico");
        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(iconUri);
        using (var iconStream = Application.GetResourceStream(iconUri).Stream)
            trayIcon = new System.Drawing.Icon(iconStream, Forms.SystemInformation.SmallIconSize);
        WindowStartupLocation = WindowStartupLocation.CenterScreen; Topmost = true;
        FontFamily = new FontFamily("Segoe UI"); FontSize = 14;
        var root = new DockPanel { Margin = new Thickness(24) }; Content = root;
        var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 18) };
        var navigation = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(navigation, Dock.Right); heading.Children.Add(navigation);
        AddButton(navigation, "Settings", ShowSettings);
        heading.Children.Add(new TextBlock { Text = "Mic Some Noise", FontSize = 24, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        header.Children.Add(heading);
        search.Margin = new Thickness(0, 0, 0, 12); search.Padding = new Thickness(10); search.ToolTip = "Search tracks or paste a YouTube link";
        System.Windows.Automation.AutomationProperties.SetName(search, "Search tracks or paste a YouTube link");
        var searchContainer = new Grid(); searchContainer.Children.Add(search);
        var searchHint = new TextBlock { Text = "Search tracks or paste a YouTube link…", Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(11, 11, 11, 0), IsHitTestVisible = false };
        searchContainer.Children.Add(searchHint);
        search.TextChanged += (_, _) => searchHint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        header.Children.Add(searchContainer);
        var importActions = new WrapPanel(); header.Children.Add(importActions);
        importButton = AddButton(importActions, "Import YouTube · Enter", async () => await ImportTrack(search.Text.Trim()));
        importButton.Visibility = Visibility.Collapsed;
        AddButton(importActions, "Import audio file", async () =>
        {
            var picker = new Microsoft.Win32.OpenFileDialog { Filter = "Audio|*.wav;*.mp3" };
            if (picker.ShowDialog(this) == true) await ImportTrack(null, picker.FileName);
        });
        cancelImportButton = AddButton(importActions, "Cancel import", () => importCancellation?.Cancel());
        cancelImportButton.Visibility = Visibility.Collapsed;
        importStatus.Margin = new Thickness(0, 4, 0, 8);
        header.Children.Add(new ScrollViewer { Content = importStatus, MaxHeight = 90, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        libraryCount.Foreground = (Brush)FindResource("Muted"); libraryCount.FontSize = 12; libraryCount.Margin = new Thickness(2, 0, 0, 8);
        header.Children.Add(libraryCount);
        var removalNotice = new DockPanel { LastChildFill = true };
        undoButton = new Button { Content = "Undo", IsEnabled = false, Visibility = Visibility.Collapsed, Margin = new Thickness(8, 0, 0, 8) };
        undoButton.Click += (_, _) => UndoRemoval();
        DockPanel.SetDock(undoButton, Dock.Right); removalNotice.Children.Add(undoButton);
        removalNotice.Children.Add(removalMessage); header.Children.Add(removalNotice);
        Label(renamePanel, "Rename track"); renamePanel.Children.Add(renameInput);
        var renameActions = new WrapPanel(); renamePanel.Children.Add(renameActions);
        AddButton(renameActions, "Save name", SaveTrackName);
        AddButton(renameActions, "Cancel", () => { renamingTrack = null; renamePanel.Visibility = Visibility.Collapsed; FocusSelectedTrack(); });
        renamePanel.Children.Add(renameError); header.Children.Add(renamePanel);
        renameInput.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; SaveTrackName(); }
            if (e.Key == Key.Escape) { e.Handled = true; renamingTrack = null; renamePanel.Visibility = Visibility.Collapsed; FocusSelectedTrack(); }
        };
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        footer.Children.Add(new Border { Height = 1, Background = (Brush)FindResource("Outline"), Margin = new Thickness(0, 12, 0, 12) });
        current.Foreground = (Brush)FindResource("Muted"); current.FontSize = 12; current.TextTrimming = TextTrimming.CharacterEllipsis;
        current.Margin = new Thickness(0, 0, 0, 4); footer.Children.Add(current);
        selected.TextWrapping = TextWrapping.NoWrap; selected.TextTrimming = TextTrimming.CharacterEllipsis;
        footer.Children.Add(selected);
        var controls = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 6, 0, 0) }; footer.Children.Add(controls);
        var buttons = new WrapPanel(); DockPanel.SetDock(buttons, Dock.Left); controls.Children.Add(buttons);
        AddButton(buttons, "Previous", () => SelectAdjacent(-1));
        playButton = AddButton(buttons, "Play", async () => await PlaySelected());
        playButton.Style = (Style)FindResource("PrimaryButton"); playButton.MinWidth = 80;
        AddButton(buttons, "Next", () => SelectAdjacent(1));
        AddButton(buttons, "Stop", () => audio.Stop());
        var volumeControls = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }; controls.Children.Add(volumeControls);
        var volume = new Slider { Minimum = 0, Maximum = 1, Value = store.Settings.TrackVolume, ToolTip = "Track volume" };
        System.Windows.Automation.AutomationProperties.SetName(volume, "Track volume");
        var volumeLabel = new TextBlock { Text = $"Volume: {volume.Value:P0}", FontSize = 12 }; volumeControls.Children.Add(volumeLabel); volumeControls.Children.Add(volume);
        volume.ValueChanged += (_, _) =>
        {
            audio.TrackVolume = (float)volume.Value;
            store.Settings.TrackVolume = (float)volume.Value;
            volumeLabel.Text = $"Volume: {volume.Value:P0}";
            try { store.SaveSettings(); } catch (Exception e) { status.Text = "Could not save volume: " + e.Message; }
        };

        footer.Children.Add(status);
        status.Margin = new Thickness(0, 10, 0, 0);
        status.Foreground = (Brush)FindResource("Muted"); status.FontSize = 12;
        results.ItemTemplate = (DataTemplate)FindResource("TrackRow");
        var trackPanel = new FrameworkElementFactory(typeof(UniformGrid));
        trackPanel.SetValue(UniformGrid.ColumnsProperty, 1);
        trackPanel.SetValue(VerticalAlignmentProperty, VerticalAlignment.Top);
        results.ItemsPanel = new ItemsPanelTemplate(trackPanel);
        results.SizeChanged += (_, _) =>
        {
            int columns = results.ActualWidth >= 800 ? 2 : 1;
            if (columns == trackColumns) return;
            trackColumns = columns;
            var itemsPanel = new FrameworkElementFactory(typeof(UniformGrid));
            itemsPanel.SetValue(UniformGrid.ColumnsProperty, columns);
            itemsPanel.SetValue(VerticalAlignmentProperty, VerticalAlignment.Top);
            results.ItemsPanel = new ItemsPanelTemplate(itemsPanel);
            if (results.SelectedItem != null) results.Dispatcher.BeginInvoke(() => results.ScrollIntoView(results.SelectedItem));
        };
        results.AddHandler(Button.ClickEvent, new RoutedEventHandler((_, e) =>
        {
            if (e.OriginalSource is not Button { DataContext: Track track } button) return;
            results.SelectedItem = track;
            var menu = new ContextMenu();
            var rename = new MenuItem { Header = "Rename", InputGestureText = "F2" };
            rename.Click += (_, _) => RenameTrack(track); menu.Items.Add(rename);
            var remove = new MenuItem { Header = "Remove from library", InputGestureText = "Delete" };
            remove.Click += (_, _) => RemoveTrack(track); menu.Items.Add(remove);
            button.ContextMenu = menu; menu.PlacementTarget = button; menu.Placement = PlacementMode.Bottom; menu.IsOpen = true;
            e.Handled = true;
        }));
        results.BorderThickness = new Thickness(0); results.Padding = new Thickness(4); root.Children.Add(results);
        ScrollViewer.SetCanContentScroll(results, false);
        search.TextChanged += (_, _) => { selectionRevision++; Refresh(); };
        results.SelectionChanged += (_, _) =>
        {
            if (refreshing || results.SelectedItem is not Track track) return;
            if (store.Settings.SelectedTrackId != track.Id) selectionRevision++;
            store.Settings.SelectedTrackId = track.Id;
            UpdateReadyTrack();
            try { store.SaveSettings(); } catch (Exception e) { status.Text = "Could not save the selection: " + e.Message; }
        };


        PreviewKeyDown += async (_, e) =>
        {
            if (renamePanel.IsKeyboardFocusWithin) return;
            if (e.Key == Key.Escape) { Dismiss(); e.Handled = true; }
            if (e.Key == Key.Enter && search.IsKeyboardFocusWithin && IsUrlInput)
            {
                e.Handled = true;
                await ImportTrack(search.Text.Trim());
                return;
            }
            if (!search.IsKeyboardFocusWithin && !results.IsKeyboardFocusWithin) return;
            if (e.Key == Key.Enter)
            {
                if (Keyboard.FocusedElement is Button) return;
                e.Handled = true;
                if (results.SelectedItem is Track) await PlaySelected();
                return;
            }
            if (results.IsKeyboardFocusWithin && results.SelectedItem is Track track)
            {
                if (e.Key == Key.F2) { e.Handled = true; RenameTrack(track); return; }
                if (e.Key == Key.Delete) { e.Handled = true; RemoveTrack(track); return; }
            }
            if (e.Key is Key.Down or Key.Up || (results.IsKeyboardFocusWithin && e.Key is Key.Left or Key.Right))
            {
                if (search.IsKeyboardFocusWithin)
                {
                    if (results.SelectedItem != null) FocusSelectedTrack();
                    e.Handled = true;
                    return;
                }
                int step = e.Key is Key.Down or Key.Up ? trackColumns : 1;
                if (e.Key is Key.Up or Key.Left) step = -step;
                results.SelectedIndex = Math.Clamp(results.SelectedIndex < 0 ? 0 : results.SelectedIndex + step, 0, Math.Max(0, results.Items.Count - 1));
                FocusSelectedTrack();
                e.Handled = true;
            }
        };
        audio.TrackStarting += pushToTalk.Hold;
        audio.TrackStopped += pushToTalk.Release;
        audio.TrackFinished += pushToTalk.ReleaseAfterPlayback;
        pushToTalk.Error += message => Dispatcher.BeginInvoke(() => { status.Text = message; tray?.ShowBalloonTip(5000, "Push-to-talk needs attention", message, Forms.ToolTipIcon.Warning); });
        audio.Changed += () => Dispatcher.BeginInvoke(UpdateReadyTrack);
        audio.Error += message => Dispatcher.BeginInvoke(() => { audio.Stop(); status.Text = message; tray?.ShowBalloonTip(5000, "Audio needs attention", message, Forms.ToolTipIcon.Warning); });
        tray = new Forms.NotifyIcon { Icon = trayIcon, Text = "Mic Some Noise", Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => Reveal()); menu.Items.Add("Stop track", null, (_, _) => audio.Stop());
        menu.Items.Add("Settings", null, (_, _) => ShowSettings());
        menu.Items.Add("Exit", null, (_, _) => Exit()); tray.ContextMenuStrip = menu; tray.DoubleClick += (_, _) => Reveal();
        SourceInitialized += (_, _) =>
        {
            int darkMode = 1;
            DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref darkMode, sizeof(int));
            hotkeys = new Hotkeys(new WindowInteropHelper(this).Handle); hotkeys.Open += Reveal; hotkeys.Play += async () => await PlaySelected(); hotkeys.Next += () => SelectAdjacent(1); hotkeys.Previous += () => SelectAdjacent(-1);
            hotkeys.VolumeDown += () => volume.Value = Math.Clamp(Math.Round(volume.Value - .05, 2), 0, 1);
            hotkeys.VolumeUp += () => volume.Value = Math.Clamp(Math.Round(volume.Value + .05, 2), 0, 1);
            audio.MicrophoneHoldPressed = () => microphoneHoldKey != 0 && KeyboardInput.IsDown(microphoneHoldKey);
            var s = store.Settings;
            string[] shortcuts = [s.OpenHotkey, s.PlayHotkey, s.NextHotkey, s.VolumeDownHotkey, s.VolumeUpHotkey, s.PreviousHotkey];
            try { hotkeys.Apply(s.OpenHotkey, s.PlayHotkey, s.NextHotkey, s.VolumeDownHotkey, s.VolumeUpHotkey, s.PreviousHotkey); }
            catch (Exception e) { status.Text = e.Message; }
            try
            {
                string[] holdConflicts = s.PushToTalkEnabled ? [.. shortcuts, s.PushToTalkKey, "F2"] : [.. shortcuts, "F2"];
                microphoneHoldKey = string.IsNullOrWhiteSpace(s.MicrophoneHoldKey) ? (ushort)0 : Hotkeys.ValidateHoldKey(s.MicrophoneHoldKey, "Microphone hold", holdConflicts);
            }
            catch (Exception e) { status.Text = "Microphone hold disabled. Change its key in Settings. " + e.Message; }
            if (s.Cable != null) try
            {
                ushort pttCode = s.PushToTalkEnabled ? Hotkeys.ValidateHoldKey(s.PushToTalkKey, "Push-to-talk", shortcuts) : (ushort)0;
                pushToTalk.Configure(s.PushToTalkEnabled, pttCode);
                audio.Start(s);
            }
            catch (Exception e) { status.Text = e.Message; }
        };
        Loaded += (_, _) => { search.Focus(); if (store.Settings.Cable == null) ShowSettings(); };
        Closing += (_, e) => { if (!exiting) { e.Cancel = true; Dismiss(); } };
        current.Text = "No track playing"; Refresh();
    }
    private static Button AddButton(Panel panel, string text, Action action)
    {
        var button = new Button { Content = text, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 4, 8, 4) };
        button.Click += (_, _) => action(); panel.Children.Add(button); return button;
    }
    private void Refresh()
    {
        refreshing = true;
        importButton.Visibility = IsUrlInput ? Visibility.Visible : Visibility.Collapsed;
        importButton.IsEnabled = importCancellation == null;
        string? selectedId = store.Settings.SelectedTrackId;
        results.ItemsSource = store.Tracks.Where(t => IsUrlInput || t.Name.Contains(search.Text, StringComparison.OrdinalIgnoreCase)).OrderBy(t => t.Name).ToList();
        results.SelectedItem = results.Items.Cast<Track>().FirstOrDefault(t => t.Id == selectedId) ?? results.Items.Cast<Track>().FirstOrDefault();
        refreshing = false;
        if (store.Tracks.Count == 0) status.Text = "Import a WAV, MP3, or YouTube video to start.";
        else if (results.Items.Count == 0) status.Text = "No matching tracks. Change or clear the search.";
        else status.Text = "Arrows select · Enter plays / pauses · F2 renames · Delete removes";
        store.Settings.SelectedTrackId = (results.SelectedItem as Track)?.Id;
        if (store.Settings.SelectedTrackId != selectedId)
        {
            selectionRevision++;
            try { store.SaveSettings(); } catch (Exception e) { status.Text = "Could not save the selection: " + e.Message; }
        }
        if (results.SelectedItem != null) results.ScrollIntoView(results.SelectedItem);
        libraryCount.Text = (search.Text.Length == 0 || IsUrlInput) ? $"Library · {results.Items.Count} tracks" : $"{results.Items.Count} matches · {store.Tracks.Count} tracks";
        UpdateReadyTrack();
    }
    private bool IsUrlInput => search.Text.TrimStart().StartsWith("https://", StringComparison.OrdinalIgnoreCase) || search.Text.TrimStart().StartsWith("http://", StringComparison.OrdinalIgnoreCase);

    private void FocusSelectedTrack()
    {
        if (results.SelectedItem == null) { search.Focus(); return; }
        results.ScrollIntoView(results.SelectedItem);
        results.UpdateLayout();
        if (results.ItemContainerGenerator.ContainerFromItem(results.SelectedItem) is ListBoxItem item) item.Focus();
    }

    private void SelectAdjacent(int direction)
    {
        var tracks = results.Items.Cast<Track>().ToList();
        if (tracks.Count == 0) return;
        int index = tracks.FindIndex(t => t.Id == store.Settings.SelectedTrackId);
        var track = tracks[index < 0 ? (direction > 0 ? 0 : tracks.Count - 1) : (index + direction + tracks.Count) % tracks.Count];
        selectionRevision++;
        store.Settings.SelectedTrackId = track.Id;
        Refresh();
        try { store.SaveSettings(); } catch (Exception e) { status.Text = "Could not save the selection: " + e.Message; }
    }

    private void RenameTrack(Track track)
    {
        renamingTrack = track;
        renameInput.Text = track.Name;
        renameError.Text = "";
        renamePanel.Visibility = Visibility.Visible;
        renameInput.Focus(); renameInput.SelectAll();
    }

    private void SaveTrackName()
    {
        if (renamingTrack is not Track track) return;
        if (string.IsNullOrWhiteSpace(renameInput.Text)) { renameError.Text = "Enter a track name."; return; }
        int index = store.Tracks.FindIndex(t => t.Id == track.Id);
        if (index < 0) { renameError.Text = "This track is no longer in the library."; return; }
        var original = store.Tracks[index];
        store.Tracks[index] = original with { Name = renameInput.Text.Trim() };
        try { store.SaveLibrary(); }
        catch (Exception e) { store.Tracks[index] = original; renameError.Text = "Could not rename track: " + e.Message; return; }
        renamingTrack = null; renamePanel.Visibility = Visibility.Collapsed;
        Refresh(); FocusSelectedTrack();
    }

    private void RemoveTrack(Track track)
    {
        int index = store.Tracks.IndexOf(track);
        if (index < 0) return;
        int selectedIndex = results.SelectedIndex;
        store.Tracks.RemoveAt(index);
        try { store.SaveLibrary(); }
        catch (Exception e) { store.Tracks.Insert(index, track); status.Text = "Could not remove track: " + e.Message; return; }
        removedTracks.Push((track, index));
        undoButton.IsEnabled = true; undoButton.Visibility = Visibility.Visible;
        undoButton.ToolTip = $"Restore {track.Name}";
        removalMessage.Text = $"Removed: {track.Name}. Audio file kept.";
        if (renamingTrack?.Id == track.Id) { renamingTrack = null; renamePanel.Visibility = Visibility.Collapsed; }
        if (audio.CurrentTrackId == track.Id || pendingTrackId == track.Id) audio.Stop();
        selectionRevision++;
        Refresh();
        if (results.Items.Count > 0) results.SelectedIndex = Math.Clamp(selectedIndex, 0, results.Items.Count - 1);
        FocusSelectedTrack();
    }

    private void UndoRemoval()
    {
        if (!removedTracks.TryPeek(out var removed)) return;
        if (!store.Tracks.Any(t => t.Id == removed.Track.Id))
        {
            int index = Math.Min(removed.Index, store.Tracks.Count);
            store.Tracks.Insert(index, removed.Track);
            try { store.SaveLibrary(); }
            catch (Exception e) { store.Tracks.RemoveAt(index); status.Text = "Could not restore track: " + e.Message; return; }
        }
        removedTracks.Pop();
        undoButton.IsEnabled = removedTracks.Count > 0;
        undoButton.Visibility = removedTracks.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        undoButton.ToolTip = removedTracks.TryPeek(out var next) ? $"Restore {next.Track.Name}" : null;
        removalMessage.Text = $"Restored: {removed.Track.Name}.";
        if (IsUrlInput || removed.Track.Name.Contains(search.Text, StringComparison.OrdinalIgnoreCase))
            store.Settings.SelectedTrackId = removed.Track.Id;
        selectionRevision++;
        Refresh();
        try { store.SaveSettings(); } catch (Exception e) { status.Text = "Could not save the selection: " + e.Message; }
    }

    private async Task ImportTrack(string? url, string? file = null)
    {
        if (importCancellation != null) { importStatus.Text = "An import is already running. Esc returns to your game."; return; }
        using var cancellation = new CancellationTokenSource();
        importCancellation = cancellation;
        long revision = selectionRevision;
        cancelImportButton.Visibility = Visibility.Visible;
        Refresh();
        try
        {
            importStatus.Text = "Importing… Esc returns to your game. The download will continue.";
            var importer = new Importer(store);
            var progress = new Progress<string>(text =>
            {
                if (importCancellation == cancellation)
                    importStatus.Text = text.StartsWith("[download]", StringComparison.Ordinal) ? text + " · Esc returns to your game." : "Preparing audio… Esc returns to your game.";
            });
            Track track = url != null ? await importer.Youtube(url, "", progress, cancellation.Token) : await importer.Local(file!, "", cancellation.Token);
            bool select = revision == selectionRevision;
            if (select)
            {
                store.Settings.SelectedTrackId = track.Id;
                search.Clear();
                store.SaveSettings();
            }
            Refresh();
            importStatus.Text = select ? $"Ready: {track.Name} · {store.Settings.PlayHotkey} to play." : $"Imported: {track.Name}. Your selection is unchanged.";
            if (!IsVisible) tray.ShowBalloonTip(4000, "Track ready", importStatus.Text, Forms.ToolTipIcon.Info);
        }
        catch (OperationCanceledException) { importStatus.Text = "Import cancelled."; }
        catch (Exception e)
        {
            importStatus.Text = "Import failed: " + e.Message;
            if (!IsVisible) tray.ShowBalloonTip(5000, "Import failed", e.Message, Forms.ToolTipIcon.Warning);
        }
        finally
        {
            importCancellation = null;
            cancelImportButton.Visibility = Visibility.Collapsed;
            Refresh();
        }
    }

    private void UpdateReadyTrack()
    {
        var ready = store.Tracks.FirstOrDefault(t => t.Id == store.Settings.SelectedTrackId);
        if (ready == null) store.Settings.SelectedTrackId = null;
        string action = ready != null && ready.Id == audio.CurrentTrackId ? (audio.IsPaused ? "Resume" : "Pause") : "Play";
        playButton.Content = action;
        playButton.IsEnabled = ready != null;
        selected.Text = ready == null ? "Ready: select a track" : $"Ready: {ready.Name} · {store.Settings.PlayHotkey} to {action.ToLowerInvariant()}";
        selected.ToolTip = selected.Text;
        current.Text = audio.Current == null ? "No track playing" : (audio.IsPaused ? "Paused: " : audio.IsStarting ? "Resuming: " : "Playing: ") + audio.Current;

    }
    private async Task PlaySelected()
    {
        var track = store.Tracks.FirstOrDefault(t => t.Id == store.Settings.SelectedTrackId);
        if (track == null)
        {
            status.Text = "Select a track to play.";
            if (!IsVisible) tray.ShowBalloonTip(3000, "No track selected", status.Text, Forms.ToolTipIcon.Info);
            return;
        }

        long revision = ++playbackRevision;
        pendingTrackId = track.Id;
        try { await audio.TogglePlayback(track); }
        catch (Exception e) { status.Text = e.Message; if (!IsVisible) tray.ShowBalloonTip(5000, "Playback failed", e.Message, Forms.ToolTipIcon.Warning); }
        finally { if (revision == playbackRevision) pendingTrackId = null; }
    }
    private void Reveal() { var foreground = Hotkeys.GetForegroundWindow(); if (foreground != new WindowInteropHelper(this).Handle) previous = foreground; Show(); WindowState = WindowState.Normal; Activate(); search.Focus(); search.SelectAll(); }
    private void Dismiss() { try { store.SaveSettings(); } catch (Exception e) { status.Text = e.Message; return; } Hide(); if (previous != IntPtr.Zero) Hotkeys.SetForegroundWindow(previous); }
    private void Exit() { if (importCancellation != null) { MessageBox.Show("Cancel the import and wait for it to finish before exiting."); return; } pushToTalk.Release(); exiting = true; store.SaveSettings(); hotkeys?.Dispose(); tray.Dispose(); trayIcon.Dispose(); audio.Dispose(); pushToTalk.Dispose(); Application.Current.Shutdown(); }
    private Window Dialog(string title, int height, out StackPanel panel)
    {
        var window = new Window { Owner = this, Title = title, Width = 600, Height = height, MinWidth = 480, MinHeight = 400, FontFamily = FontFamily, FontSize = FontSize, WindowStartupLocation = WindowStartupLocation.CenterScreen };
        window.SourceInitialized += (_, _) =>
        {
            int darkMode = 1;
            DwmSetWindowAttribute(new WindowInteropHelper(window).Handle, 20, ref darkMode, sizeof(int));
        };
        panel = new StackPanel { Margin = new Thickness(24) }; window.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        return window;
    }
    private static void Label(Panel panel, string text) => panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 5) });
    private void ShowSettings()
    {
        if (managementWindow != null) { managementWindow.Activate(); return; }
        var window = Dialog("Audio and shortcuts", 700, out var panel);
        Label(panel, "Choose physical headphones and microphone. Install VB-CABLE, then select CABLE Input for broadcast. In voice chat, select CABLE Output as the input device.");
        using var enumerator = new MMDeviceEnumerator();
        Device[] List(DataFlow flow) => enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active).Select(d => { var item = new Device(d.ID, d.FriendlyName); d.Dispose(); return item; }).ToArray();
        ComboBox Devices(string label, Device[] items, string? selected)
        {
            Label(panel, label); var box = new ComboBox { ItemsSource = items, SelectedValuePath = nameof(Device.Id), SelectedValue = selected, MinHeight = 30 }; panel.Children.Add(box); return box;
        }
        var mic = Devices("Physical microphone", List(DataFlow.Capture), store.Settings.Microphone);
        var outputs = List(DataFlow.Render);
        var hp = Devices("Headphones", outputs, store.Settings.Headphones);
        var cable = Devices("Broadcast output: CABLE Input", outputs, store.Settings.Cable);
        var includeMicrophone = new CheckBox { Content = "Include my microphone while a track plays", IsChecked = store.Settings.IncludeMicrophoneDuringPlayback, Margin = new Thickness(0, 12, 0, 5) }; panel.Children.Add(includeMicrophone);
        Label(panel, "Off sends only the track during playback unless you hold the microphone shortcut below. Pausing or stopping restores your microphone. Discord stays unchanged when it uses your main mic directly.");
        Label(panel, "Hold to speak over a track"); var microphoneHold = new TextBox { Text = store.Settings.MicrophoneHoldKey }; panel.Children.Add(microphoneHold);
        Label(panel, "Hold F6 by default to mix your microphone with the track. Release it to return to track-only audio. Choose a single key different from your other shortcuts, F2, and game push-to-talk, or leave blank to disable. Your microphone must be unmuted in Windows and on the device. The game's push-to-talk rules still apply.");
        Label(panel, "Microphone volume"); var gain = new Slider { Minimum = 0, Maximum = 2, Value = store.Settings.MicrophoneVolume }; panel.Children.Add(gain);
        Label(panel, "Open shortcut"); var open = new TextBox { Text = store.Settings.OpenHotkey }; panel.Children.Add(open);
        Label(panel, "Play / pause shortcut"); var play = new TextBox { Text = store.Settings.PlayHotkey }; panel.Children.Add(play);
        Label(panel, "Previous track shortcut"); var previousTrack = new TextBox { Text = store.Settings.PreviousHotkey }; panel.Children.Add(previousTrack);
        Label(panel, "Next track shortcut"); var next = new TextBox { Text = store.Settings.NextHotkey }; panel.Children.Add(next);
        Label(panel, "Track volume down shortcut"); var volumeDown = new TextBox { Text = store.Settings.VolumeDownHotkey }; panel.Children.Add(volumeDown);
        Label(panel, "Track volume up shortcut"); var volumeUp = new TextBox { Text = store.Settings.VolumeUpHotkey }; panel.Children.Add(volumeUp);
        Label(panel, "Use a single key such as F8 or F9, or modifiers plus a key such as Ctrl+Alt+Space. If push-to-talk holds a modifier, include it in the shortcut. Apply again after a device disconnect or sleep.");
        var automaticPtt = new CheckBox { Content = "Hold push-to-talk while a track plays", IsChecked = store.Settings.PushToTalkEnabled, Margin = new Thickness(0, 12, 0, 5) }; panel.Children.Add(automaticPtt);
        Label(panel, "Game push-to-talk key"); var pttKey = new TextBox { Text = store.Settings.PushToTalkKey, IsEnabled = automaticPtt.IsChecked == true }; panel.Children.Add(pttKey);
        Label(panel, "Delay before track starts (milliseconds, 0–2000)"); var pttDelay = new TextBox { Text = store.Settings.PushToTalkDelayMs.ToString(), IsEnabled = automaticPtt.IsChecked == true }; panel.Children.Add(pttDelay);
        automaticPtt.Checked += (_, _) => { pttKey.IsEnabled = true; pttDelay.IsEnabled = true; };
        automaticPtt.Unchecked += (_, _) => { pttKey.IsEnabled = false; pttDelay.IsEnabled = false; };
        Label(panel, "Use the same single key as the game, for example V. Return to the game before pressing F8. Track end or Stop releases it. Some games reject simulated input. Test recordings do not hold the key.");
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(error);
        AddButton(panel, "Apply and start audio", () =>
        {
            try
            {
                if (mic.SelectedValue == null || hp.SelectedValue == null || cable.SelectedValue == null) throw new InvalidOperationException("Select all three audio devices.");
                if (!int.TryParse(pttDelay.Text, out int delayMs) || delayMs < 0 || delayMs > 2000) throw new InvalidOperationException("Enter a push-to-talk delay from 0 to 2000 milliseconds.");
                string[] shortcuts = [open.Text, play.Text, next.Text, volumeDown.Text, volumeUp.Text, previousTrack.Text];
                ushort pttCode = automaticPtt.IsChecked == true ? Hotkeys.ValidateHoldKey(pttKey.Text, "Push-to-talk", shortcuts) : (ushort)0;
                string[] holdConflicts = automaticPtt.IsChecked == true ? [.. shortcuts, pttKey.Text, "F2"] : [.. shortcuts, "F2"];
                ushort holdCode = string.IsNullOrWhiteSpace(microphoneHold.Text) ? (ushort)0 : Hotkeys.ValidateHoldKey(microphoneHold.Text, "Microphone hold", holdConflicts);
                hotkeys!.Apply(open.Text, play.Text, next.Text, volumeDown.Text, volumeUp.Text, previousTrack.Text);
                audio.Stop();
                pushToTalk.Configure(automaticPtt.IsChecked == true, pttCode);
                microphoneHoldKey = holdCode;
                var s = store.Settings; s.Microphone = (string)mic.SelectedValue; s.Headphones = (string)hp.SelectedValue; s.Cable = (string)cable.SelectedValue;
                s.MicrophoneHoldKey = microphoneHold.Text.Trim(); s.IncludeMicrophoneDuringPlayback = includeMicrophone.IsChecked == true; s.MicrophoneVolume = (float)gain.Value; s.OpenHotkey = open.Text; s.PlayHotkey = play.Text; s.NextHotkey = next.Text; s.PreviousHotkey = previousTrack.Text; s.VolumeDownHotkey = volumeDown.Text; s.VolumeUpHotkey = volumeUp.Text; s.PushToTalkEnabled = automaticPtt.IsChecked == true; s.PushToTalkKey = pttKey.Text.Trim(); s.PushToTalkDelayMs = delayMs;
                audio.Start(s); store.SaveSettings(); Refresh(); error.Text = "Audio ready. You can test the microphone and selected track below.";
            }
            catch (Exception e) { error.Text = e.Message; }
        });
        AddButton(panel, "Close", () => window.Close());
        var testPanel = new StackPanel();
        var testExpander = new Expander { Header = "Test microphone + track", Content = testPanel, Margin = new Thickness(0, 10, 0, 0) };
        panel.Children.Add(testExpander);
        testPanel.Children.Add(new TextBlock { Text = "Record up to 10 seconds of the mix sent to broadcast. Listen through headphones. This test is before VB-CABLE and Discord processing; live broadcasting continues.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 4) });
        var testState = new TextBlock { TextWrapping = TextWrapping.Wrap };
        testPanel.Children.Add(testState);
        var testButtons = new WrapPanel(); testPanel.Children.Add(testButtons);
        var recordTest = new Button { Content = "Record 10 seconds", Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 4, 6, 4) };
        var finishTest = new Button { Content = "Finish", Padding = recordTest.Padding, Margin = recordTest.Margin };
        var listenTest = new Button { Content = "Listen", Padding = recordTest.Padding, Margin = recordTest.Margin };
        var clearTest = new Button { Content = "Clear", Padding = recordTest.Padding, Margin = recordTest.Margin };
        testButtons.Children.Add(recordTest); testButtons.Children.Add(finishTest); testButtons.Children.Add(listenTest); testButtons.Children.Add(clearTest);
        recordTest.Click += (_, _) => { try { audio.BeginTest(); error.Text = "Test recording started. Speak or play a track."; } catch (Exception e) { error.Text = e.Message; } };
        finishTest.Click += (_, _) => audio.FinishTest();
        listenTest.Click += (_, _) => { try { audio.ListenToTest(); } catch (Exception e) { error.Text = e.Message; } };
        clearTest.Click += (_, _) => audio.ClearTest();
        AddButton(testButtons, "Play / pause selected track", async () => await PlaySelected());
        AddButton(testButtons, "Stop", () => audio.Stop());
        var testTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        void UpdateTestState()
        {
            var state = audio.TestStatus;
            recordTest.IsEnabled = audio.Running && !state.Recording && !state.Playing;
            finishTest.IsEnabled = state.Recording;
            listenTest.IsEnabled = audio.Running && state.HasRecording && !state.Recording && !state.Playing;
            clearTest.IsEnabled = state.HasRecording || state.Recording;
            testState.Text = state.Recording ? $"Recording {state.Seconds:0.0} / 10 seconds…" : state.Playing ? $"Listening {state.Seconds:0.0} seconds… Stop ends playback." : state.HasRecording ? $"{state.Seconds:0.0} seconds ready. Listen plays only in your headphones." : "No test recorded. Audio stays in memory until cleared or the app exits.";
        }
        testTimer.Tick += (_, _) => UpdateTestState();
        UpdateTestState();
        testExpander.Expanded += (_, _) => testTimer.Start();
        testExpander.Collapsed += (_, _) => testTimer.Stop();

        window.Closed += (_, _) => { testTimer.Stop(); audio.FinishTest(); };
        managementWindow = window;
        try { window.ShowDialog(); } finally { managementWindow = null; }
    }
    private sealed record Device(string Id, string Name) { public override string ToString() => Name; }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
