using MicSomeNoise;

var queue = new FloatQueue(8);
void AssertSamples(float[] expected)
{
    var actual = new float[expected.Length];
    queue.Read(actual, 0, actual.Length);
    if (!actual.SequenceEqual(expected)) throw new Exception($"Expected {string.Join(',', expected)}; got {string.Join(',', actual)}");
}
queue.Write([1, 2, 3, 4], 0, 4);
AssertSamples([1, 2]);
queue.Write([5, 6, 7, 8, 9, 10, 11, 12], 0, 8);
AssertSamples([5, 6, 7, 8, 9, 10, 11, 12, 0, 0]);
queue.Write([1, 2], 0, 2);
queue.Clear();
AssertSamples([0, 0]);
var expectedQueue = new Queue<float>();
var random = new Random(42);
for (int round = 0; round < 10000; round++)
{
    if (random.Next(2) == 0)
    {
        var values = Enumerable.Range(0, random.Next(1, 10) * 2).Select(_ => (float)random.Next()).ToArray();
        queue.Write(values, 0, values.Length);
        foreach (var value in values) expectedQueue.Enqueue(value);
        while (expectedQueue.Count > 8) expectedQueue.Dequeue();
    }
    else
    {
        var expected = new float[random.Next(1, 10) * 2];
        for (int i = 0; i < expected.Length && expectedQueue.Count > 0; i++) expected[i] = expectedQueue.Dequeue();
        AssertSamples(expected);
    }
}
Console.WriteLine("PASS: stereo FIFO order, overflow, underflow, clear, and 10,000 randomized operations.");

// Run the production output providers without opening Windows audio devices.
using (var engine = new AudioEngine { IncludeMicrophoneDuringPlayback = true })
{
    var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
    typeof(AudioEngine).GetField("microphoneSamples", flags)!.SetValue(engine, new ConstantSamples(.75f));
    var output = typeof(AudioEngine).GetNestedType("Output", System.Reflection.BindingFlags.NonPublic)!;
    var headphones = (NAudio.Wave.ISampleProvider)Activator.CreateInstance(output, engine, false)!;
    var broadcast = (NAudio.Wave.ISampleProvider)Activator.CreateInstance(output, engine, true)!;
    float[] samples = new float[10000];
    for (int round = 0; round < 1000; round++)
    {
        int count = random.Next(1, 2000) * 2;
        Array.Fill(samples, -1f);
        broadcast.Read(samples, 2, count);
        if (samples.Skip(2).Take(count).Any(x => x != .75f)) throw new Exception("Microphone missing from broadcast.");
        // Reuse the same dirty buffer to detect output contamination and underflow.
        headphones.Read(samples, 2, count + 2);
        if (samples.Skip(2).Take(count + 2).Any(x => x != 0)) throw new Exception("Microphone leaked into headphones with no track.");
    }
    typeof(AudioEngine).GetField("track", flags)!.SetValue(engine, new ConstantSamples(.25f));
    engine.TrackVolume = 1;
    engine.Stop();
    typeof(AudioEngine).GetField("track", flags)!.SetValue(engine, new ConstantSamples(.25f));
    broadcast.Read(samples, 0, 200);
    if (samples.Take(200).Any(x => x != 1f)) throw new Exception("Broadcast mix incorrect.");
    headphones.Read(samples, 0, 200);
    if (samples.Take(200).Any(x => x != .25f)) throw new Exception("Microphone leaked into headphone track.");
    engine.Stop();
    broadcast.Read(samples, 0, 200);
    if (samples.Take(200).Any(x => x != .75f)) throw new Exception("Stop interrupted microphone forwarding.");
    headphones.Read(samples, 0, 200);
    if (samples.Take(200).Any(x => x != 0)) throw new Exception("Headphones not silent after stop.");
}
Console.WriteLine("PASS: production output providers isolate microphone from headphones, mix tracks, and preserve speech after stop. Windows endpoint routing is not covered.");

