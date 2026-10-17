param(
    [Parameter(Mandatory)]
    [string]$PublishDirectory,
    [string]$AssetsFile = (Join-Path $PSScriptRoot '../src/OTPinsert.WinUI/obj/project.assets.json')
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$assets = Get-Content -LiteralPath $AssetsFile -Raw | ConvertFrom-Json
$deps = Get-Content -LiteralPath (Join-Path $PublishDirectory 'OTPinsert.deps.json') -Raw | ConvertFrom-Json
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -Destination (Join-Path $PublishDirectory 'LICENSE.txt')

# Include build/native packages too: WindowsAppSDK imports can copy binaries that
# are not represented as managed runtime assets in the .deps.json file.
$packages = @{}
foreach ($entry in $assets.libraries.PSObject.Properties) {
    if ($entry.Value.type -eq 'package') { $packages[$entry.Name] = $entry.Value.path }
}
foreach ($entry in $deps.libraries.PSObject.Properties) {
    if ($entry.Name.StartsWith('runtimepack.')) {
        $name = $entry.Name.Substring('runtimepack.'.Length)
        $packages[$name] = $name.ToLowerInvariant()
    }
}

$index = [System.Collections.Generic.List[string]]::new()
$index.Add('# Third-party software notices')
$index.Add('')
$index.Add('OTPinsert is licensed under the MIT license in LICENSE.txt. The following bundled dependencies retain their own license and notice texts. This list also includes native/build packages used by the self-contained Windows release.')
$index.Add('')

function Convert-LicenseDocumentToText {
    param([System.IO.FileInfo]$Document)

    $content = Get-Content -LiteralPath $Document.FullName -Raw
    switch ($Document.Extension.ToLowerInvariant()) {
        '.rtf' {
            Add-Type -AssemblyName System.Windows.Forms
            $textBox = [System.Windows.Forms.RichTextBox]::new()
            try {
                $textBox.Rtf = $content
                return $textBox.Text
            }
            finally { $textBox.Dispose() }
        }
        '.html' {
            $plainText = [System.Text.RegularExpressions.Regex]::Replace($content, '(?is)<(script|style)\b[^>]*>.*?</\1>', '')
            $plainText = [System.Text.RegularExpressions.Regex]::Replace($plainText, '(?i)<br\s*/?>|</p>|</div>|</li>|</tr>', "`n")
            $plainText = [System.Text.RegularExpressions.Regex]::Replace($plainText, '<[^>]+>', '')
            return [System.Net.WebUtility]::HtmlDecode($plainText).Trim()
        }
        default { return $content.Trim() }
    }
}

foreach ($name in ($packages.Keys | Sort-Object)) {
    $packageDirectory = $null
    foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
        $candidate = Join-Path $folder $packages[$name]
        if (Test-Path -LiteralPath $candidate -PathType Container) { $packageDirectory = $candidate; break }
    }
    if (!$packageDirectory) { throw "Package is missing from the restore cache: $name" }
    $nuspecFile = Get-ChildItem -LiteralPath $packageDirectory -Filter '*.nuspec' -File | Select-Object -First 1
    if (!$nuspecFile) { throw "Package metadata is missing: $name" }
    [xml]$nuspec = Get-Content -LiteralPath $nuspecFile.FullName -Raw
    $metadata = $nuspec.SelectSingleNode('/*[local-name()="package"]/*[local-name()="metadata"]')
    $id = $metadata.SelectSingleNode('*[local-name()="id"]').InnerText
    $version = $metadata.SelectSingleNode('*[local-name()="version"]').InnerText
    $license = $metadata.SelectSingleNode('*[local-name()="license"]')
    $copyright = $metadata.SelectSingleNode('*[local-name()="copyright"]')
    $authors = $metadata.SelectSingleNode('*[local-name()="authors"]')
    $licenseUrl = $metadata.SelectSingleNode('*[local-name()="licenseUrl"]')
    $projectUrl = $metadata.SelectSingleNode('*[local-name()="projectUrl"]')
    $documents = @(Get-ChildItem -LiteralPath $packageDirectory -Recurse -File | Where-Object {
        $_.Name -match '(?i)(license|licence|notice|copying|copyright)' -and $_.Extension -in @('', '.txt', '.md', '.rtf', '.html')
    })
    $fallback = Join-Path $repositoryRoot "licenses/$id/$version"
    if (Test-Path -LiteralPath $fallback -PathType Container) {
        $documents += @(Get-ChildItem -LiteralPath $fallback -Recurse -File | Where-Object {
            $_.Name -match '(?i)(license|licence|notice|copying|copyright)' -and $_.Extension -in @('', '.txt', '.md', '.rtf', '.html')
        })
    }
    $documents = @($documents | Sort-Object FullName -Unique)
    $licenseDocuments = @($documents | Where-Object { $_.Name -match '(?i)(license|licence|copying)' })
    $hasLicense = $licenseDocuments.Count -gt 0
    # BuildTools itself contributes build executables, not redistributed runtime
    # files. Its separately licensed MSIX package is included above.
    if (!$hasLicense -and $id -ne 'Microsoft.Windows.SDK.BuildTools') {
        throw "No license text found for $name. Add its original license under licenses/$id/$version before releasing."
    }

    $index.Add("## $id $version")
    $index.Add('')
    if ($license) { $index.Add("License declaration: $($license.InnerText)") }
    if ($copyright) { $index.Add($copyright.InnerText) }
    if ($authors) { $index.Add("Authors: $($authors.InnerText)") }
    if ($projectUrl) { $index.Add("Project: $($projectUrl.InnerText)") }
    if ($licenseUrl) { $index.Add("License source: $($licenseUrl.InnerText)") }
    $index.Add('')
    foreach ($document in $documents) {
        $relative = if ($document.FullName.StartsWith($packageDirectory, [System.StringComparison]::OrdinalIgnoreCase)) {
            [System.IO.Path]::GetRelativePath($packageDirectory, $document.FullName).Replace('\', '/')
        } else {
            [System.IO.Path]::GetRelativePath($fallback, $document.FullName).Replace('\', '/')
        }
        $index.Add("### $($document.Name) ($relative)")
        $index.Add('')
        $index.Add('````text')
        $index.Add((Convert-LicenseDocumentToText -Document $document))
        $index.Add('````')
        $index.Add('')
    }
    if (!$hasLicense) { $index.Add('Build-time tools only; no runtime files redistributed from this package.') }
    $index.Add('')
}
$index | Set-Content -LiteralPath (Join-Path $PublishDirectory 'THIRD-PARTY-NOTICES.md') -Encoding utf8
Write-Host "Included license notices for $($packages.Count) packages."
