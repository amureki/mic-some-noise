using System;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MicSomeNoise;

public sealed class AudioEngine : IDisposable
{
    private readonly object gate = new();
    private WasapiCapture? capture;
    private WasapiOut? headphones, broadcast;
    private BufferedWaveProvider? microphone;
    private ISampleProvider? microphoneSamples;
    private AudioFileReader? reader;
    private ISampleProvider? track;
    private readonly FloatQueue monitor = new(48000 * 2 * 80 / 1000);
    private readonly AudioTestRecording testRecording = new();
    public AudioTestStatus TestStatus { get { lock (gate) return testRecording.Status; } }
    public void BeginTest()
    {
        if (!Running) throw new InvalidOperationException("Apply your audio devices in Settings before recording a test.");
        lock (gate) testRecording.Begin();
    }
    public void FinishTest() { lock (gate) testRecording.Finish(); }
    public void ListenToTest()
    {
        if (!Running) throw new InvalidOperationException("Apply your audio devices in Settings before listening.");
        lock (gate) testRecording.Play();
    }
    public void ClearTest() { lock (gate) testRecording.Clear(); }
    private int generation;
    private int playbackStartDelayMs;
    private bool automaticPushToTalk;
    private long microphoneMuteUntil;
    public bool IncludeMicrophoneDuringPlayback { get; set; }
    public Func<bool> MicrophoneHoldPressed { get; set; } = () => false;
    private float trackVolume = .5f, headphoneTrackVolume = .5f, micVolume = 1;
    public string? Current { get; private set; }
    public string? CurrentTrackId { get; private set; }
    public bool IsPaused { get; private set; }
    public bool IsStarting { get; private set; }
    public event Action<string>? Error;
    public event Action? Changed;
    public event Action? TrackStarting;
    public event Action? TrackStopped;
    public event Action? TrackFinished;
    public bool Running => broadcast != null;
    public float TrackVolume { get => trackVolume; set => trackVolume = Math.Clamp(value, 0, 1); }
    public float HeadphoneTrackVolume { get => headphoneTrackVolume; set => headphoneTrackVolume = Math.Clamp(value, 0, 1); }
    public float MicrophoneVolume { get => micVolume; set => micVolume = Math.Clamp(value, 0, 2); }

