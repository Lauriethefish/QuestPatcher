# Verifies that the SHA-256 hashes in file-downloads.json match the files at their download URLs
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$sets = Get-Content "$PSScriptRoot/../../QuestPatcher.Core/Resources/file-downloads.json" -Raw | ConvertFrom-Json
$file = New-TemporaryFile
$failed = $false

foreach ($set in $sets) {
    foreach ($type in $set.sha256.PSObject.Properties) {
        foreach ($os in $type.Value.PSObject.Properties) {
            $url = $set.downloads.($type.Name).($os.Name)
            Invoke-WebRequest $url -OutFile $file
            $actual = (Get-FileHash $file -Algorithm SHA256).Hash

            if ($actual -eq $os.Value) {
                Write-Output "OK: $($type.Name) ($($os.Name))"
            }
            else {
                Write-Output "::error::$($type.Name) ($($os.Name)) hash mismatch, expected $($os.Value) but got $actual"
                $failed = $true
            }
        }
    }
}

Remove-Item $file
if ($failed) {
    exit 1
}
