using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

namespace MicSomeNoise;

public sealed class Importer(Store store)
{
    public async Task<Track> Local(string source, string name, CancellationToken token)
    {
        string extension = Path.GetExtension(source).ToLowerInvariant();
        if (extension is not ".wav" and not ".mp3") throw new InvalidOperationException("Choose a WAV or MP3 file.");
        return await Commit(source, name, null, Path.GetFileNameWithoutExtension(source), token);
    }
    private async Task<Track> Commit(string source, string name, string? url, string title, CancellationToken token)
    {
        string id = Guid.NewGuid().ToString("N");
        string destination = Path.Combine(Store.Audio, id + Path.GetExtension(source));
        try
        {
            await Task.Run(() =>
            {
                using var input = new AudioFileReader(source);
                if (input.TotalTime <= TimeSpan.Zero || input.Read(new float[4096], 0, 4096) == 0)
                    throw new InvalidOperationException("The audio file is empty or cannot be decoded.");
                if (input.WaveFormat.Channels > 2) throw new InvalidOperationException("Import mono or stereo audio.");
                token.ThrowIfCancellationRequested();
                File.Copy(source, destination);
            }, token);
            token.ThrowIfCancellationRequested();
            var track = new Track(id, string.IsNullOrWhiteSpace(name) ? title : name.Trim(), destination, url, title);
            store.Tracks.Add(track);
            try { store.SaveLibrary(); } catch { store.Tracks.Remove(track); throw; }
            return track;
        }
        catch { if (File.Exists(destination)) File.Delete(destination); throw; }
    }
    public async Task<Track> Youtube(string url, string name, IProgress<string> progress, CancellationToken token)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            !(uri.Host == "youtu.be" || uri.Host == "youtube.com" || uri.Host.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Enter an HTTPS YouTube video URL.");
        string temp = Path.Combine(Store.Root, "Imports", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var start = new ProcessStartInfo(Tool("yt-dlp.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string arg in new[] { "--ignore-config", "--no-playlist", "--no-overwrites", "--newline", "--no-colors", "--progress", "--progress-delta", "0.5", "--match-filter", "!is_live & !was_live", "--write-info-json", "--extract-audio", "--audio-format", "mp3", "--audio-quality", "2", "--js-runtimes", "deno:" + Tool("deno.exe"), "--ffmpeg-location", Tool("ffmpeg.exe"), "-o", Path.Combine(temp, "audio.%(ext)s"), "--", url }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start yt-dlp.");
            using var cancellation = token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
            var errors = new StringBuilder();
            async Task Drain(StreamReader stream)
            {
                while (await stream.ReadLineAsync() is { } line)
                {
                    progress.Report(line);
                    if (errors.Length < 12000) errors.AppendLine(line);
                }
            }
            await Task.WhenAll(Drain(process.StandardOutput), Drain(process.StandardError), process.WaitForExitAsync());
            token.ThrowIfCancellationRequested();
            if (process.ExitCode != 0) throw new InvalidOperationException("YouTube import failed. Update yt-dlp and check that the video is public and unrestricted.\n" + errors);
            using var info = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(temp, "audio.info.json"), token));
            string id = info.RootElement.GetProperty("id").GetString()!;
            string canonical = "https://www.youtube.com/watch?v=" + id;
            if (store.Tracks.Any(t => t.SourceUrl == canonical)) throw new InvalidOperationException("This video is already in your library.");
            string title = info.RootElement.GetProperty("title").GetString() ?? id;
            return await Commit(Path.Combine(temp, "audio.mp3"), name, canonical, title, token);
        }
        finally { Directory.Delete(temp, true); }
    }
    private static string Tool(string name)
    {
        string bundled = Path.Combine(AppContext.BaseDirectory, "tools", name);
        if (File.Exists(bundled)) return bundled;
        foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            string path = Path.Combine(folder, name);
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException($"Install {name} in the tools folder beside MicSomeNoise.exe, or add it to PATH. See README.md.");
    }
}