    public void Start(Settings settings)
    {
        Shutdown();
        if (settings.Microphone == null || settings.Headphones == null || settings.Cable == null)
            throw new InvalidOperationException("Select a microphone, headphones, and CABLE Input in Settings.");
        if (settings.Headphones == settings.Cable)
            throw new InvalidOperationException("Headphones and broadcast output must be different devices.");
        try
        {
            using var devices = new MMDeviceEnumerator();
            using var mic = devices.GetDevice(settings.Microphone);
            using var hp = devices.GetDevice(settings.Headphones);
            using var cable = devices.GetDevice(settings.Cable);
            capture = new WasapiCapture(mic);
            microphone = new BufferedWaveProvider(capture.WaveFormat) { BufferDuration = TimeSpan.FromMilliseconds(200), DiscardOnBufferOverflow = true, ReadFully = true };
            microphoneSamples = Stereo48(microphone.ToSampleProvider());
            capture.DataAvailable += (_, e) => { lock (gate) if (microphone != null) { if (microphone.BufferedDuration.TotalMilliseconds > 80) microphone.ClearBuffer(); microphone.AddSamples(e.Buffer, 0, e.BytesRecorded); } };
            capture.RecordingStopped += (_, e) => { if (e.Exception != null) { Stop(); Error?.Invoke("Microphone disconnected. Reopen Settings and apply the audio devices. " + e.Exception.Message); } };
            headphones = new WasapiOut(hp, AudioClientShareMode.Shared, true, 40);
            broadcast = new WasapiOut(cable, AudioClientShareMode.Shared, true, 40);
            headphones.Init(new Output(this, false));
            broadcast.Init(new Output(this, true));
            headphones.PlaybackStopped += OnOutputStopped;
            broadcast.PlaybackStopped += OnOutputStopped;
            IncludeMicrophoneDuringPlayback = settings.IncludeMicrophoneDuringPlayback;
            automaticPushToTalk = settings.PushToTalkEnabled;
            playbackStartDelayMs = settings.PushToTalkEnabled ? Math.Clamp(settings.PushToTalkDelayMs, 0, 2000) : 0;
            TrackVolume = settings.TrackVolume;
            HeadphoneTrackVolume = settings.HeadphoneTrackVolume;
            MicrophoneVolume = settings.MicrophoneVolume;
            capture.StartRecording();

            headphones.Play();
            broadcast.Play();
        }
        catch { Shutdown(); throw; }
    }
    private void OnOutputStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null) { Stop(); Error?.Invoke("Audio output stopped. Reopen Settings and apply the audio devices. " + e.Exception.Message); }
    }
    private static ISampleProvider Stereo48(ISampleProvider source)
    {
        if (source.WaveFormat.Channels == 1) source = new MonoToStereoSampleProvider(source);
        if (source.WaveFormat.Channels != 2) throw new InvalidOperationException("Only mono and stereo audio devices/files are supported. Select stereo format in Windows sound settings.");
        return source.WaveFormat.SampleRate == 48000 ? source : new WdlResamplingSampleProvider(source, 48000);
    }
    public Task TogglePlayback(Track selected)
    {
        lock (gate)
        {
            if (track != null && CurrentTrackId == selected.Id)
            {
                if (IsPaused) return Resume();
                Pause();
                return Task.CompletedTask;
            }
        }
        return Play(selected);
    }
    public void Pause()
    {
        lock (gate)
        {
            if (track == null || IsPaused) return;
            generation++;
            microphoneMuteUntil = 0;
            IsPaused = true;
            IsStarting = false;
            monitor.Clear();
            TrackStopped?.Invoke();
        }
        Changed?.Invoke();
    }
    public async Task Resume()
    {
        int request, delay;
        lock (gate)
        {
            if (track == null || !IsPaused) return;
            if (!Running) throw new InvalidOperationException("Apply your audio devices in Settings before resuming.");
            TrackStarting?.Invoke();
            request = ++generation;
            delay = playbackStartDelayMs;
            IsPaused = false;
            IsStarting = true;
        }
        Changed?.Invoke();
        if (delay > 0) await Task.Delay(delay);
        lock (gate)
        {
            if (request != generation) return;
            IsStarting = false;
        }
        Changed?.Invoke();
    }
    public async Task Play(Track selected)

    {
        if (!Running) throw new InvalidOperationException("Apply your audio devices in Settings before playback.");
        Stop();
        int request = generation;
        AudioFileReader? loaded = null;
        try
        {
            loaded = await Task.Run(() => new AudioFileReader(selected.File));
            var samples = Stereo48(loaded);
            int delay;
            lock (gate)
            {
                if (request != generation) return;
                TrackStarting?.Invoke();
                IsStarting = true;
                delay = playbackStartDelayMs;
            }
            if (delay > 0) await Task.Delay(delay);
            lock (gate)
            {
                // Stop or a newer selection may have cancelled this request during the delay.
                if (request != generation) return;
                IsStarting = false;
                reader = loaded; loaded = null; track = samples; Current = selected.Name; CurrentTrackId = selected.Id;
            }
            Changed?.Invoke();
        }
        finally { loaded?.Dispose(); }
    }
    public void Stop()
    {
        lock (gate)
        {
            generation++; microphoneMuteUntil = 0; track = null; reader?.Dispose(); reader = null; Current = null; CurrentTrackId = null; IsPaused = IsStarting = false; monitor.Clear(); testRecording.StopPreview();
            TrackStopped?.Invoke();
        }
        Changed?.Invoke();
    }
    private int Read(float[] buffer, int offset, int count, bool mix, float[] scratch)
    {
        lock (gate)
        {
            // NAudio can overlay float[] on byte[]. Array.Clear uses the backing byte element size.
            for (int i = offset; i < offset + count; i++) buffer[i] = 0;
            if (!mix)
            {
                monitor.Read(buffer, offset, count);
                for (int i = offset; i < offset + count; i++) buffer[i] *= headphoneTrackVolume;
                testRecording.ReadPreview(buffer, offset, count);
                return count;
            }

            bool readingTrack = track != null && !IsPaused && !IsStarting;
            int got = readingTrack ? track!.Read(buffer, offset, count) : 0;
            monitor.Write(buffer, offset, count);
            for (int i = 0; i < got; i++) buffer[offset + i] *= trackVolume;
            if (readingTrack && got == 0)
            {
                track = null; reader?.Dispose(); reader = null; Current = null; CurrentTrackId = null;
                if (automaticPushToTalk) microphoneMuteUntil = Environment.TickCount64 + PushToTalk.ReleaseTailMilliseconds;
                TrackFinished?.Invoke();
                ThreadPool.QueueUserWorkItem(_ => Changed?.Invoke());
            }
            // Keep draining capture while muted so old speech is not replayed on unmute.
            microphoneSamples?.Read(scratch, 0, count);
            bool muteMicrophone = !IncludeMicrophoneDuringPlayback
                && (readingTrack || IsStarting || Environment.TickCount64 < microphoneMuteUntil)
                && !MicrophoneHoldPressed();
            for (int i = 0; i < count; i++) buffer[offset + i] = Math.Clamp(buffer[offset + i] + (muteMicrophone ? 0 : scratch[i] * micVolume), -1f, 1f);
            testRecording.Capture(buffer, offset, count);
            return count;
        }
    }
    private void Shutdown()
    {
        Stop();
        capture?.StopRecording(); capture?.Dispose(); capture = null;
        headphones?.Stop(); headphones?.Dispose(); headphones = null;
        broadcast?.Stop(); broadcast?.Dispose(); broadcast = null;
        lock (gate) { microphone = null; microphoneSamples = null; testRecording.Clear(); }
    }
    public void Dispose() { Shutdown(); }
    private sealed class Output(AudioEngine owner, bool mix) : ISampleProvider
    {
        private float[] scratch = new float[8192];
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        public int Read(float[] buffer, int offset, int count)
        {
            if (scratch.Length < count) scratch = new float[count];
            return owner.Read(buffer, offset, count, mix, scratch);
        }
    }
}
