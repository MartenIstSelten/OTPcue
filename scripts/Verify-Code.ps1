param(
    [switch]$Interactive
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repositoryRoot
try {
    function Invoke-DotNet {
        param([string[]]$Arguments)
        & dotnet @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
        }
    }

    # SDK artifact paths keep each project's output isolated from the running app.
    $verificationOutput = Join-Path $repositoryRoot 'artifacts/verification'
    Invoke-DotNet -Arguments @('restore', 'OTPcue.slnx', '--artifacts-path', $verificationOutput)
    Invoke-DotNet -Arguments @('build', 'OTPcue.slnx', '-c', 'Release', '--no-restore', '--artifacts-path', $verificationOutput, '-warnaserror')
    Invoke-DotNet -Arguments @('test', 'OTPcue.slnx', '-c', 'Release', '--no-build', '--no-restore',
        '--artifacts-path', $verificationOutput, '--logger', 'trx', '--results-directory', (Join-Path $verificationOutput 'test-results'))
    Invoke-DotNet -Arguments @('format', 'whitespace', 'OTPcue.slnx', '--no-restore', '--verify-no-changes')
    if ($Interactive) {
        Invoke-DotNet -Arguments @('run', '--project', 'tests/OTPcue.Windows.Tests', '-c', 'Release',
            '--no-build', '--no-restore', '--artifacts-path', $verificationOutput)
    }

}
finally {
    Pop-Location
}
