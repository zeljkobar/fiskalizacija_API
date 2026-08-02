[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$userSecretsId = '927e75f4-38f7-493d-bcbc-0a3c2645d963'
$userSecretsPath = Join-Path $env:APPDATA "Microsoft\UserSecrets\$userSecretsId\secrets.json"
$outputDirectory = Join-Path $PSScriptRoot 'local-secrets'
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

function Get-SecretValue {
    param(
        [Parameter(Mandatory)] [object] $Json,
        [Parameter(Mandatory)] [string] $Name
    )

    $property = $Json.PSObject.Properties | Where-Object Name -eq $Name | Select-Object -First 1
    if ($null -eq $property -or [string]::IsNullOrWhiteSpace([string]$property.Value)) {
        throw "Nedostaje lokalni user-secret: $Name"
    }

    return [string]$property.Value
}

function ConvertFrom-ProtectedString {
    param([Parameter(Mandatory)] [Security.SecureString] $Value)

    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Value)
    try {
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
    }
}

function Write-SecretFile {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [string] $Value
    )

    [IO.File]::WriteAllText((Join-Path $outputDirectory $Name), $Value, $utf8NoBom)
}

if (-not (Test-Path -LiteralPath $userSecretsPath)) {
    throw "Lokalni user-secrets fajl nije pronadjen."
}

$json = Get-Content -LiteralPath $userSecretsPath -Raw | ConvertFrom-Json
$vaultKey = Get-SecretValue $json 'Fiscalization:CertificateVault:MasterKeyBase64'
$pfxPath = Get-SecretValue $json 'Fiscalization:DevelopmentCertificate:Path'
$pfxPassword = Get-SecretValue $json 'Fiscalization:DevelopmentCertificate:Password'

$vaultKeyBytes = [Convert]::FromBase64String($vaultKey)
if ($vaultKeyBytes.Length -ne 32) {
    throw 'Vault master kljuc nije Base64 vrijednost od tacno 32 bajta.'
}
[Array]::Clear($vaultKeyBytes, 0, $vaultKeyBytes.Length)

if (-not (Test-Path -LiteralPath $pfxPath)) {
    throw 'Lokalni fiskalni PFX nije pronadjen.'
}

$secureDatabasePassword = Read-Host 'Unesi lozinku serverskog PostgreSQL korisnika summa_fiscal_app' -AsSecureString
$databasePassword = ConvertFrom-ProtectedString $secureDatabasePassword
if ([string]::IsNullOrWhiteSpace($databasePassword)) {
    throw 'Lozinka baze ne smije biti prazna.'
}

New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$escapedDatabasePassword = $databasePassword.Replace('"', '""')
$connectionString = "Host=127.0.0.1;Port=5432;Database=summa_fiscal;Username=summa_fiscal_app;Password=`"$escapedDatabasePassword`";SSL Mode=Disable"
$bootstrapKeyBytes = New-Object byte[] 48
$randomGenerator = [Security.Cryptography.RandomNumberGenerator]::Create()
try {
    $randomGenerator.GetBytes($bootstrapKeyBytes)
    $bootstrapKey = [Convert]::ToBase64String($bootstrapKeyBytes)
}
finally {
    $randomGenerator.Dispose()
    [Array]::Clear($bootstrapKeyBytes, 0, $bootstrapKeyBytes.Length)
}

try {
    Write-SecretFile 'postgres_password.txt' $databasePassword
    Write-SecretFile 'database_connection.txt' $connectionString
    Write-SecretFile 'bootstrap_admin_key.txt' $bootstrapKey
    Write-SecretFile 'certificate_vault_key.txt' $vaultKey
    Write-SecretFile 'fiscal_certificate_password.txt' $pfxPassword
    Copy-Item -LiteralPath $pfxPath -Destination (Join-Path $outputDirectory 'fiscal-certificate.pfx') -Force
}
finally {
    $databasePassword = $null
    $connectionString = $null
    $bootstrapKey = $null
    $pfxPassword = $null
    $vaultKey = $null
}

Write-Output "Pripremljeno je 6 secret fajlova u: $outputDirectory"
Write-Output 'Vrijednosti nijesu prikazane. Direktorijum je ignorisan u Git-u.'
