# Compresses a XAML text file to gzip for embedding as a managed resource.
# XamlPageLoader detects the gzip header at runtime and decompresses transparently.
param(
    [Parameter(Mandatory = $true)][string]$Source,
    [Parameter(Mandatory = $true)][string]$Destination
)

$ErrorActionPreference = 'Stop'

$bytes = [System.IO.File]::ReadAllBytes($Source)

$directory = [System.IO.Path]::GetDirectoryName($Destination)
if ($directory -and -not (Test-Path $directory)) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}

$fileStream = [System.IO.File]::Create($Destination)
try {
    $gzipStream = New-Object System.IO.Compression.GZipStream(
        $fileStream,
        [System.IO.Compression.CompressionLevel]::Optimal)
    try {
        $gzipStream.Write($bytes, 0, $bytes.Length)
    }
    finally {
        $gzipStream.Dispose()
    }
}
finally {
    $fileStream.Dispose()
}
