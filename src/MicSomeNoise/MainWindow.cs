using System;
using System.ComponentModel;
using System.Linq;
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
    private readonly TextBlock selected = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold };
    private readonly Forms.NotifyIcon tray;
    private readonly Button playButton;
    private Hotkeys? hotkeys;
    private IntPtr previous;
    private CancellationTokenSource? importCancellation;
    private bool exiting;
    private Window? managementWindow;

    public MainWindow()
    {
        Title = "Mic Some Noise 0.1.12"; Width = 540; Height = 620; MinWidth = 440; MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; Topmost = true;
        FontFamily = new FontFamily("Segoe UI"); FontSize = 14;
        Background = new SolidColorBrush(Color.FromRgb(245, 246, 248));
        var root = new DockPanel { Margin = new Thickness(24) }; Content = root;
        var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        header.Children.Add(new TextBlock { Text = "Mic Some Noise", FontSize = 25, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 16) });
        search.Margin = new Thickness(0, 0, 0, 12); search.Padding = new Thickness(10); search.ToolTip = "Search your local library";
        System.Windows.Automation.AutomationProperties.SetName(search, "Search sounds");
        header.Children.Add(search);
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        current.Margin = new Thickness(0, 14, 0, 4); footer.Children.Add(current);
        var volume = new Slider { Minimum = 0, Maximum = 1, Value = store.Settings.TrackVolume, ToolTip = "Track volume", Margin = new Thickness(0, 4, 0, 8) };
        var volumeLabel = new TextBlock { Text = $"Track volume: {volume.Value:P0}" }; footer.Children.Add(volumeLabel); footer.Children.Add(volume);
        volume.ValueChanged += (_, _) =>
        {
            audio.TrackVolume = (float)volume.Value;
            store.Settings.TrackVolume = (float)volume.Value;
            volumeLabel.Text = $"Track volume: {volume.Value:P0}";
            try { store.SaveSettings(); } catch (Exception e) { status.Text = "Could not save volume: " + e.Message; }
        };

        footer.Children.Add(selected);
        var buttons = new WrapPanel(); footer.Children.Add(buttons);
        playButton = AddButton(buttons, "Play", async () => await PlaySelected());
        AddButton(buttons, "Stop", () => audio.Stop());
        AddButton(buttons, "Import / library", ShowLibrary);
        AddButton(buttons, "Settings", ShowSettings);
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
        results.BorderThickness = new Thickness(0); results.Padding = new Thickness(4); root.Children.Add(results);
        search.TextChanged += (_, _) => Refresh();
        results.SelectionChanged += (_, _) =>
        {
            if (results.SelectedItem is not Track track) return;
            store.Settings.SelectedTrackId = track.Id;
            UpdateReadyTrack();
            try { store.SaveSettings(); } catch (Exception e) { status.Text = "Could not save the selection: " + e.Message; }
        };


        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Dismiss(); e.Handled = true; }
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
        tray = new Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Application, Text = "Mic Some Noise", Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => Reveal()); menu.Items.Add("Stop track", null, (_, _) => audio.Stop());
        menu.Items.Add("Library", null, (_, _) => ShowLibrary()); menu.Items.Add("Settings", null, (_, _) => ShowSettings());
        menu.Items.Add("Exit", null, (_, _) => Exit()); tray.ContextMenuStrip = menu; tray.DoubleClick += (_, _) => Reveal();
        SourceInitialized += (_, _) =>
        {
            hotkeys = new Hotkeys(new WindowInteropHelper(this).Handle); hotkeys.Open += Reveal; hotkeys.Play += async () => await PlaySelected(); hotkeys.Stop += audio.Stop;
            hotkeys.VolumeDown += () => volume.Value = Math.Clamp(Math.Round(volume.Value - .05, 2), 0, 1);
            hotkeys.VolumeUp += () => volume.Value = Math.Clamp(Math.Round(volume.Value + .05, 2), 0, 1);
            try { hotkeys.Apply(store.Settings.OpenHotkey, store.Settings.PlayHotkey, store.Settings.StopHotkey, store.Settings.VolumeDownHotkey, store.Settings.VolumeUpHotkey); } catch (Exception e) { status.Text = e.Message; }
            if (store.Settings.Cable != null) try { var s = store.Settings; pushToTalk.Configure(s.PushToTalkEnabled, s.PushToTalkEnabled ? Hotkeys.ValidatePushToTalk(s.PushToTalkKey, s.OpenHotkey, s.PlayHotkey, s.StopHotkey, s.VolumeDownHotkey, s.VolumeUpHotkey) : (ushort)0); audio.Start(s); } catch (Exception e) { status.Text = e.Message; }
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
        string? selectedId = store.Settings.SelectedTrackId;
        results.ItemsSource = store.Tracks.Where(t => t.Name.Contains(search.Text, StringComparison.OrdinalIgnoreCase)).OrderBy(t => t.Name).ToList();
        results.SelectedItem = results.Items.Cast<Track>().FirstOrDefault(t => t.Id == selectedId);
        UpdateReadyTrack();
        if (store.Tracks.Count == 0) status.Text = "Import a WAV, MP3, or YouTube video to start.";
    }
    private void UpdateReadyTrack()
    {
        var ready = store.Tracks.FirstOrDefault(t => t.Id == store.Settings.SelectedTrackId);
        if (ready == null) store.Settings.SelectedTrackId = null;
        string action = ready != null && ready.Id == audio.CurrentTrackId ? (audio.IsPaused ? "Resume" : "Pause") : "Play";
        playButton.Content = action;
        playButton.IsEnabled = ready != null;
        selected.Text = ready == null ? "Ready: select a track" : $"Ready: {ready.Name} · {store.Settings.PlayHotkey} to {action.ToLowerInvariant()}";
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
    private void Exit() { if (importCancellation != null) { MessageBox.Show("Cancel the import and wait for it to finish before exiting."); return; } pushToTalk.Release(); exiting = true; store.SaveSettings(); hotkeys?.Dispose(); tray.Dispose(); audio.Dispose(); pushToTalk.Dispose(); Application.Current.Shutdown(); }
    private Window Dialog(string title, int height, out StackPanel panel)
    {
        var window = new Window { Owner = this, Title = title, Width = 600, Height = height, MinWidth = 480, MinHeight = 400, FontFamily = FontFamily, FontSize = FontSize, WindowStartupLocation = WindowStartupLocation.CenterScreen };
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
        Label(panel, "Stop shortcut"); var stop = new TextBox { Text = store.Settings.StopHotkey }; panel.Children.Add(stop);
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
                ushort pttCode = automaticPtt.IsChecked == true ? Hotkeys.ValidatePushToTalk(pttKey.Text, open.Text, play.Text, stop.Text, volumeDown.Text, volumeUp.Text) : (ushort)0;
                hotkeys!.Apply(open.Text, play.Text, stop.Text, volumeDown.Text, volumeUp.Text);
                audio.Stop();
                pushToTalk.Configure(automaticPtt.IsChecked == true, pttCode);
                var s = store.Settings; s.Microphone = (string)mic.SelectedValue; s.Headphones = (string)hp.SelectedValue; s.Cable = (string)cable.SelectedValue;
                s.IncludeMicrophoneDuringPlayback = includeMicrophone.IsChecked == true; s.MicrophoneVolume = (float)gain.Value; s.OpenHotkey = open.Text; s.PlayHotkey = play.Text; s.StopHotkey = stop.Text; s.VolumeDownHotkey = volumeDown.Text; s.VolumeUpHotkey = volumeUp.Text; s.PushToTalkEnabled = automaticPtt.IsChecked == true; s.PushToTalkKey = pttKey.Text.Trim(); s.PushToTalkDelayMs = delayMs;
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
        var window = Dialog("Library and import", 680, out var panel);
        Label(panel, "Library name / rename selected track"); var name = new TextBox(); panel.Children.Add(name);
        var library = new ListBox { Height = 170, ItemsSource = store.Tracks.ToList(), Margin = new Thickness(0, 12, 0, 0) }; panel.Children.Add(library);
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
        Label(panel, "YouTube video URL"); var url = new TextBox(); panel.Children.Add(url);
        Label(panel, "Import one public, non-live video that you have permission to download. Requires yt-dlp, FFmpeg, and Deno. Existing tracks work offline.");
        var imports = new WrapPanel(); panel.Children.Add(imports);
        async Task Import(bool youtube)
        {
            if (importCancellation != null) { message.Text = "An import is already running."; return; }
            string? file = null;
            if (!youtube)
            {
                var picker = new Microsoft.Win32.OpenFileDialog { Filter = "Audio|*.wav;*.mp3" };
                if (picker.ShowDialog(window) != true) return; file = picker.FileName;
            }
            using var cancel = new CancellationTokenSource(); importCancellation = cancel; imports.IsEnabled = false;
            try
            {
                message.Text = "Importing…"; var importer = new Importer(store);
                if (youtube) await importer.Youtube(url.Text.Trim(), name.Text, new Progress<string>(text => message.Text = text), cancel.Token);
                else await importer.Local(file!, name.Text, cancel.Token);
                Reload(); message.Text = "Imported. Select the track in the launcher to play it.";
            }
            catch (OperationCanceledException) { message.Text = "Import cancelled."; }
            catch (Exception e) { message.Text = e.Message; }
            finally { importCancellation = null; imports.IsEnabled = true; }
        }
        AddButton(imports, "Import local file", async () => await Import(false)); AddButton(imports, "Import YouTube", async () => await Import(true));
        AddButton(panel, "Cancel import", () => importCancellation?.Cancel()); panel.Children.Add(message);
        window.Closing += (_, e) => { if (importCancellation != null) { importCancellation.Cancel(); e.Cancel = true; message.Text = "Cancelling. Close this window when cancellation finishes."; } };
        managementWindow = window;
        try { window.ShowDialog(); } finally { managementWindow = null; }
    }
    private sealed record Device(string Id, string Name) { public override string ToString() => Name; }
}