// WASAPI consumes byte buffers through this adapter, not ordinary float arrays.
using (var engine = new AudioEngine { IncludeMicrophoneDuringPlayback = true })
{
    var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
    typeof(AudioEngine).GetField("microphoneSamples", flags)!.SetValue(engine, new ConstantSamples(.25f));
    var output = typeof(AudioEngine).GetNestedType("Output", System.Reflection.BindingFlags.NonPublic)!;
    var broadcast = new NAudio.Wave.SampleProviders.SampleToWaveProvider((NAudio.Wave.ISampleProvider)Activator.CreateInstance(output, engine, true)!);
    var headphones = new NAudio.Wave.SampleProviders.SampleToWaveProvider((NAudio.Wave.ISampleProvider)Activator.CreateInstance(output, engine, false)!);
    var cableBytes = new byte[7680];
    var headphoneBytes = new byte[7680];
    for (int round = 0; round < 10; round++)
    {
        broadcast.Read(cableBytes, 0, cableBytes.Length);
        headphones.Read(headphoneBytes, 0, headphoneBytes.Length);
        for (int i = 0; i < headphoneBytes.Length; i += sizeof(float))
            if (BitConverter.ToSingle(headphoneBytes, i) != 0)
                throw new Exception($"Microphone leaked through NAudio byte adapter: round {round}, sample {i / 4}, level {BitConverter.ToSingle(headphoneBytes, i)}");
        for (int i = 0; i < cableBytes.Length; i += sizeof(float))
            if (BitConverter.ToSingle(cableBytes, i) != .25f)
                throw new Exception("Microphone accumulated in reused broadcast buffer.");
    }
}
Console.WriteLine("PASS: reused NAudio byte-backed output buffers stay isolated without microphone accumulation.");

using (var engine = new AudioEngine { IncludeMicrophoneDuringPlayback = true })
{
    var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
    var microphoneField = typeof(AudioEngine).GetField("microphoneSamples", flags)!;
    microphoneField.SetValue(engine, new ConstantSamples(.25f));
    typeof(AudioEngine).GetField("track", flags)!.SetValue(engine, new ConstantSamples(.125f));
    engine.TrackVolume = 1;
    var recording = (AudioTestRecording)typeof(AudioEngine).GetField("testRecording", flags)!.GetValue(engine)!;
    var output = typeof(AudioEngine).GetNestedType("Output", System.Reflection.BindingFlags.NonPublic)!;
    var cable = new NAudio.Wave.SampleProviders.SampleToWaveProvider((NAudio.Wave.ISampleProvider)Activator.CreateInstance(output, engine, true)!);
    var headphones = new NAudio.Wave.SampleProviders.SampleToWaveProvider((NAudio.Wave.ISampleProvider)Activator.CreateInstance(output, engine, false)!);
    var bytes = new byte[7680];
    recording.Begin();
    cable.Read(bytes, 0, bytes.Length);
    cable.Read(bytes, 0, bytes.Length);
    engine.FinishTest();
    if (engine.TestStatus.Recording || !engine.TestStatus.HasRecording || engine.TestStatus.Seconds != .04)
        throw new Exception("Test recording duration/state incorrect.");
    recording.Play();
    microphoneField.SetValue(engine, new ConstantSamples(.75f));
    cable.Read(bytes, 0, bytes.Length);
    for (int i = 0; i < bytes.Length; i += 4)
        if (BitConverter.ToSingle(bytes, i) != .875f) throw new Exception("Test playback leaked into broadcast.");
    // Drain more headphone samples than the recorded clip contains, with a nonzero offset.
    var previewBytes = new byte[19208];
    headphones.Read(previewBytes, 8, 19200);
    for (int i = 8; i < previewBytes.Length; i += 4)
    {
        float expected = i < 8 + 15360 ? .375f : 0;
        if (BitConverter.ToSingle(previewBytes, i) != expected) throw new Exception("Test preview mix or silence padding incorrect.");
    }
    if (engine.TestStatus.Playing) throw new Exception("Preview did not finish naturally.");
    recording.Play(); engine.Stop();
    if (engine.TestStatus.Playing || !engine.TestStatus.HasRecording) throw new Exception("Stop must stop preview but retain the recording.");
    cable.Read(bytes, 0, bytes.Length);
    for (int i = 0; i < bytes.Length; i += 4)
        if (BitConverter.ToSingle(bytes, i) != .75f) throw new Exception("Test stop interrupted microphone forwarding.");
    engine.ClearTest();
    if (engine.TestStatus.HasRecording || engine.TestStatus.Recording) throw new Exception("Clear retained test audio.");
    recording.Begin();
    for (int i = 0; i < 510; i++) cable.Read(bytes, 0, bytes.Length);
    if (engine.TestStatus.Recording || engine.TestStatus.Seconds != 10) throw new Exception("Test exceeded its ten-second limit.");
    recording.Begin();
    if (engine.TestStatus.HasRecording || engine.TestStatus.Seconds != 0) throw new Exception("New recording retained previous audio.");
}
Console.WriteLine("PASS: test recording captures the broadcast mix, previews only in headphones, pads byte-backed buffers, stops, clears, and caps at ten seconds.");

