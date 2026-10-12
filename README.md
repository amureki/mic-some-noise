# Mic Some Noise

A Windows 11 sound launcher for voice chat. Version 0.1.12 supports one track, microphone forwarding, local WAV/MP3 imports, and YouTube URL imports.

## Run on Windows 11 x64

Download [the latest Windows ZIP](https://github.com/amureki/mic-some-noise/releases/latest/download/MicSomeNoise-win-x64.zip), or choose a version from [Releases](https://github.com/amureki/mic-some-noise/releases). Downloads become available after the first version tag is built.

1. Extract `MicSomeNoise-win-x64.zip` to a writable folder. Keep its files together.
2. Install [VB-CABLE](https://vb-audio.com/Cable/) using the vendor's Windows installer. Restart Windows if requested.
3. Run `MicSomeNoise.exe`. The self-contained package includes .NET. For a framework-dependent build, install the [.NET 10 Desktop Runtime x64](https://dotnet.microsoft.com/download/dotnet/10.0).
4. In Settings, select your physical microphone, physical headphones, and **CABLE Input** as broadcast output. Click **Apply and start audio**.
5. In Discord or your game's voice settings, select **CABLE Output** as the input and your physical headphones as the output. Leave Windows default devices unchanged. Disable Windows “Listen to this device” for the cable and microphone to prevent duplicate audio.
6. Import a WAV or MP3, select it in the launcher, and press F8. Ask a teammate to confirm that they hear the track. Microphone audio is excluded during playback by default; enable **Include my microphone while a track plays** to mix speech with tracks.

The executable is unsigned. Windows may show an unknown-publisher warning. Only run the build you trust.

## Controls

- `Ctrl+Alt+Space`: show the launcher and focus search.
- Click or use Up / Down to select a track without playing. The Ready label names it. The main button and F8 play, pause, or resume that track. The launcher stays open.
- Escape or the close button: hide the launcher. Audio continues.
- `F8`: play, pause, or resume the selected track globally, without opening the launcher or changing focus.
- `F9`: stop and reset the track, including a pending load or resume. Microphone forwarding continues.
- Tray menu: open, stop, library, settings, or exit.
- Settings: change the global hotkeys, devices, and microphone gain. The launcher slider controls track volume for both outputs.

The app clamps the mixed signal to prevent digital overflow. Lower track or microphone volume if the mix sounds distorted. Start with headphones to avoid acoustic feedback.

## Test the outgoing mix

Expand **Test microphone + track** in the launcher. Click **Record 10 seconds**, then speak and play a track. Recording stops at ten seconds; **Finish** ends it sooner. Click **Listen** to hear the captured mix through your headphones. **Stop** also stops test playback. **Clear** deletes the test from memory.

The test records the exact mix the app sends to its broadcast output, before VB-CABLE and Discord processing. Test playback goes only to headphones. Live broadcasting continues during recording and listening. While listening, the recorded mix replaces your local track monitoring; it does not replace the live broadcast.

Use this to compare the app's output against Discord. A clean recording with cut-off audio in Discord narrows the problem to a later stage, such as cable routing or voice-client processing. It does not validate what a teammate receives.

The recording uses at most about 3.7 MiB of memory. It is never saved to disk or added to the library. A new recording replaces it; clearing, restarting audio devices, or exiting discards it. There is no live microphone monitoring.

## YouTube import

Local playback needs no external tools. YouTube import needs [yt-dlp](https://github.com/yt-dlp/yt-dlp), [FFmpeg](https://ffmpeg.org/download.html), and [Deno](https://deno.com/). Install them with `install-import-tools.ps1` in PowerShell, or put `yt-dlp.exe`, `ffmpeg.exe`, `ffprobe.exe`, and `deno.exe` in a `tools` folder beside the app. Restart the app after PATH changes.

Paste a single public video URL in **Import / library**, enter an optional name, then click **Import YouTube**. Import reports tool output and supports cancellation. It stores MP3 audio and preserves the original title and canonical source URL. A repeated video is rejected before adding it to the library, though it may download again. Import completion never starts playback.

Only download content you have permission to use and that the service permits you to download. Playlists, accounts, cookies, live videos, and restricted videos are unsupported. Extractor failures do not affect existing files. Update yt-dlp when extraction fails. See the [JavaScript runtime requirements](https://github.com/yt-dlp/yt-dlp/wiki/EJS).

## Storage and recovery

Settings and the library are in `%LOCALAPPDATA%\MicSomeNoise`. Local imports are copied to its `Audio` folder. Removing a library entry retains its copied file. Original files are never changed. Back up the whole folder to preserve the library.

If a device is removed, sleep interrupts audio, or the driver changes, reopen Settings, select available devices, and apply. The app does not change endpoints automatically. Only mono/stereo devices are supported; set multichannel headsets to stereo in Windows sound settings.

Exiting or crashing the app interrupts microphone forwarding through the cable. To restore normal voice chat, select your physical microphone in Discord or the game. A second app instance is blocked to prevent duplicate microphone forwarding.

## Voice-chat validation

Use voice activation for the first test. Select CABLE Output explicitly. If tracks disappear during silence, lower the input threshold and test with noise suppression, echo cancellation, and automatic gain control disabled. Settings vary by voice client. Use headphones while these features are disabled. Music and quiet passages need a teammate's check; playback does not override the voice client's gate.

Test these cases before relying on it during a game:

- Speech alone, music alone, and both together; no microphone monitoring in headphones.
- Rapid F8 presses, pause/resume, replacement, stop during loading or resume, natural completion, and a long track.
- Hide/reopen, hotkey conflicts, held keys, and borderless/fullscreen game focus.
- Local and YouTube import during playback; cancellation and invalid URLs; offline playback after restart.
- Unplug/reconnect, sleep/resume, missing files, and restart with unavailable endpoints.

Exclusive fullscreen may prevent the launcher from appearing. Test borderless mode. No game compatibility is claimed yet.

## Build and validation

Install the .NET 10 SDK. Run:

```powershell
dotnet build src/MicSomeNoise -c Release
dotnet run --project tests/MicSomeNoise.Checks -c Release
./scripts/publish.ps1 -SelfContained
```

Omit `-SelfContained` for the smaller package that requires the Desktop Runtime.

## GitHub releases

Windows checks run on pull requests and pushes to `main` that change code, tests, scripts, or workflows. Documentation-only changes skip CI. New commits cancel older checks for the same branch or pull request. Routine checks do not package or upload artifacts.

To release, update the project version and the version shown in the app and docs, then commit and push the changes. Tag that commit with the matching version:

```sh
git tag v0.1.12
git push origin v0.1.12
```

The release workflow checks the tag against the project version, runs the checks on Windows, and publishes a self-contained x64 ZIP with a SHA-256 checksum. It uploads files to a draft before publishing, so a failed upload does not leave a public release without its download. Rerun a failed job from Actions; a draft can be retried, but an already published release requires a new version.

No personal access token or repository secret is needed. The workflow uses GitHub's temporary token with `contents: write`; routine checks have read-only access. Actions are pinned to commit hashes. Builds are unsigned and do not run audio devices or game integration tests.

## License

[MIT](LICENSE). ZIPs include the app, NAudio, and bundled .NET license notices in `licenses/`. VB-CABLE and the optional import tools are installed separately under their publishers' licenses.

The audio engine uses NAudio 2.2.1, shared-mode WASAPI, 48 kHz stereo mixing, incremental decoding, and bounded audio buffers. Headphone playback consumes a copy of the decoded track; microphone audio goes only to broadcast. Buffer correction drops old frames when devices drift. This may cause an audible discontinuity and requires hardware validation.

The first build was cross-compiled on macOS. Compilation and the buffer checks do not validate WPF rendering, Windows devices, voice transmission, or YouTube extraction. These require the Windows tests above.

Performance goals remain unmeasured: under 100 MB working set, under 1% total-machine idle CPU with microphone forwarding, and under 100 ms warm audible response at the 95th percentile. Record CPU model, RAM, Windows version, device formats, library size, and Release package type. Measure 60 seconds of hidden idle and sustained playback. For latency, record a repeated start signal and actual headphone output; decoder timing alone is insufficient. Self-contained package size is separate from process memory.

## Version 0.1.2 audio fix

Fixed microphone leakage into headphones and accumulating distortion. NAudio supplies byte-backed float buffers; the previous Array.Clear call cleared only a quarter of each output buffer. The audio engine now clears every float sample explicitly. A regression test exercises the real NAudio SampleToWaveProvider adapter with reused byte buffers. It failed before the fix and passes after it. Temporary diagnostic logging has been removed. Confirm the fix on Windows by speaking with no track playing, then playing and stopping a track while speaking.

## Package contents

Release packages include English framework resources only and omit app debug symbols and development reports. The packaged README is the short QUICKSTART.md guide. Publishing uses a fresh staging directory so previous builds cannot leave extra resources in the ZIP. Runtime DLLs remain included for self-contained execution.

Choose a track, then hide the launcher with Escape, return to the game, hold push-to-talk, and press F8. F9 stops playback. Selection survives restart. If push-to-talk uses Ctrl, Alt, or Shift, configure the play/stop shortcuts with that held modifier, or choose a non-modifier push-to-talk key. Shortcuts report conflicts and ignore held-key repeats. Existing installations using the old Ctrl+Alt+S stop default switch to F9 when the play shortcut is first added.

The persistent **Ready** label identifies the selected track. Searching keeps the choice even if the track is hidden by the filter. Clicking another track or using the arrow keys changes it. Play and F8 use that same selection. The choice is saved immediately across restarts. Removing the selected library entry clears the ready state.

## Automatic push-to-talk

Settings includes an optional **Hold push-to-talk while a track plays** checkbox and a configurable single key. It defaults off, with V suggested. Bind the same key in the game. The key must differ from all app shortcut keys; modifiers/chords are unsupported so holding it does not change F8/F9 matching.

With the game focused, F8 decodes the selected track, presses the configured key, waits for the configured delay, then starts the track. The delay defaults to 150 ms and accepts 0–2000 ms in Settings. It applies only when automatic push-to-talk is enabled. F9 also cancels tracks waiting in this delay. Stop/F9 cancels pending playback and releases the key immediately. Natural completion releases after a 120 ms output-buffer allowance. Errors, audio restart, disabling the setting, and normal exit release it. Test-preview playback does not activate it. No audio recording or microphone forwarding behavior changes.

The implementation uses Windows [SendInput](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput) with scan codes. Windows can block input due to privilege isolation, and games may ignore synthetic input. Blocked key-down aborts playback; failed key-up reports an error and remains eligible for retry through Stop. Forced process termination cannot guarantee release. Physically press/release the key if necessary. Stay in the game during playback and do not manually hold the configured key at the same time. Playback from the launcher while automatic push-to-talk is enabled is rejected to avoid typing into it.

Managed checks use a fake OS sender. They cover disable-by-default, hold/release pairing, natural EOF, stale completion timers, stop, shutdown, input failures, and test-preview isolation. Windows input delivery and game acceptance remain manual checks.

## Track volume shortcuts

F10 lowers track volume and F11 raises it, in five-percentage-point steps between 0% and 100%. Both are global, configurable in Settings, and checked against other shortcuts and the push-to-talk key. They change the track level in headphones and broadcast, leave microphone gain unchanged, update the launcher slider/percentage, and save the level immediately.

## Pause and resume

The main button and F8 act on the selected track. If that track is playing, they pause it; if it is paused, they resume it at its retained position. Selecting another track and pressing F8 replaces the active track. F9 discards the retained position. Paused positions are not saved across app restarts.

Pause stops track decoding and clears queued headphone monitoring. Device buffers can still contain a short tail. Microphone forwarding stays active. Automatic push-to-talk releases on pause; resuming presses it again and uses the configured lead-in delay. Stop and another pause can cancel that delay. The main button reflects Play, Pause, or Resume, and the current-track label distinguishes paused playback.

For Discord on the physical microphone and a game on VB-CABLE, Discord receives voice independently. Pausing does not mute the microphone. The setting below controls whether the game receives voice during track playback.

## Microphone during tracks

Settings has **Include my microphone while a track plays**, off by default. Off sends only the track through the cable during playback and the automatic push-to-talk delay. On mixes your voice with the track. Pause and Stop restore normal microphone forwarding. Natural completion keeps it muted through the short push-to-talk release tail, then restores it. Microphone capture keeps draining while muted to avoid replaying old speech.

Discord remains independent when its input is your physical microphone or NVIDIA Broadcast. This setting changes only this app's mix sent to the game through VB-CABLE. It does not mute the microphone device globally. Audio already queued in Windows or the virtual cable can still produce a short transition tail; verify transitions with your game.
