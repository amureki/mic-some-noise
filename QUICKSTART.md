# Mic Some Noise

Play sounds and music in voice chat without leaving your game.

## Set up

1. Install [VB-CABLE](https://vb-audio.com/Cable/). Restart Windows if the installer asks you to.
2. Extract the ZIP, open the versioned folder, and run `MicSomeNoise.exe`. Keep the extracted files together.
3. Open Settings. Choose your microphone and headphones, then choose **CABLE Input** as the broadcast output. Click **Apply and start audio**.
4. In Discord or your game, choose **CABLE Output** as the microphone input and your headphones as the output.
5. Click **Import audio file** in the launcher and add a WAV or MP3 file. Select a track, return to your game, and press **F8**.

Use headphones to avoid feedback. Leave Windows "Listen to this device" off for your microphone and VB-CABLE.

Your voice passes through while no track is playing. During playback, the app sends only the track by default. Hold **F6** to speak over the track; release it to return to track-only audio. This works while the launcher is hidden. Change **Hold to speak over a track** in Settings, or leave it blank to disable. To keep your microphone on throughout playback, enable **Include my microphone while a track plays** instead. Your microphone must be unmuted on the device and in Windows. F6 controls the app’s mix; your game’s push-to-talk rules still apply. Use a different key from the app shortcuts and automatic game push-to-talk.

## Play a track

Click a track or use the arrow keys to select it. Selecting a track does not play it. Use Enter in search or the track list to play, pause, or resume the selected result. Previous/Next and F7/F9 cycle through the search results in name order and wrap at either end. Clear search to cycle through the full library. Use **Stop** in the launcher or tray menu to stop playback.

| Shortcut | Action |
| --- | --- |
| Hold F6 | Mix your microphone with the playing track while held |
| F7 | Select the previous track |
| F8 | Play, pause, or resume the selected track |
| F9 | Select the next track |
| F10 / F11 | Lower / raise track volume |
| Ctrl+Alt+Space | Open the launcher and search |
| Escape | Hide the launcher |

Shortcuts work while the launcher is hidden. Change them in Settings. Use the tray menu to exit. Closing the launcher hides it and keeps audio running.

When the launcher is hidden or minimized, a small semi-transparent bar shows the selected track and playback state at the top of the active screen. If a different track is still playing, it appears on a second line. Mouse input passes through to your game. Toggle **Show game overlay** in the tray menu to hide or restore it. Use windowed or borderless mode if exclusive fullscreen hides the overlay.

Widen the launcher to show two columns of tracks; a narrow window uses one column. Use a track’s **Track actions** menu to rename it or remove it from the library. With the list focused, F2 renames and Delete removes. **Undo** restores removals in reverse order until you exit the app. Removing the playing track stops playback. Audio files stay on disk.

## Use push-to-talk

Hold your game's push-to-talk key while playing a track. If the key is Ctrl, Alt, or Shift, add the same modifier to the app's playback shortcuts in Settings.

To let the app hold the key for you, enable **Hold push-to-talk while a track plays** in Settings. Choose the same single key you use in the game, different from the app's shortcuts. Select a track, return to the game, and press F8. Keep the game focused during playback. Some games do not accept simulated key presses.

If a crash leaves the key held, press and release it yourself.

## Import from YouTube

Run the included `tools/install-import-tools.ps1` script in PowerShell to install the required tools, then restart the app.

Paste a public YouTube video URL into the launcher search field and press **Enter**. Press **Esc** to return to your game while it downloads. When ready, the track is selected unless you selected another track during the import. A notification confirms completion without opening the launcher. Press **F8** to play.

Links with playlist or tracking parameters work: only the linked video is imported. An existing video is selected without downloading it again. Use **Cancel import** in the launcher to cancel. Imported tracks work offline. Playlist-only links, live videos, and restricted videos are not supported. Only download audio you have permission to use and that the service allows you to download.

## Check your audio

Open Settings, expand **Test microphone + track**, click **Record 10 seconds**, then speak and use **Play / pause selected track**. Apply your audio devices first if audio is not running. Click **Listen** to hear the mix through your headphones. Your voice-chat broadcast stays active during the test.

If the recording sounds right but your friends cannot hear the track, check your voice-chat input, voice activation threshold, and noise suppression settings.

If audio stops after sleep or a device change, open Settings, select your devices again, and click **Apply and start audio**.

If you exit the app, switch Discord or your game back to your physical microphone to keep talking.

## Updates and your library

Run `MicSomeNoise.exe` from the extracted folder. It includes the required .NET/WPF runtime and NAudio audio libraries; no separate .NET installation is needed. Native libraries extract to `%TEMP%/.net` when the app starts.

`README.md` contains these instructions. `tools/` contains the optional YouTube import installer. `licenses/` contains license terms and third-party notices; keep it with the app when you redistribute it. Local WAV and MP3 playback does not need the import tools.

Get updates from [Releases](https://github.com/amureki/mic-some-noise/releases/latest). Each ZIP and its app folder include the version, for example `MicSomeNoise-0.1.16-win-x64.zip` and `MicSomeNoise-0.1.16-win-x64/`. Exit the app, extract the new ZIP, and run the app from its new versioned folder. Your settings and library carry over.

Back up `%LOCALAPPDATA%\MicSomeNoise` to keep your settings and audio. Removing a track from the library leaves its copied audio file in that folder.
