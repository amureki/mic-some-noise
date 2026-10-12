# Windows soundpad plan

Status: core requirements, launcher interface, C#/.NET 10/WPF/NAudio stack, and initial performance targets agreed. Version one uses local playback, including audio imported from YouTube URLs. Import tooling, Windows audio routing, and measured performance still need validation.

## Goal

Build a lightweight Windows 11 app that lets the user search for and play sounds or music to teammates through voice chat. The design must work independently of any specific game.

## Confirmed requirements

- Run on Windows 11 for personal use and testing. Public distribution is outside the current scope.
- Maintain a local audio library with local-file import and YouTube URL import.
- Download YouTube audio before playback. Live YouTube search and streaming are outside version-one scope.
- Play only one sound or music track at a time. Microphone audio remains independent of this limit.
- Send the selected track to voice chat and the user's headphones.
- Use voice activation as the primary voice-chat test mode.
- Use a compact launcher-style search window with separate global open and stop hotkeys.
- Keep resource use and the interface small. Use C# with .NET 10, WPF, and NAudio.
- A one-time virtual audio driver installation is acceptable.

## Context

The repository has no application code yet. Planning takes place on macOS. The user can test builds on a Windows 11 PC, but its architecture and microphone/headset setup are not yet known. WPF UI and Windows audio behavior require Windows validation.

The game title is not a design requirement. Select a game the user plays for compatibility testing before claiming game support.

The user does not know whether a virtual audio driver is installed. Detect available devices during setup and guide installation if needed. The driver is not yet selected.

## Selected technology stack

| Component | Choice | Purpose |
| --- | --- | --- |
| Language and runtime | C# with .NET 10 LTS | Application logic and background work |
| Desktop interface | WPF | Compact search launcher, library/import view, and settings |
| Audio | NAudio over WASAPI | Microphone capture, local playback, mixing, and resampling |

This stack balances Windows integration, implementation effort, and performance for one microphone and one active track. It does not require a browser for the interface or playback. Pin a stable NAudio release after validating the APIs needed by the routing prototype.

Keep the audio engine independent of the WPF window lifetime and UI thread. Decode the selected track once and distribute its audio to separate headphone and broadcast buffers. Use bounded buffers, minimize allocations during audio processing, and handle sample-rate differences and device clock drift.

Supporting recommendations remain Windows RegisterHotKey for global shortcuts, local files and JSON for library metadata/settings, and import-only subprocesses for yt-dlp, a supported JavaScript runtime, and FFmpeg. VB-CABLE is the candidate virtual device; verify it in the routing prototype. Start with a folder-based build using an installed .NET Desktop Runtime for personal testing. A self-contained package is an alternative if installation convenience outweighs package size.

## Agreed initial performance targets

These are acceptance targets, not measured results. Validate a Release build without a debugger on the user's Windows 11 PC and record the CPU, memory, audio devices, formats, buffer settings, and library size.

| Metric | Target | Measurement scope |
| --- | --- | --- |
| App memory | Under 100 MB | App process working set after warm-up, both idle and during sustained local playback |
| Idle CPU | Under 1% | Average total-machine CPU used by the app over 60 seconds, launcher hidden, no track playing, microphone forwarding active |
| Warm playback response | Under 100 ms | From acceptance of the playback command to the first corresponding sound at the local output, with the app and audio engine already running |

Measure playback response with a test file that begins with a known audible signal, across repeated starts, and report the median and 95th percentile. Use the 95th percentile to assess the response target. Instrumentation must account for output buffering; timing a decoder call alone does not measure audible response.

Import subprocess memory and CPU are outside the normal playback targets and must be reported separately. Discord/network transmission delay is outside the local response target. Also report playback CPU, cold startup time, launcher-open time, stop latency, and package size; their limits remain open. Verify that importing does not cause audible playback gaps.

## Agreed interface