// Fake only the OS key sender: no actual input or Windows audio devices are used.
var sentKeys = new System.Collections.Concurrent.ConcurrentQueue<(ushort Key, bool Down)>();
using (var ptt = new PushToTalk((key, down) => sentKeys.Enqueue((key, down))))
using (var engine = new AudioEngine { IncludeMicrophoneDuringPlayback = true })
{
    ptt.Hold(); ptt.Release();
    if (!sentKeys.IsEmpty) throw new Exception("Disabled push-to-talk sent input.");
    ptt.Configure(true, 0x56);
    engine.TrackStarting += ptt.Hold;
    engine.TrackStopped += ptt.Release;
    engine.TrackFinished += ptt.ReleaseAfterPlayback;
    ptt.Hold(); ptt.Hold();
    engine.Stop(); engine.Stop();
    if (!sentKeys.ToArray().SequenceEqual(new[] { ((ushort)0x56, true), ((ushort)0x56, false) }))
        throw new Exception("Stop did not produce exactly one key release.");
    sentKeys.Clear();
    var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
    var output = typeof(AudioEngine).GetNestedType("Output", System.Reflection.BindingFlags.NonPublic)!;
    var cable = (NAudio.Wave.ISampleProvider)Activator.CreateInstance(output, engine, true)!;
    typeof(AudioEngine).GetField("track", flags)!.SetValue(engine, new EmptySamples());
    ptt.Hold();
    cable.Read(new float[100], 0, 100);
    if (!SpinWait.SpinUntil(() => sentKeys.Count == 2, 2000) || sentKeys.Last().Down)
        throw new Exception("Natural track completion did not release push-to-talk.");
    sentKeys.Clear();
    ptt.Hold(); ptt.ReleaseAfterPlayback(); ptt.Hold();
    await Task.Delay(250);
    if (sentKeys.Count != 1) throw new Exception("A stale finish timer released a new track's key.");
    ptt.Configure(false, 0);
    if (sentKeys.Count != 2 || sentKeys.Last().Down) throw new Exception("Disabling did not release push-to-talk.");
    sentKeys.Clear();
    ptt.Configure(true, 0x56);
    var recording = (AudioTestRecording)typeof(AudioEngine).GetField("testRecording", flags)!.GetValue(engine)!;
    recording.Begin(); recording.Capture([.25f, .25f], 0, 2); recording.Finish(); recording.Play();
    var headphones = (NAudio.Wave.ISampleProvider)Activator.CreateInstance(output, engine, false)!;
    headphones.Read(new float[100], 0, 100);
    if (!sentKeys.IsEmpty) throw new Exception("Test preview pressed push-to-talk.");
    ptt.Hold(); engine.Dispose();
    if (sentKeys.Count != 2 || sentKeys.Last().Down) throw new Exception("Audio shutdown did not release push-to-talk.");
}
bool rejectDown = true;
using (var ptt = new PushToTalk((_, down) => { if (down && rejectDown) throw new InvalidOperationException("blocked"); }))
{
    ptt.Configure(true, 0x56);
    try { ptt.Hold(); throw new Exception("Blocked input did not abort start."); } catch (InvalidOperationException) { }
    rejectDown = false;
    ptt.Hold(); ptt.Release();
}
int releases = 0, releaseErrors = 0;
using (var ptt = new PushToTalk((_, down) => { if (!down && ++releases == 1) throw new InvalidOperationException("blocked release"); }))
{
    ptt.Error += _ => releaseErrors++;
    ptt.Configure(true, 0x56); ptt.Hold(); ptt.Release(); ptt.Release();
    if (releases != 2 || releaseErrors != 1) throw new Exception("Failed release was not retained for retry.");
}
Console.WriteLine("PASS: optional push-to-talk holds/releases, natural completion, stale timer cancellation, disable, shutdown, input failures, and preview isolation.");

string fixture = Path.Combine(Path.GetTempPath(), "mic-delay-" + Guid.NewGuid().ToString("N") + ".wav");
using (var writer = new NAudio.Wave.WaveFileWriter(fixture, NAudio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)))
    writer.WriteSamples(Enumerable.Repeat(.125f, 9600).ToArray(), 0, 9600);
