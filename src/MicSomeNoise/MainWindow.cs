using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
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

    public MainWindow()
    {
        Style = (Style)FindResource(typeof(Window));
        Title = "Mic Some Noise 0.1.13"; Width = 800; Height = 900; MinWidth = 560; MinHeight = 600;
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
        AddButton(navigation, "Library", ShowLibrary);
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
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        footer.Children.Add(new Border { Height = 1, Background = (Brush)FindResource("Outline"), Margin = new Thickness(0, 12, 0, 12) });
        current.Foreground = (Brush)FindResource("Muted"); current.FontSize = 12; current.TextTrimming = TextTrimming.CharacterEllipsis;
        current.Margin = new Thickness(0, 0, 0, 4); footer.Children.Add(current);
        var volume = new Slider { Minimum = 0, Maximum = 1, Value = store.Settings.TrackVolume, ToolTip = "Track volume", Margin = new Thickness(0, 4, 0, 8) };
        var volumeLabel = new TextBlock { Text = $"Track volume: {volume.Value:P0}" }; footer.Children.Add(volumeLabel); footer.Children.Add(volume);
        volume.ValueChanged += (_, _) =>
        {
            audio.TrackVolume = (float)volume.Value;
            store.Settings.TrackVolume = (float)volume.Value;
            volumeLabel.Text = $"Track volume: {volume.Value:P0}";
            try { store.SaveSettings(); } catch (Exception e) { status.Text = "Could not save volume: " + e.Message; }
        };

        selected.TextWrapping = TextWrapping.NoWrap; selected.TextTrimming = TextTrimming.CharacterEllipsis;
        footer.Children.Insert(2, selected);
        var buttons = new WrapPanel(); footer.Children.Add(buttons);
        AddButton(buttons, "Previous", () => SelectAdjacent(-1));
        playButton = AddButton(buttons, "Play", async () => await PlaySelected());
        playButton.Style = (Style)FindResource("PrimaryButton"); playButton.MinWidth = 100;
        AddButton(buttons, "Next", () => SelectAdjacent(1));
        AddButton(buttons, "Stop", () => audio.Stop());
        var testPanel = new StackPanel();
        var testExpander = new Expander { Header = "Test microphone + track", Content = testPanel, Margin = new Thickness(0, 10, 0, 0) };
        footer.Children.Add(testExpander);
        testPanel.Children.Add(new TextBlock { Text = "Record up to 10 seconds of the mix sent to broadcast. Listen through headphones. This test is before VB-CABLE and Discord processing; live broadcasting continues.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 4) });
        var testState = new TextBlock { TextWrapping = TextWrapping.Wrap };
        testPanel.Children.Add(testState);
        var testButtons = new WrapPanel(); testPanel.Children.Add(testButtons);
        var recordTest = new Button { Content = "Record 10 seconds", Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 4, 6, 4) };
        var finishTest = new Button { Content = "Finish", Padding = recordTest.Padding, Margin = recordTest.Margin };
        var listenTest = new Button { Content = "Listen", Padding = recordTest.Padding, Margin = recordTest.Margin };
        var clearTest = new Button { Content = "Clear", Padding = recordTest.Padding, Margin = recordTest.Margin };
        testButtons.Children.Add(recordTest); testButtons.Children.Add(finishTest); testButtons.Children.Add(listenTest); testButtons.Children.Add(clearTest);
        recordTest.Click += (_, _) => { try { audio.BeginTest(); status.Text = "Test recording started. Speak or play a track."; } catch (Exception e) { status.Text = e.Message; } };
        finishTest.Click += (_, _) => audio.FinishTest();
        listenTest.Click += (_, _) => { try { audio.ListenToTest(); } catch (Exception e) { status.Text = e.Message; } };
        clearTest.Click += (_, _) => audio.ClearTest();
        var testTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        testTimer.Tick += (_, _) =>
        {
            var state = audio.TestStatus;
            recordTest.IsEnabled = audio.Running && !state.Recording && !state.Playing;
            finishTest.IsEnabled = state.Recording;
            listenTest.IsEnabled = audio.Running && state.HasRecording && !state.Recording && !state.Playing;
            clearTest.IsEnabled = state.HasRecording || state.Recording;
            testState.Text = state.Recording ? $"Recording {state.Seconds:0.0} / 10 seconds…" : state.Playing ? $"Listening {state.Seconds:0.0} seconds… Stop ends playback." : state.HasRecording ? $"{state.Seconds:0.0} seconds ready. Listen plays only in your headphones." : "No test recorded. Audio stays in memory until cleared or the app exits.";
        };
        testExpander.Expanded += (_, _) => testTimer.Start();
        testExpander.Collapsed += (_, _) => testTimer.Stop();
        IsVisibleChanged += (_, _) => { if (IsVisible && testExpander.IsExpanded) testTimer.Start(); else testTimer.Stop(); };
        Closed += (_, _) => testTimer.Stop();
        footer.Children.Add(status);
        status.Margin = new Thickness(0, 10, 0, 0);
        status.Foreground = (Brush)FindResource("Muted"); status.FontSize = 12;
        results.ItemTemplate = (DataTemplate)FindResource("TrackRow");
        results.BorderThickness = new Thickness(0); results.Padding = new Thickness(4); root.Children.Add(results);
        search.TextChanged += (_, _) => Refresh();
        results.SelectionChanged += (_, _) =>
        {
            if (results.SelectedItem is not Track track) return;
            if (!refreshing && store.Settings.SelectedTrackId != track.Id) selectionRevision++;
            store.Settings.SelectedTrackId = track.Id;
            UpdateReadyTrack();
            try { store.SaveSettings(); } catch (Exception e) { status.Text = "Could not save the selection: " + e.Message; }
        };


        PreviewKeyDown += async (_, e) =>
        {
            if (e.Key == Key.Escape) { Dismiss(); e.Handled = true; }
            if (e.Key == Key.Enter && search.IsKeyboardFocusWithin && IsUrlInput)
            {
                e.Handled = true;
                await ImportTrack(search.Text.Trim());
                return;
            }
            if (!search.IsKeyboardFocusWithin && !results.IsKeyboardFocusWithin) return;
            if (e.Key is Key.Down or Key.Up)
            {
                results.SelectedIndex = Math.Clamp(results.SelectedIndex + (e.Key == Key.Down ? 1 : -1), 0, Math.Max(0, results.Items.Count - 1));
                if (results.SelectedItem != null) results.ScrollIntoView(results.SelectedItem);
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
        menu.Items.Add("Library", null, (_, _) => ShowLibrary()); menu.Items.Add("Settings", null, (_, _) => ShowSettings());
        menu.Items.Add("Exit", null, (_, _) => Exit()); tray.ContextMenuStrip = menu; tray.DoubleClick += (_, _) => Reveal();
        SourceInitialized += (_, _) =>
        {
            int darkMode = 1;
            DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref darkMode, sizeof(int));
            hotkeys = new Hotkeys(new WindowInteropHelper(this).Handle); hotkeys.Open += Reveal; hotkeys.Play += async () => await PlaySelected(); hotkeys.Next += () => SelectAdjacent(1); hotkeys.Previous += () => SelectAdjacent(-1);
            hotkeys.VolumeDown += () => volume.Value = Math.Clamp(Math.Round(volume.Value - .05, 2), 0, 1);
            hotkeys.VolumeUp += () => volume.Value = Math.Clamp(Math.Round(volume.Value + .05, 2), 0, 1);
            try { hotkeys.Apply(store.Settings.OpenHotkey, store.Settings.PlayHotkey, store.Settings.NextHotkey, store.Settings.VolumeDownHotkey, store.Settings.VolumeUpHotkey, store.Settings.PreviousHotkey); } catch (Exception e) { status.Text = e.Message; }
            if (store.Settings.Cable != null) try { var s = store.Settings; pushToTalk.Configure(s.PushToTalkEnabled, s.PushToTalkEnabled ? Hotkeys.ValidatePushToTalk(s.PushToTalkKey, s.OpenHotkey, s.PlayHotkey, s.NextHotkey, s.VolumeDownHotkey, s.VolumeUpHotkey, s.PreviousHotkey) : (ushort)0); audio.Start(s); } catch (Exception e) { status.Text = e.Message; }
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
        results.SelectedItem = results.Items.Cast<Track>().FirstOrDefault(t => t.Id == selectedId);
        refreshing = false;
        if (results.SelectedItem != null) results.ScrollIntoView(results.SelectedItem);
        libraryCount.Text = (search.Text.Length == 0 || IsUrlInput) ? $"Library · {results.Items.Count} tracks" : $"{results.Items.Count} matches · {store.Tracks.Count} tracks";
        UpdateReadyTrack();
        if (store.Tracks.Count == 0) status.Text = "Import a WAV, MP3, or YouTube video to start.";
    }
    private bool IsUrlInput => search.Text.TrimStart().StartsWith("https://", StringComparison.OrdinalIgnoreCase) || search.Text.TrimStart().StartsWith("http://", StringComparison.OrdinalIgnoreCase);

    private void SelectAdjacent(int direction)
    {
        var tracks = store.Tracks.OrderBy(t => t.Name).ToList();
        if (tracks.Count == 0) return;
        int index = tracks.FindIndex(t => t.Id == store.Settings.SelectedTrackId);
        var track = tracks[index < 0 ? (direction > 0 ? 0 : tracks.Count - 1) : (index + direction + tracks.Count) % tracks.Count];
        selectionRevision++;
        store.Settings.SelectedTrackId = track.Id;
        search.Clear();
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

        try { await audio.TogglePlayback(track); } catch (Exception e) { status.Text = e.Message; if (!IsVisible) tray.ShowBalloonTip(5000, "Playback failed", e.Message, Forms.ToolTipIcon.Warning); }
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
        Label(panel, "Off sends only the track to the game during playback and the push-to-talk delay. Pausing or stopping restores your microphone. Discord stays unchanged when it uses your main mic directly.");
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
                ushort pttCode = automaticPtt.IsChecked == true ? Hotkeys.ValidatePushToTalk(pttKey.Text, open.Text, play.Text, next.Text, volumeDown.Text, volumeUp.Text, previousTrack.Text) : (ushort)0;
                hotkeys!.Apply(open.Text, play.Text, next.Text, volumeDown.Text, volumeUp.Text, previousTrack.Text);
                audio.Stop();
                pushToTalk.Configure(automaticPtt.IsChecked == true, pttCode);
                var s = store.Settings; s.Microphone = (string)mic.SelectedValue; s.Headphones = (string)hp.SelectedValue; s.Cable = (string)cable.SelectedValue;
                s.IncludeMicrophoneDuringPlayback = includeMicrophone.IsChecked == true; s.MicrophoneVolume = (float)gain.Value; s.OpenHotkey = open.Text; s.PlayHotkey = play.Text; s.NextHotkey = next.Text; s.PreviousHotkey = previousTrack.Text; s.VolumeDownHotkey = volumeDown.Text; s.VolumeUpHotkey = volumeUp.Text; s.PushToTalkEnabled = automaticPtt.IsChecked == true; s.PushToTalkKey = pttKey.Text.Trim(); s.PushToTalkDelayMs = delayMs;
                audio.Start(s); store.SaveSettings(); Refresh(); status.Text = "Audio ready. Select a track; use Play or your play hotkey. Escape hides."; window.Close();
            }
            catch (Exception e) { error.Text = e.Message; }
        });
        managementWindow = window;
        try { window.ShowDialog(); } finally { managementWindow = null; }
    }
    private void ShowLibrary()
    {
        if (managementWindow != null) { managementWindow.Activate(); return; }
        var window = Dialog("Library", 680, out var panel);
        Label(panel, "Rename selected track"); var name = new TextBox(); panel.Children.Add(name);
        var library = new ListBox { Height = 250, ItemTemplate = (DataTemplate)FindResource("TrackRow"), ItemsSource = store.Tracks.ToList(), Margin = new Thickness(0, 12, 0, 0) }; panel.Children.Add(library);
        library.SelectionChanged += (_, _) => { if (library.SelectedItem is Track t) name.Text = t.Name; };
        var actions = new WrapPanel(); panel.Children.Add(actions);
        var message = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxHeight = 100 };
        void Reload() { library.ItemsSource = store.Tracks.ToList(); Refresh(); }
        AddButton(actions, "Rename", () =>
        {
            if (library.SelectedItem is not Track t || string.IsNullOrWhiteSpace(name.Text)) return;
            try { int i = store.Tracks.IndexOf(t); store.Tracks[i] = t with { Name = name.Text.Trim() }; try { store.SaveLibrary(); } catch { store.Tracks[i] = t; throw; } Reload(); } catch (Exception e) { message.Text = e.Message; }
        });
        AddButton(actions, "Remove from library", () =>
        {
            if (library.SelectedItem is not Track t) return;
            try { int i = store.Tracks.IndexOf(t); store.Tracks.Remove(t); try { store.SaveLibrary(); } catch { store.Tracks.Insert(i, t); throw; } Reload(); message.Text = "Removed. The copied audio remains in the Audio folder."; } catch (Exception e) { message.Text = e.Message; }
        });
        panel.Children.Add(message);
        managementWindow = window;
        try { window.ShowDialog(); } finally { managementWindow = null; }
    }
    private sealed record Device(string Id, string Name) { public override string ToString() => Name; }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
