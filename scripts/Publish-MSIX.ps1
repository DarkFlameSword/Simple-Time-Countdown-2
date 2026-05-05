[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$RuntimeIdentifier = 'win-x64',
    [string]$Version = '',
    [string]$Publisher = 'CN=TimeCountdownDev',
    [string]$CertificatePassword = 'TimeCountdownDev123!'
)

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$dotnet = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) {
    $dotnet = 'dotnet'
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$versionProps = Get-Content (Join-Path $root 'Directory.Build.props')
    $Version = $versionProps.Project.PropertyGroup.FileVersion
}

$kitBin = 'C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64'
$makeAppx = Join-Path $kitBin 'makeappx.exe'
$signTool = Join-Path $kitBin 'signtool.exe'

if (-not (Test-Path $makeAppx)) {
    throw "makeappx.exe not found at $makeAppx"
}

if (-not (Test-Path $signTool)) {
    throw "signtool.exe not found at $signTool"
}

& (Join-Path $PSScriptRoot 'Generate-AppAssets.ps1') -Root $root
if ($LASTEXITCODE) { exit $LASTEXITCODE }

$publishDir = Join-Path $root "artifacts\publish\msix\$RuntimeIdentifier\app"
$layoutDir = Join-Path $root "artifacts\publish\msix\$RuntimeIdentifier\layout"
$packageDir = Join-Path $root 'artifacts\packages'
$certDir = Join-Path $root 'artifacts\certificates'
New-Item -ItemType Directory -Force -Path $publishDir, $layoutDir, $packageDir, $certDir | Out-Null

Get-ChildItem -LiteralPath $layoutDir -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force

& $dotnet publish (Join-Path $root 'src\SimpleTimeCountdown.App\SimpleTimeCountdown.App.csproj') `
    -c $Configuration `
    -r $RuntimeIdentifier `
    --self-contained true `
    -p:PublishSingleFile=false `
    -o $publishDir
if ($LASTEXITCODE) { exit $LASTEXITCODE }

Copy-Item -Path (Join-Path $publishDir '*') -Destination $layoutDir -Recurse -Force
Copy-Item -Path (Join-Path $root 'packaging\msix\Assets') -Destination $layoutDir -Recurse -Force

$manifestTemplate = Get-Content (Join-Path $root 'packaging\msix\AppxManifest.xml') -Raw
$manifest = $manifestTemplate.Replace('__VERSION__', $Version).Replace('__PUBLISHER__', $Publisher)
Set-Content -Path (Join-Path $layoutDir 'AppxManifest.xml') -Value $manifest -Encoding UTF8

$pfxPath = Join-Path $certDir 'TimeCountdownDev.pfx'
$cerPath = Join-Path $certDir 'TimeCountdownDev.cer'
if (-not (Test-Path $pfxPath)) {
    $cert = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject $Publisher `
        -CertStoreLocation 'Cert:\CurrentUser\My' `
        -FriendlyName 'Simple Time Countdown Dev'
    $securePassword = ConvertTo-SecureString -String $CertificatePassword -Force -AsPlainText
    Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $securePassword | Out-Null
    Export-Certificate -Cert $cert -FilePath $cerPath | Out-Null
}

$msixPath = Join-Path $packageDir "SimpleTimeCountdown_$Version`_$RuntimeIdentifier.msix"
if (Test-Path $msixPath) {
    Remove-Item -LiteralPath $msixPath -Force
}

& $makeAppx pack /d $layoutDir /p $msixPath /o
if ($LASTEXITCODE) { exit $LASTEXITCODE }

& $signTool sign /fd SHA256 /f $pfxPath /p $CertificatePassword $msixPath
if ($LASTEXITCODE) { exit $LASTEXITCODE }

Write-Host "MSIX package ready: $msixPath"
Write-Host "Certificate: $cerPath"
