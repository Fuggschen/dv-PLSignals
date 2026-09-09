param (
	[switch]$NoArchive,
	[string]$OutputDirectory = $PSScriptRoot
)

Set-Location "$PSScriptRoot"
$FilesToInclude = "info.json","build/*","LICENSE"

$modInfo = Get-Content -Raw -Path "info.json" | ConvertFrom-Json
$modId = $modInfo.Id
$modVersion = $modInfo.Version

$DistDir = "$OutputDirectory/dist"
if ($NoArchive) {
	$ZipWorkDir = "$OutputDirectory"
} else {
	$ZipWorkDir = "$DistDir/tmp"
}
$ZipOutDir = "$ZipWorkDir/$modId"

New-Item "$ZipOutDir" -ItemType Directory -Force
Copy-Item -Force -Path $FilesToInclude -Destination "$ZipOutDir"

# Extract signal_bundle from Unity zip
$SourceZip = "PLSignals.UnityProject\Assets\PL_Signals\PLSignals.zip"
$BundleZipEntry = "PLSignals/signal_bundle"
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($SourceZip)
try {
	$entry = $zip.Entries | Where-Object { $_.FullName -eq $BundleZipEntry } | Select-Object -First 1
	if ($null -eq $entry) {
		Write-Error "signal_bundle not found in $SourceZip"
		exit 1
	}
	[System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, "$ZipOutDir\signal_bundle", $true)
} finally {
	$zip.Dispose()
}

if (!$NoArchive)
{
	$FILE_NAME = "$DistDir/${modId}_v$modVersion.zip"
	Compress-Archive -Update -CompressionLevel Fastest -Path "$ZipOutDir/*" -DestinationPath "$FILE_NAME"
}