using (var engine = new AudioEngine { IncludeMicrophoneDuringPlayback = true })
{
    var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
    var broadcastField = typeof(AudioEngine).GetField("broadcast", flags)!;
    // Play only checks this guard; never invoke methods on the uninitialized Windows output.
    broadcastField.SetValue(engine, System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(NAudio.Wave.WasapiOut)));
    typeof(AudioEngine).GetField("playbackStartDelayMs", flags)!.SetValue(engine, 150);
    var pressed = new TaskCompletionSource(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
    bool held = false;
    engine.TrackStarting += () => { held = true; pressed.TrySetResult(); };
    engine.TrackStopped += () => held = false;
    try
    {
        var first = engine.Play(new Track("one", "one", fixture));
        await pressed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        if (!held || engine.Current != null) throw new Exception("Track started before the push-to-talk delay.");
        engine.Stop();
        await first;
        if (held || engine.Current != null) throw new Exception("Stop during the delay allowed delayed playback or retained the key.");
        pressed = new TaskCompletionSource(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
        var replaced = engine.Play(new Track("old", "old", fixture));
        await pressed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var replacement = engine.Play(new Track("new", "new", fixture));
        await Task.WhenAll(replaced, replacement);
        if (engine.Current != "new" || !held || watch.ElapsedMilliseconds < 140)
            throw new Exception("Replacement skipped its delay or stale playback replaced it.");
        var selectedTrack = new Track("new", "new", fixture);
        var output = typeof(AudioEngine).GetNestedType("Output", System.Reflection.BindingFlags.NonPublic)!;
        var cable = new NAudio.Wave.SampleProviders.SampleToWaveProvider((NAudio.Wave.ISampleProvider)Activator.CreateInstance(output, engine, true)!);
        var headphones = new NAudio.Wave.SampleProviders.SampleToWaveProvider((NAudio.Wave.ISampleProvider)Activator.CreateInstance(output, engine, false)!);
        typeof(AudioEngine).GetField("microphoneSamples", flags)!.SetValue(engine, new ConstantSamples(.25f));
        var bytes = new byte[512];
        cable.Read(bytes, 0, bytes.Length);
        var decoder = (NAudio.Wave.AudioFileReader)typeof(AudioEngine).GetField("reader", flags)!.GetValue(engine)!;
        long position = decoder.Position;
        await engine.TogglePlayback(selectedTrack);
        if (!engine.IsPaused || held || engine.CurrentTrackId != "new") throw new Exception("Pause did not retain the track and release push-to-talk.");
        for (int i = 0; i < 100; i++) cable.Read(bytes, 0, bytes.Length);
        if (decoder.Position != position || !engine.IsPaused) throw new Exception("Paused decoder advanced or finished.");
        if (Enumerable.Range(0, bytes.Length / 4).Any(i => BitConverter.ToSingle(bytes, i * 4) != .25f))
            throw new Exception("Pause interrupted microphone forwarding.");
        headphones.Read(bytes, 0, bytes.Length);
        if (bytes.Any(x => x != 0)) throw new Exception("Pause left audio in headphone monitoring.");
        var resumed = engine.TogglePlayback(selectedTrack);
        if (!held || !engine.IsStarting) throw new Exception("Resume did not hold push-to-talk before waiting.");
        cable.Read(bytes, 0, bytes.Length);
        if (decoder.Position != position) throw new Exception("Resume consumed audio during the push-to-talk delay.");
        await resumed;
        cable.Read(bytes, 0, bytes.Length);
        if (engine.IsPaused || engine.IsStarting || decoder.Position <= position) throw new Exception("Resume failed to continue the decoder.");
        engine.Pause();
        var cancelledResume = engine.Resume();
        engine.Stop();
        await cancelledResume;
        if (held || engine.Current != null || engine.IsStarting || engine.IsPaused) throw new Exception("Stop did not cancel a pending resume.");
        await engine.TogglePlayback(selectedTrack);
        if (engine.CurrentTrackId != "new" || engine.IsPaused) throw new Exception("Play after stop did not restart the selected track.");
        engine.Pause();
        await engine.TogglePlayback(new Track("different", "different", fixture));
        if (engine.CurrentTrackId != "different" || engine.IsPaused) throw new Exception("Selecting a different track resumed the previous track.");
        engine.Stop();
        engine.IncludeMicrophoneDuringPlayback = false;
        engine.TrackVolume = 1;
        pressed = new TaskCompletionSource(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
        var playback = engine.TogglePlayback(selectedTrack);
        await pressed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        cable.Read(bytes, 0, bytes.Length);
        if (bytes.Any(x => x != 0)) throw new Exception("Real playback leaked microphone during push-to-talk lead-in.");
        headphones.Read(bytes, 0, bytes.Length);
        await playback;
        for (int block = 0; block < 20; block++)
        {
            cable.Read(bytes, 0, bytes.Length);
            if (!held || Enumerable.Range(0, bytes.Length / 4).Any(i => BitConverter.ToSingle(bytes, i * 4) != .125f))
                throw new Exception("Real playback did not send track-only audio with push-to-talk held.");
            headphones.Read(bytes, 0, bytes.Length);
            if (Enumerable.Range(0, bytes.Length / 4).Any(i => BitConverter.ToSingle(bytes, i * 4) != .125f))
                throw new Exception("Headphone and cable track audio differ.");
        }
        await engine.TogglePlayback(selectedTrack);
        cable.Read(bytes, 0, bytes.Length);
        if (held || BitConverter.ToSingle(bytes, 0) != .25f) throw new Exception("Pause failed to release push-to-talk and restore voice.");
        await engine.TogglePlayback(selectedTrack);
        cable.Read(bytes, 0, bytes.Length);
        if (!held || BitConverter.ToSingle(bytes, 0) != .125f) throw new Exception("Resume failed to restore track-only audio.");
        engine.Stop();
    }
    finally
    {
        broadcastField.SetValue(engine, null);

        engine.Stop();
        File.Delete(fixture);
    }
}
Console.WriteLine("PASS: actual decoder/start path, lead-in cancellation, pause position, resume, microphone continuity, push-to-talk release, and track replacement.");
Console.WriteLine("PASS: real WAV playback sends identical track-only audio to cable and headphones while push-to-talk is held; pause restores voice and resume suppresses it.");

using (var engine = new AudioEngine())
{
    var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
    typeof(AudioEngine).GetField("microphoneSamples", flags)!.SetValue(engine, new ConstantSamples(.5f));
    typeof(AudioEngine).GetField("track", flags)!.SetValue(engine, new ConstantSamples(.25f));
    engine.TrackVolume = 1;
    var output = typeof(AudioEngine).GetNestedType("Output", System.Reflection.BindingFlags.NonPublic)!;
    var cable = new NAudio.Wave.SampleProviders.SampleToWaveProvider((NAudio.Wave.ISampleProvider)Activator.CreateInstance(output, engine, true)!);
    var bytes = new byte[512];
    cable.Read(bytes, 0, bytes.Length);
    if (Enumerable.Range(0, 128).Any(i => BitConverter.ToSingle(bytes, i * 4) != .25f)) throw new Exception("Default playback leaked microphone audio.");
    engine.IncludeMicrophoneDuringPlayback = true;
    cable.Read(bytes, 0, bytes.Length);
    if (BitConverter.ToSingle(bytes, 0) != .75f) throw new Exception("Opt-in microphone mix failed.");
    engine.IncludeMicrophoneDuringPlayback = false;
    engine.Pause();
    cable.Read(bytes, 0, bytes.Length);
    if (BitConverter.ToSingle(bytes, 0) != .5f) throw new Exception("Pause did not restore microphone.");
    engine.Stop();
    typeof(AudioEngine).GetField("<IsStarting>k__BackingField", flags)!.SetValue(engine, true);
    cable.Read(bytes, 0, bytes.Length);
    if (bytes.Any(x => x != 0)) throw new Exception("Push-to-talk lead-in leaked microphone audio.");
    engine.Stop();
    cable.Read(bytes, 0, bytes.Length);
    if (BitConverter.ToSingle(bytes, 0) != .5f) throw new Exception("Stop did not restore microphone.");
    typeof(AudioEngine).GetField("track", flags)!.SetValue(engine, new EmptySamples());
    typeof(AudioEngine).GetField("automaticPushToTalk", flags)!.SetValue(engine, true);
    cable.Read(bytes, 0, bytes.Length);
    cable.Read(bytes, 0, bytes.Length);
    if (bytes.Any(x => x != 0)) throw new Exception("Microphone leaked during push-to-talk release tail.");
    await Task.Delay(200);
    cable.Read(bytes, 0, bytes.Length);
    if (BitConverter.ToSingle(bytes, 0) != .5f) throw new Exception("Microphone did not return after completion.");
}
Console.WriteLine("PASS: tracks-only default, opt-in mix, pause/stop restoration, lead-in mute, and release-tail mute.");

sealed class EmptySamples : NAudio.Wave.ISampleProvider
{
    public NAudio.Wave.WaveFormat WaveFormat { get; } = NAudio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
    public int Read(float[] buffer, int offset, int count) => 0;
}

sealed class ConstantSamples(float value) : NAudio.Wave.ISampleProvider
{
    public NAudio.Wave.WaveFormat WaveFormat { get; } = NAudio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
    public int Read(float[] buffer, int offset, int count) { for (int i = offset; i < offset + count; i++) buffer[i] = value; return count; }
}
