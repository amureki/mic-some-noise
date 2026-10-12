# Mic Some Noise

Play sounds and music in voice chat without leaving your game. Mic Some Noise runs on Windows 11 and sends your microphone and selected track to Discord or your game through VB-CABLE.

[Download for Windows](https://github.com/amureki/mic-some-noise/releases/latest/download/MicSomeNoise-win-x64.zip)

## Get started

1. Install [VB-CABLE](https://vb-audio.com/Cable/). Restart Windows if the installer asks you to.
2. Extract the downloaded ZIP and run `MicSomeNoise.exe`. Keep the extracted files together.
3. Open Settings. Choose your microphone and headphones, then choose **CABLE Input** as the broadcast output. Click **Apply and start audio**.
4. In Discord or your game, choose **CABLE Output** as the microphone input and your headphones as the output.
5. Open **Import / library** and add a WAV or MP3 file. Select a track, return to your game, and press **F8** to play it.

Use headphones to avoid feedback. Leave Windows "Listen to this device" off for your microphone and VB-CABLE.

Your voice passes through while no track is playing. During playback, the app sends only the track by default. To speak over it, turn on **Include my microphone while a track plays** in Settings.

## Controls

| Shortcut | Action |
| --- | --- |
| F8 | Play, pause, or resume the selected track |
| F9 | Stop and return to the start |
| F10 / F11 | Lower / raise track volume |
| Ctrl+Alt+Space | Open the launcher and search |
| Escape | Hide the launcher |

Click a track or use the arrow keys to select it. Selecting a track does not play it. Shortcuts work while the launcher is hidden, and you can change them in Settings.

Use the tray menu to exit. Closing the launcher hides it and keeps audio running.

## Push-to-talk

If your game uses push-to-talk, hold that key while playing a track. If the key is Ctrl, Alt, or Shift, add the same modifier to the app's playback shortcuts in Settings.

To let the app hold the key for you, enable **Hold push-to-talk while a track plays** in Settings. Choose the same single key you use in the game, different from the app's shortcuts. Select a track, return to the game, and press F8. Keep the game focused during playback. Some games do not accept simulated key presses.

## Import from YouTube

YouTube import needs yt-dlp, FFmpeg, and Deno. On Windows, run the included `install-import-tools.ps1` script in PowerShell, then restart the app.

In **Import / library**, paste a public video URL and click **Import YouTube**. Once imported, the track works offline. Playlists, live videos, and restricted videos are not supported. Only download audio you have permission to use and that the service allows you to download.

## Check your audio

Open **Test microphone + track**, click **Record 10 seconds**, then speak and play a track. Click **Listen** to hear the mix through your headphones. Your voice-chat broadcast stays active during the test.

If the recording sounds right but your friends cannot hear the track, check your voice-chat input, voice activation threshold, and noise suppression settings. Ask a friend to confirm what they hear.

If audio stops after sleep or a device change, open Settings, select your devices again, and click **Apply and start audio**.

If you exit the app, switch Discord or your game back to your physical microphone to keep talking.

## Updates and your library

Get updates from [Releases](https://github.com/amureki/mic-some-noise/releases). Exit the app, extract the new ZIP into a new folder, and run it. Your settings and library carry over.

The app keeps settings and copied audio in `%LOCALAPPDATA%\MicSomeNoise`. Back up that folder to keep your library. Removing a track from the library leaves its copied audio file in that folder.

The app is unsigned, so Windows may show an unknown-publisher warning.

## License

[MIT](LICENSE).
