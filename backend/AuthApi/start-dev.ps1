$ErrorActionPreference = 'Stop'

$email = Read-Host 'Bootstrap admin email'
if ([string]::IsNullOrWhiteSpace($email)) {
    throw 'Admin email is required.'
}

$securePassword = Read-Host 'Bootstrap admin password (minimum 12 characters)' -AsSecureString
$credential = [System.Management.Automation.PSCredential]::new('bootstrap', $securePassword)
$password = $credential.GetNetworkCredential().Password
if ($password.Length -lt 12) {
    throw 'Admin password must contain at least 12 characters.'
}

$keyBytes = New-Object byte[] 48
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
try {
    $rng.GetBytes($keyBytes)
} finally {
    $rng.Dispose()
}

$env:CTF_SESSION_KEY = [Convert]::ToBase64String($keyBytes)
$env:CTF_BOOTSTRAP_ADMIN_EMAIL = $email.Trim()
$env:CTF_BOOTSTRAP_ADMIN_PASSWORD = $password
$env:CTF_WEB_ORIGIN = 'http://localhost:4200'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = 'http://localhost:5000'

Write-Host 'Starting the CTF API at http://localhost:5000'
Write-Host 'Keep this terminal open while using the portal.'
dotnet run --project (Join-Path $PSScriptRoot 'AuthApi.csproj')
