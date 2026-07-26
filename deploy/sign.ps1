<#
.SYNOPSIS
  Signs PrinterConnect.exe with your company's code-signing certificate.

.DESCRIPTION
  Run once per release before distribution. Signing "seals" the exe under your
  publisher identity: SmartScreen trusts it, and your security tools can allow-list
  by publisher so every future signed version is trusted automatically.

  Works with a certificate in the local store (internal CA, OV/EV) or with
  Azure Trusted Signing (pass -AzureCodeSigningDlib etc. per its docs).

.EXAMPLE
  .\sign.ps1 -ExePath ..\PrinterConnect.exe -Thumbprint 1A2B3C...

.EXAMPLE
  # pick the cert interactively from your store
  .\sign.ps1 -ExePath ..\PrinterConnect.exe
#>
param(
    [Parameter(Mandatory = $true)] [string] $ExePath,
    [string] $Thumbprint,
    [string] $TimestampUrl = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $ExePath)) { throw "Not found: $ExePath" }

if (-not $Thumbprint) {
    $cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
        Out-GridView -Title "Choose your code-signing certificate" -OutputMode Single
    if (-not $cert) { throw "No certificate selected." }
    $Thumbprint = $cert.Thumbprint
}

# signtool ships with the Windows SDK; fall back to Set-AuthenticodeSignature
$signtool = Get-Command signtool.exe -ErrorAction SilentlyContinue
if ($signtool) {
    & $signtool.Source sign /sha1 $Thumbprint /fd SHA256 /td SHA256 /tr $TimestampUrl $ExePath
} else {
    $cert = Get-Item "Cert:\CurrentUser\My\$Thumbprint"
    Set-AuthenticodeSignature -FilePath $ExePath -Certificate $cert `
        -HashAlgorithm SHA256 -TimestampServer $TimestampUrl | Out-Null
}

$sig = Get-AuthenticodeSignature $ExePath
Write-Host "Status : $($sig.Status)"
Write-Host "Signer : $($sig.SignerCertificate.Subject)"
if ($sig.Status -ne "Valid") { throw "Signing did not produce a valid signature." }
Write-Host "Done — $ExePath is signed and ready for distribution." -ForegroundColor Green
