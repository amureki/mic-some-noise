# Run on the Windows test machine. Each package uses its publisher's installer.
$ErrorActionPreference = 'Stop'
foreach ($id in @('yt-dlp.yt-dlp', 'Gyan.FFmpeg', 'DenoLand.Deno')) {
    winget install --id $id --exact --source winget
    if ($LASTEXITCODE -ne 0) { throw "Installation of $id failed. Check winget output, or install it manually using README.md." }
}
Write-Host 'Restart Mic Some Noise after installation so it receives the updated PATH.'