- The open hotkey shows a small search window above the current app and focuses its search field. Opening it does not stop playback.
- Search covers the local library, including completed YouTube imports. Playback search requires no network connection.
- Up and Down select a result.
- Enter replaces the current track with the selected result, starts playback, and keeps the launcher open.
- Escape dismisses the launcher without changing playback.
- The separate stop hotkey stops the active track and cancels pending playback without opening the launcher. Microphone forwarding continues.
- Playback ends naturally. No looping is required for version one.
- Reopening the launcher shows the current track, a stop button, and a volume control.
- The app stays in the system tray when the launcher is closed. A permanent overlay is outside version-one scope.
- Dismissing the launcher returns focus to the previous app where possible. Validate focus and visibility in fullscreen and borderless games; do not assume an ordinary topmost window works over every fullscreen mode.

## Import and library

The agreed YouTube workflow is to paste a URL, download and convert its audio, name the track, and add it to the local library. This replaces the earlier requirement for live YouTube search and playback.

Proposed implementation details:

- Provide a library management and import view accessible from the tray or launcher.
- Accept local WAV and MP3 files. Select the stored format for YouTube imports after checking decoder support, quality, and storage use.
- Evaluate yt-dlp with a supported JavaScript runtime and FFmpeg for downloading and conversion. These are candidates, not verified dependencies.
- Show import progress, cancellation, and actionable failure messages. Import completion must not start playback automatically.
- Download and convert into temporary files. Add the track to the searchable library only after validating the completed audio file. Remove incomplete files after failure or cancellation.
- Preserve the source URL and original title alongside the editable library name. Detect repeated imports by source identity.
- Keep importing independent of playback. Stop playback and cancel import are separate controls.
- Start with single public, non-live videos that require no account. Defer playlists, login, browser-cookie import, restricted videos, and automatic source refresh.
- Use content the user has permission to download and use. Personal testing does not remove YouTube's service restrictions.

YouTube extraction can fail or require updates. Such failures must affect only new imports; existing library files remain playable offline. No embedded YouTube player, persistent browser, or process audio capture is needed for the selected playback design.

## Audio and application behavior

```text
Local audio file --> decoder --+--> headphones
                              |
                              v
Physical microphone ------> mixer --> virtual audio playback endpoint
                                                  |
                                                  v
                                      virtual recording endpoint
                                                  |
                                                  v
                                         Discord / game input
```

Use an existing virtual audio driver for the routing prototype. Select it and check installation requirements before packaging. Installation and voice-chat input selection add setup work, but avoid custom driver development.

The app must keep microphone forwarding active when tracks stop. Hiding the launcher must leave the audio engine running. Exiting or crashing the app may interrupt voice on this route; the setup guide must explain how to return to the physical microphone.

Discord and games still control voice transmission. Validate noise suppression and voice activation with speech, music, and quiet passages. A playback hotkey does not bypass the destination app's voice settings.

Proposed details:

- Control microphone and track volume separately and prevent clipping at the mixed output.
- Do not play microphone audio back through headphones. Separate headphone and broadcast track volume controls remain a proposed detail.
- Stop the previous track before starting another. Cancel stale load requests so a stopped or replaced track cannot start later.
- Read and decode long tracks incrementally instead of loading full music files into memory.
- Save the library, hotkeys, volume, and selected devices locally.
- Report hotkey conflicts, missing files, and disconnected devices clearly.
- Use explicit device selection. Do not silently change Windows default devices.

## Outside version one

Live YouTube search and streaming, embedded video players, browser/application audio capture, general-purpose recording/export, editing, effects, accounts, cloud sync, per-track hotkeys, looping, and custom audio drivers.

## Open decisions

1. Which existing virtual audio driver should the app use, and how should setup guide installation?
2. Which import tools, versions, and stored audio format meet packaging and maintenance needs?
3. Which CPU architecture and microphone/headset does the Windows 11 test PC use?
4. Which game will be used for compatibility testing? This need not block the initial design.
5. Which default key combinations should open the launcher and stop playback? Both must be configurable and checked for conflicts.
6. Should headphone and broadcast volumes be separate? Should music continue at the same volume while the user speaks?
7. Which voice activation and noise suppression settings preserve speech and music on the user's setup?
8. What limits should apply to playback CPU, package size, cold startup, launcher-open time, and stop latency after baseline measurement? Memory, idle CPU, and warm playback response targets are agreed above.
9. Where should library files be stored, should local imports be copied or referenced, and what should removal from the library delete?

