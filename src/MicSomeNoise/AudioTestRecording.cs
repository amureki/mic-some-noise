using System;

namespace MicSomeNoise;

public readonly record struct AudioTestStatus(bool Recording, bool Playing, double Seconds, bool HasRecording);

// Called under AudioEngine's gate. Retain at most ten seconds, in memory only.
internal sealed class AudioTestRecording
{
    private const int SamplesPerSecond = 48000 * 2;
    private float[]? samples;
    private int length, position;
    private bool recording, playing;
    public AudioTestStatus Status => new(recording, playing, (double)(playing ? position : length) / SamplesPerSecond, length > 0);
    public void Begin()
    {
        samples = new float[SamplesPerSecond * 10];
        length = position = 0; recording = true; playing = false;
    }
    public void Capture(float[] source, int offset, int count)
    {
        if (!recording || samples == null) return;
        int take = Math.Min(count, samples.Length - length);
        // Source may be NAudio's byte-backed float array; copy through typed indexing.
        for (int i = 0; i < take; i++) samples[length + i] = source[offset + i];
        length += take;
        if (length == samples.Length) recording = false;
    }
    public void Finish() => recording = false;
    public void Play()
    {
        if (recording || length == 0) throw new InvalidOperationException("Finish recording before listening to the test.");
        position = 0; playing = true;
    }
    public void ReadPreview(float[] output, int offset, int count)
    {
        if (!playing || samples == null) return;
        int take = Math.Min(count, length - position);
        for (int i = 0; i < count; i++) output[offset + i] = i < take ? samples[position + i] : 0;
        position += take;
        if (position == length) playing = false;
    }
    public void StopPreview() => playing = false;
    public void Clear() { samples = null; length = position = 0; recording = playing = false; }
}
