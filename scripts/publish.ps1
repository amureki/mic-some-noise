param([switch]$SelfContained)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$artifacts = Join-Path $root 'artifacts'
# A new staging folder prevents old language resources and symbols entering the ZIP.
$output = Join-Path $artifacts ('publish-' + [Guid]::NewGuid().ToString('N'))
try {
    dotnet publish "$root/src/MicSomeNoise" -c Release -r win-x64 --self-contained $SelfContained.IsPresent.ToString().ToLowerInvariant() -o $output
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    Copy-Item "$root/QUICKSTART.md" "$output/README.md"
    Copy-Item "$root/scripts/install-import-tools.ps1" $output
    Compress-Archive -Path "$output/*" -DestinationPath "$artifacts/MicSomeNoise-win-x64.zip" -Force
}
finally {
    if (Test-Path $output) { Remove-Item $output -Recurse -Force }
}
