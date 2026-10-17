param([switch]$SelfContained)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$artifacts = Join-Path $root 'artifacts'
# A new staging folder prevents old language resources and symbols entering the ZIP.
$output = Join-Path $artifacts ('publish-' + [Guid]::NewGuid().ToString('N'))
try {
    dotnet publish "$root/src/MicSomeNoise" -c Release -r win-x64 --self-contained $SelfContained.IsPresent.ToString().ToLowerInvariant() -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $output
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    Copy-Item "$root/QUICKSTART.md" "$output/README.md"
    $tools = Join-Path $output 'tools'
    New-Item -ItemType Directory -Path $tools | Out-Null
    Copy-Item "$root/scripts/install-import-tools.ps1" $tools
    if (-not (Test-Path "$output/MicSomeNoise.exe" -PathType Leaf)) { throw 'Published executable is missing.' }
    $unexpected = @(Get-ChildItem $output -Force | Where-Object { $_.Name -notin @('MicSomeNoise.exe', 'README.md', 'tools', 'licenses') })
    if ($unexpected.Count -gt 0) { throw "Unexpected release files: $($unexpected.Name -join ', ')" }
    Compress-Archive -Path "$output/*" -DestinationPath "$artifacts/MicSomeNoise-win-x64.zip" -Force
}
finally {
    if (Test-Path $output) { Remove-Item $output -Recurse -Force }
}
