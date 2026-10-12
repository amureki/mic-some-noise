# Mic Some Noise

Download updates from [GitHub Releases](https://github.com/amureki/mic-some-noise/releases/latest). Exit the app through its tray menu, extract the new ZIP into a new folder, and run it. Settings and the library remain in LocalAppData.

## Setup

1. Install VB-CABLE from https://vb-audio.com/Cable/ and restart Windows if requested.
2. Extract this ZIP into a new folder. Run MicSomeNoise.exe. The self-contained build includes .NET.
3. In Settings, select your microphone, physical headphones, and CABLE Input as broadcast output. Apply.
4. In Discord or the game, select CABLE Output as input and your headphones as output.

Do not enable Windows "Listen to this device" for the microphone or cable. If the app exits, select your physical microphone in voice chat to restore voice.

## Playback and test

Import WAV/MP3 files with Import / library. Search and click a track, or use the arrow keys, to select it without playing. The Ready label shows your selection. Search preserves the choice even when it hides the selected track. Your selection is saved across restarts.

F8 plays the selected track, pauses it, or resumes it without opening the app. The main button changes between Play, Pause, and Resume and does the same thing. Selecting another track and pressing F8 starts that track. F9 stops and resets playback to the beginning. F10 lowers track volume by 5%; F11 raises it by 5%. These work while the app is hidden and do not change microphone volume. Ctrl+Alt+Space opens the launcher. These global shortcuts are configurable. Escape or Close hides the launcher; audio continues.

Select a track, press Escape, return to the game, hold push-to-talk, and press F8. If push-to-talk holds Ctrl, Alt, or Shift, include that modifier in the play/stop shortcuts. Use the tray menu to exit.

Expand Test microphone + track. Record up to ten seconds, then Listen through headphones. This tests the outgoing mix before VB-CABLE and Discord. Live broadcast continues. The test stays in memory; Clear deletes it.

## Optional automatic push-to-talk

In Settings, enable **Hold push-to-talk while a track plays**, enter the game's push-to-talk key, and apply. It is off by default. The suggested key is V; it must match your game. Use a single letter, digit such as D1, or function key, different from the app's shortcut keys. Modifier keys and chords are not supported.

Select a track, return to the game, and press F8. The app presses the key, waits 150 ms by default, then starts the track. Adjust Delay before track starts in Settings from 0 to 2000 ms. F9 cancels a waiting track and releases the key immediately. The app holds the configured key during the track and releases it after the final audio buffer drains. Pause also releases it immediately. Resume presses it again and waits for the configured delay. Playback errors, applying settings, and normal exit also release it. Test-recording playback never presses the key. Do not manually hold the key at the same time.

Automatic playback requires another app to have focus; clicking Play or Resume while this launcher has focus shows a message instead of typing into the launcher. Keep the game focused while playing. Some games reject simulated input. Windows also blocks input across certain permission levels. If a crash or forced termination leaves the key held, physically press and release it. Switching apps during playback can affect where the key release is received.

## YouTube import

Install yt-dlp, FFmpeg, and Deno with install-import-tools.ps1 in PowerShell. Restart the app afterward. Alternatively, put yt-dlp.exe, ffmpeg.exe, ffprobe.exe, and deno.exe in a tools folder beside the app.

Import one public, non-live video that you have permission to download. Completed imports work offline. Update yt-dlp if extraction fails.

## Data and recovery

Settings and copied audio are in %LOCALAPPDATA%\MicSomeNoise. Removing a library entry retains the audio file. Back up this folder to keep your library.

After device changes or sleep, reopen Settings and apply the devices again. If Discord cuts off audio but the local test is clean, check its voice activation and audio processing settings.

Keep the runtime DLLs beside the executable. This package contains English framework resources only. App and dependency license notices are in `licenses/`.

## Microphone during tracks

Settings has **Include my microphone while a track plays**, off by default. Off sends only the track through the cable during playback and the automatic push-to-talk delay. On mixes your voice with the track. Pause and Stop restore normal microphone forwarding. Natural completion keeps it muted through the short push-to-talk release tail, then restores it. Microphone capture keeps draining while muted to avoid replaying old speech.

Discord remains independent when its input is your physical microphone or NVIDIA Broadcast. This setting changes only this app's mix sent to the game through VB-CABLE. It does not mute the microphone device globally. Audio already queued in Windows or the virtual cable can still produce a short transition tail; verify transitions with your game.
