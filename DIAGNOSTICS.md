# Microphone leakage investigation

## Evidence

The Windows 0.1.1 log showed matching headphone and broadcast peaks during microphone-only operation. Both eventually reached 1. The QuadCast, DAC-X6, and VB-CABLE all used 48 kHz stereo float audio. Windows monitoring was disabled; bypassing NVIDIA Broadcast did not resolve the noise.

## Cause and reproduction

NAudio's SampleToWaveProvider presents a byte-backed WaveBuffer as float[]. Array.Clear follows the backing array element size, so a sample count cleared only a quarter of the intended bytes. Subsequent reads retained microphone samples, copied them into the headphone queue, and added microphone audio again until clipping occurred.

The original tests used real float arrays, which hid this defect. The added test drives the production output providers through NAudio's actual byte adapter with reused buffers. Before the fix, it failed on the second read at sample 480 with a headphone value of 0.25 despite no track playing.

Run the regression locally:

```powershell
dotnet run --project tests/MicSomeNoise.Checks -c Release
```

## Fix and validation

Version 0.1.2 clears output samples through float indexing. The byte-adapter regression, existing provider tests, and queue checks pass. The Windows x64 Release package compiles. Temporary signal logging was removed after diagnosis.

Hardware confirmation remains pending. Exit the previous build through the tray, extract version 0.1.2 into a separate folder, and run it. Restore microphone volume and unmute the app. With no track playing, speech should produce no headphone audio. During track playback, headphones should contain only the track; voice chat should receive both speech and the track. Stopping a track must preserve microphone forwarding.
