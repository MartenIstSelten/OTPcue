param(
    [Parameter(Mandatory)]
    [ValidatePattern('^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$')]
    [string]$Version,
    [ValidatePattern('^https://github\.com/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+/?$')]
    [string]$UpdateRepositoryUrl = 'https://github.com/MartenIstSelten/OTPinsert',
    [string]$PreviousReleaseDirectory
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repositoryRoot
try {
    function Invoke-DotNet {
        param([string[]]$Arguments)
        & dotnet @Arguments
        if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE." }
    }

    $releaseRoot = Join-Path $repositoryRoot "artifacts/releases/$Version"
    $publishDirectory = Join-Path $releaseRoot 'publish'
    $packageDirectory = Join-Path $releaseRoot 'packages'
    if (Test-Path -LiteralPath $releaseRoot) {
        throw "Release output already exists: $releaseRoot. Use a new version or remove that output before rebuilding."
    }
    New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null
    if ($PreviousReleaseDirectory) {
        foreach ($file in Get-ChildItem -LiteralPath $PreviousReleaseDirectory -File) {
            if ($file.Extension -eq '.nupkg' -or $file.Name -like 'releases.*.json' -or $file.Name -like 'RELEASES*') {
                Copy-Item -LiteralPath $file.FullName -Destination $packageDirectory
            }
        }
    }
    Invoke-DotNet -Arguments @('tool', 'restore')
    Invoke-DotNet -Arguments @('publish', 'src/OTPinsert.WinUI/OTPinsert.WinUI.csproj',
        '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-o', $publishDirectory,
        "-p:Version=$Version", "-p:UpdateRepositoryUrl=$UpdateRepositoryUrl",
        '-p:WindowsAppSDKSelfContained=true', '-p:PublishSingleFile=false', '-p:PublishTrimmed=false')
    # WinUI cannot initialize App.xaml without the application's compiled resources.
    if (!(Test-Path -LiteralPath (Join-Path $publishDirectory 'OTPinsert.pri'))) {
        throw 'Published WinUI resources are missing: OTPinsert.pri. Refusing to package an app that cannot start.'
    }
    & (Join-Path $PSScriptRoot 'Copy-ReleaseLicenses.ps1') -PublishDirectory $publishDirectory
    if (Get-ChildItem -LiteralPath $publishDirectory -Recurse -File -Filter '*.update-token.dat') {
        throw 'Local update credentials found in publish output. Refusing to create an installer.'
    }
    Invoke-DotNet -Arguments @('tool', 'run', 'vpk', '--', 'pack',
        '--packId', 'OTPinsert', '--packTitle', 'OTPinsert',
        '--packAuthors', 'MartenIstSelten', '--packVersion', $Version,
        '--packDir', $publishDirectory, '--mainExe', 'OTPinsert.exe',
        '--icon', (Join-Path $publishDirectory 'Assets/OTPinsert.ico'),
        '--channel', 'win-x64', '--runtime', 'win-x64', '--outputDir', $packageDirectory)
    # Verify that the installer/update payload includes the WinUI resource index
    # and license notices, not just the publish directory.
    $fullPackage = Join-Path $packageDirectory "OTPinsert-$Version-win-x64-full.nupkg"
    $archive = [System.IO.Compression.ZipFile]::OpenRead($fullPackage)
    try {
        if ($archive.Entries | Where-Object { $_.FullName -like '*.update-token.dat' }) {
            throw 'Local update credentials found in update package. Refusing release publication.'
        }
        foreach ($file in @(
            (Get-Item -LiteralPath (Join-Path $publishDirectory 'OTPinsert.pri')),
            (Get-Item -LiteralPath (Join-Path $publishDirectory 'Assets/OTPinsert.ico')),
            (Get-Item -LiteralPath (Join-Path $publishDirectory 'LICENSE.txt')),
            (Get-Item -LiteralPath (Join-Path $publishDirectory 'THIRD-PARTY-NOTICES.md'))
        )) {
            $relative = [System.IO.Path]::GetRelativePath($publishDirectory, $file.FullName).Replace('\', '/')
            if (!$archive.GetEntry("lib/app/$relative")) { throw "Required file missing from Velopack package: $relative" }
        }
    }
    finally { $archive.Dispose() }
    # Keep the update identity stable while publishing a shorter installer name.
    Move-Item -LiteralPath (Join-Path $packageDirectory 'OTPinsert-win-x64-Setup.exe') -Destination (Join-Path $packageDirectory 'OTPinsert.exe')
    $assetManifest = Join-Path $packageDirectory 'assets.win-x64.json'
    $assets = @(Get-Content -LiteralPath $assetManifest -Raw | ConvertFrom-Json)
    foreach ($asset in $assets) {
        if ($asset.RelativeFileName -eq 'OTPinsert-win-x64-Setup.exe') {
            $asset.RelativeFileName = 'OTPinsert.exe'
        }
    }
    ConvertTo-Json -InputObject $assets -Depth 10 | Set-Content -LiteralPath $assetManifest -Encoding utf8
    Write-Host "Installer and update packages: $packageDirectory"
}
finally { Pop-Location }
