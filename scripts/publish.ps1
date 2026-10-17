param([switch]$SelfContained)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$artifacts = Join-Path $root 'artifacts'
[xml]$project = Get-Content "$root/src/MicSomeNoise/MicSomeNoise.csproj"
$version = $project.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Project version must use major.minor.patch.' }
$packageName = "MicSomeNoise-$version-win-x64"
# A new staging folder prevents old language resources and symbols entering the ZIP.
$staging = Join-Path $artifacts ('publish-' + [Guid]::NewGuid().ToString('N'))
$output = Join-Path $staging $packageName
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
    $zip = Join-Path $artifacts "$packageName.zip"
    Compress-Archive -Path $output -DestinationPath $zip -Force
    $archive = [System.IO.Compression.ZipFile]::OpenRead($zip)
    try {
        $entries = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
        if ("$packageName/MicSomeNoise.exe" -notin $entries -or @($entries | Where-Object { -not $_.StartsWith("$packageName/") }).Count -gt 0) {
            throw 'Release ZIP must contain the versioned app folder.'
        }
    }
    finally { $archive.Dispose() }
}
finally {
    if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
}