## Delivery plan

### 1. Set technical targets

Identify the Windows test setup and use the selected stack and agreed performance targets for a small prototype. Select candidate import tools. Resolve remaining playback and storage details before implementing them.

### 2. Prove local playback and voice routing on Windows

Use C#/.NET 10 and NAudio to combine microphone audio and one local file. Send the mix into Discord and a selected game, with the track also audible through headphones. Verify continued playback with the launcher hidden, separate stop control, voice activation, quiet passages, device reconnects, and absence of feedback. Measure initial memory, idle CPU, and warm playback response against the agreed targets before building the full WPF interface.

Exit condition: the user and a teammate hear the intended track, the teammate also hears speech, and there is no overlapping playback, duplicate monitoring, persistent distortion, or unacceptable voice delay.

### 3. Prove YouTube URL import

Using permitted test content, download and convert one public video into a validated local file. Test naming, cancellation, failed imports, and duplicate detection. Disconnect the network and play the completed import through the same audio route as other local files.

Exit condition: imports produce usable local tracks; failed or cancelled imports do not appear as playable entries or damage existing tracks. Playback requires neither network access nor a running importer.

### 4. Build the application

Build the NAudio audio core and WPF interface with compact local search, keyboard navigation, current-track controls, library/import view, separate global open and stop hotkeys, tray menu, and settings. Check registration conflicts and focus restoration. Avoid game hooks and input injection.

### 5. Validate and package

Measure idle and playback CPU, memory, startup time, and hotkey response on Windows. Test long tracks, missing files, sleep/resume, device removal, restart, clipping, held-key repeat behavior, and importing while a track plays. Document driver setup, target-app input selection, and microphone recovery. Package for personal testing.

## Acceptance checks

- The open hotkey shows the launcher with search focused while Discord or a supported game has focus, without interrupting playback.
- Search finds local files and completed YouTube imports offline.
- Arrow keys select; Enter replaces playback and keeps the launcher open; Escape dismisses without changing playback.
- Reopening shows the current track, stop button, and volume control.
- The separate stop hotkey stops playback with the launcher closed. A track otherwise plays until it ends.
- Only one track plays, including during rapid selection changes or delayed file loading.
- Stop cancels pending playback while microphone forwarding continues.
- The user can import audio from a permitted YouTube URL, name it, and play it offline after restarting the app.
- Failed or cancelled imports leave no playable partial entries. Import completion does not interrupt or start playback.
- Teammates hear speech and the track together. The user hears the track without microphone feedback or duplicate audio.
- Voice activation transmits tracks while the user is silent and preserves speech during playback under the documented settings.
- Settings survive restart. Missing files and unavailable devices produce actionable errors.
- The Release build meets the under-100-MB memory, under-1% idle CPU, and under-100-ms warm playback response targets under the measurement conditions above on the named Windows test machine.

## Research and sources

Documentation reviewed on 2026-10-11. No Windows playback or import prototype has run.

Earlier research found restrictions on hidden official YouTube players and audio isolation. The user chose URL import and local playback instead of live integration. This removes the browser from playback, but does not remove download restrictions or extractor maintenance.

