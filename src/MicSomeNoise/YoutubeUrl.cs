using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace MicSomeNoise;

public static class YoutubeUrl
{
    public static string Normalize(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("https" or "http") ||
            !(uri.Host == "youtu.be" || uri.Host == "youtube.com" || uri.Host.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Paste a YouTube video URL.");
        var path = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string? id = null;
        if (uri.Host == "youtu.be") id = path.FirstOrDefault();
        else if (path.Length == 2 && path[0] is "shorts" or "embed" or "live") id = path[1];
        else if (uri.AbsolutePath == "/watch")
            id = uri.Query.TrimStart('?').Split('&').Select(part => part.Split('=', 2))
                .Where(part => part.Length == 2 && part[0] == "v").Select(part => Uri.UnescapeDataString(part[1])).FirstOrDefault();
        if (id == null || !Regex.IsMatch(id, "^[A-Za-z0-9_-]{11}$"))
            throw new InvalidOperationException("Paste a link to one video, not a playlist or channel.");
        return "https://www.youtube.com/watch?v=" + id;
    }
}
