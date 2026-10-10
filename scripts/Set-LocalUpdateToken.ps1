param(
    [ValidatePattern('^https://github\.com/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+/?$')]
    [string]$RepositoryUrl = 'https://github.com/MartenIstSelten/OTPcue',
    [switch]$Remove
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security
$normalized = $RepositoryUrl.TrimEnd('/').ToLowerInvariant()
$sha = [System.Security.Cryptography.SHA256]::Create()
try { $key = $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($normalized)) }
finally { $sha.Dispose() }
$name = ([BitConverter]::ToString($key)).Replace('-', '') + '.update-token.dat'
$directory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'OTPcue/update-auth'
$path = Join-Path $directory $name
if ($Remove) {
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path }
    Write-Host 'Local update token removed. Restart OTPcue.'
    return
}

# No token parameter: keep the secret out of command history and process arguments.
$secret = Read-Host 'GitHub token (hidden; repository Contents: read-only)' -AsSecureString
$pointer = [IntPtr]::Zero
$plain = $null
try {
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secret)
    $value = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
    if ([string]::IsNullOrWhiteSpace($value)) { throw 'Token cannot be empty.' }
    $plain = [Text.Encoding]::UTF8.GetBytes($value.Trim())
    $encrypted = [Security.Cryptography.ProtectedData]::Protect($plain, $key, [Security.Cryptography.DataProtectionScope]::CurrentUser)
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    [IO.File]::WriteAllBytes($path, $encrypted)
    Write-Host 'Token saved outside the repository, encrypted for this Windows user. Restart OTPcue.'
}
finally {
    if ($plain) { [Array]::Clear($plain, 0, $plain.Length) }
    $value = $null
    if ($pointer -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
    $secret.Dispose()
}