- [VB-CABLE reference manual](https://vb-audio.com/Cable/VBCABLE_ReferenceManual.pdf): virtual playback-to-recording routing.
- [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core): .NET 10 LTS support.
- [WPF overview](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/): Windows desktop interface framework.
- [NAudio](https://github.com/naudio/NAudio): audio capture, playback, mixing, and resampling.
- [.NET deployment options](https://learn.microsoft.com/en-us/dotnet/core/deploying/): framework-dependent and self-contained deployment.
- [Microsoft RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey): global hotkeys and restrictions.
- [Discord voice input modes](https://support.discord.com/hc/en-us/articles/211376518-Voice-Input-Modes-101-Push-to-Talk-Voice-Activated): voice activation and push-to-talk.
- [Discord troubleshooting](https://support.discord.com/hc/en-us/articles/360045138471-Discord-Voice-and-Video-Troubleshooting-Guide): input devices and voice processing.
- [YouTube service terms](https://uk.youtube.com/t/terms): content use and automated access restrictions.
- [YouTube developer policies](https://developers.google.com/youtube/terms/developer-policies): restrictions relevant to the rejected embedded-player approach.
- [yt-dlp documentation](https://github.com/yt-dlp/yt-dlp): download and audio conversion support.
- [yt-dlp JavaScript requirements](https://github.com/yt-dlp/yt-dlp/wiki/EJS) and [token guide](https://github.com/yt-dlp/yt-dlp/wiki/PO-Token-Guide): dependencies and access limitations.

## Version 0.1.0 implementation

Implemented on 2026-10-11 for the confirmed Windows 11 x64 test PC:

- WPF launcher, search, tray controls, configurable open/stop hotkeys, and device settings.
- NAudio 2.2.1 shared-mode WASAPI routing, separate microphone gain, common track volume, incremental decoding, and bounded buffers.
- Copied WAV/MP3 imports, cancellable yt-dlp/FFmpeg/Deno YouTube imports, MP3 storage, naming, source identity, and atomic JSON writes.
- Self-contained x64 package, Windows build workflow, setup/recovery guide, and local buffer checks.

VB-CABLE is the first test driver. Data lives in LocalAppData. Removing a library entry retains its copied audio file. Hotkeys default to Ctrl+Alt+Space and Ctrl+Alt+S. Device recovery is manual through Settings.

Release compilation and managed buffer checks passed on macOS. Windows audio routing, UI rendering, focus, import extraction, and all performance acceptance targets remain pending on the test PC. This implementation does not establish the routing prototype's Windows exit condition.

## Version 0.1.3 feedback changes

The user confirmed the microphone leakage fix in version 0.1.2. Discord playback can still sound cut off; changing its mode to Studio improved the user's result, but the downstream cause is not yet established.

Track playback now leaves the launcher open. Escape and the close button still hide it.

The launcher includes an optional ten-second in-memory recording of the outgoing microphone-and-track mix. Playback is headphone-only, with finish and clear controls. It tests the app's mix before VB-CABLE and Discord. Live broadcast continues. Export and general-purpose recording remain outside scope.

Managed tests cover capture, headphone-only preview, absence of preview feedback into broadcast, byte-backed buffers, stopping, clearing, and the duration limit. Windows UI and hardware confirmation of the new controls remains pending.

## Version 0.1.5 hotkeys

F8 plays the selected track globally without showing the launcher or changing focus. F9 stops playback. Open, play, and stop shortcuts are configurable and checked for conflicts. Click/arrows select without playback; Enter or Play selected explicitly starts playback. Selection persists across restart. The old default stop shortcut migrates to F9 on upgrade; custom shortcuts remain. Windows/game hotkey validation is pending.

## Version 0.1.6 explicit preselection

Set for F8 explicitly saves the track that the global play shortcut will play. A persistent ready label names it. Browsing, searching, and Play now do not change preselection. Enter and Play now use the highlighted result. Removing the preselected library entry clears the ready state.

## Version 0.1.7 simplified selection

Supersedes the explicit preselection flow in 0.1.6. Click or arrows select one ready track without playback. F8 and Play use that same selection; F9 stops. Search preserves the choice even when the selected track is filtered out. Selection persists across restarts. Set for F8, Play now, and the separate Enter playback action are removed.

## Version 0.1.8 optional push-to-talk

At the user's request, optional configured single-key SendInput automation now holds push-to-talk while a track plays. It defaults off. Natural completion releases after an output-buffer allowance; F9/Stop, error, settings changes, and normal exit release immediately. This supersedes the earlier exclusion of input injection. No game hooks or anti-cheat bypasses are used. Windows/game compatibility remains unverified.

## Version 0.1.9 push-to-talk lead-in

Automatic push-to-talk now waits 150 ms after key-down before starting the track. Settings accepts 0–2000 ms. Disabled push-to-talk adds no delay. Stop and track replacement invalidate pending starts; regression checks exercise the actual file-decoder/start path with disposable local WAV fixtures and no audio hardware.
