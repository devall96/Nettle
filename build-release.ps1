<#
  Builds a Slopfin release: self-contained publish + Velopack installer and update packages.

    .\build-release.ps1                 # uses <Version> from Directory.Build.props
    .\build-release.ps1 -Version 0.2.0  # override

  Output (releases\):
    SlopfinApp-win-Setup.exe     one-click per-user installer (no admin needed)
    SlopfinApp-win-Portable.zip  portable copy
    *.nupkg + releases.win.json  update packages; upload ALL of these to a GitHub Release
#>
param([string]$Version)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

# Keep the build itself private too (Avalonia's build telemetry is also disabled in Directory.Build.targets).
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:AVALONIA_TELEMETRY_OPTOUT = '1'

if (-not $Version) { $Version = ([xml](Get-Content Directory.Build.props)).Project.PropertyGroup.Version }
$publishDir = "dist\publish-$Version"
Write-Host "== Slopfin $Version" -ForegroundColor Cyan

& "$PSScriptRoot\get-libmpv.ps1"   # pinned, hash-checked libmpv-2.dll

if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
dotnet publish src/Slopfin.Desktop/Slopfin.Desktop.csproj -c Release -r win-x64 --self-contained true `
  -p:Version=$Version -o $publishDir -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

# Installer splash art, drawn by the app itself so it matches the current theme / logo / version.
$splash = Join-Path (Resolve-Path dist).Path "installer-splash-$Version.png"
$r = Start-Process (Join-Path $publishDir 'Slopfin.exe') -ArgumentList '--render-splash', "`"$splash`"" -PassThru
if (-not $r.WaitForExit(60000) -or $r.ExitCode -ne 0 -or -not (Test-Path $splash)) { if (-not $r.HasExited) { $r.Kill() }; throw "splash render failed" }

dotnet tool restore | Out-Null
dotnet vpk pack `
  --packId SlopfinApp `
  --packVersion $Version `
  --packDir $publishDir `
  --mainExe Slopfin.exe `
  --packTitle "Slopfin" `
  --packAuthors "Slopfin contributors" `
  --icon src/Slopfin.Desktop/Assets/slopfin.ico `
  --splashImage $splash `
  --outputDir releases
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed" }

# Optional code signing (see README): add  --signParams "/fd sha256 /tr http://timestamp.digicert.com /td sha256 /a"
Write-Host "== Done. Installer: releases\SlopfinApp-win-Setup.exe" -ForegroundColor Green
