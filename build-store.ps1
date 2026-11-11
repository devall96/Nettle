<#
  Builds the Microsoft Store package of Nettle (an MSIX file to upload in Partner Center).

    .\build-store.ps1                          # uses <Version> from Directory.Build.props
    .\build-store.ps1 -PackageVersion 1.0.1.0  # choose the Store's version number yourself
    .\build-store.ps1 -Register                # also install it on this PC to try it (needs Developer Mode)

  Output (dist\store\):
    Nettle-<package version>-x64.msix   upload this one; the Store signs it, no certificate needed
    layout\                             the unpacked package (what -Register installs)

  The Store's version number is Nettle's own with ".0" added (1.0.0 becomes 1.0.0.0). It can't
  start with 0 and must end in .0, and each upload must be numbered higher than the last.

  The Store copy leaves Nettle's own updater off (Windows updates it); see StorePackage.cs.
#>
param([string]$Version, [string]$PackageVersion, [switch]$Register)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

# Keep the build itself private too (same as build-release.ps1).
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:AVALONIA_TELEMETRY_OPTOUT = '1'

if (-not $Version) { $Version = (([xml](Get-Content Directory.Build.props)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1) }
$Version = "$Version".Trim()
if (-not $PackageVersion) {
  $v = [version]$Version
  $PackageVersion = "$($v.Major).$($v.Minor).$([Math]::Max($v.Build, 0)).0"
}
if ($PackageVersion -notmatch '^[1-9]\d*\.\d+\.\d+\.0$') { throw "Store package version must look like 1.0.0.0 (first part not 0, last part 0): $PackageVersion" }
Write-Host "== Nettle $Version -> Store package $PackageVersion" -ForegroundColor Cyan

# makeappx / makepri come with the Windows SDK. Without the SDK, Microsoft's own NuGet package of the same tools is used.
function Find-SdkTool([string]$name) {
  $roots = @("${env:ProgramFiles(x86)}\Windows Kits\10\bin", "$env:USERPROFILE\.nuget\packages\microsoft.windows.sdk.buildtools")
  foreach ($r in $roots) {
    if (-not (Test-Path $r)) { continue }
    $hit = Get-ChildItem $r -Recurse -Filter $name -ErrorAction SilentlyContinue | Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName -Descending | Select-Object -First 1
    if ($hit) { return $hit.FullName }
  }
  return $null
}
if (-not (Find-SdkTool 'makeappx.exe')) {
  Write-Host "   Windows SDK not found: fetching Microsoft.Windows.SDK.BuildTools from NuGet"
  $toolProj = "dist\store-tools"
  New-Item -ItemType Directory -Force $toolProj | Out-Null
  [IO.File]::WriteAllText((Join-Path (Resolve-Path $toolProj).Path 'tools.csproj'),
    '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><PackageReference Include="Microsoft.Windows.SDK.BuildTools" Version="10.0.26100.4654" /></ItemGroup></Project>')
  dotnet restore "$toolProj\tools.csproj" -nologo -v q
  if ($LASTEXITCODE -ne 0) { throw "couldn't fetch the Windows SDK build tools" }
}
$makeappx = Find-SdkTool 'makeappx.exe'
$makepri = Find-SdkTool 'makepri.exe'
if (-not $makeappx -or -not $makepri) { throw "makeappx.exe / makepri.exe not found" }

& "$PSScriptRoot\get-libmpv.ps1"   # pinned, hash-checked libmpv-2.dll

$out = Join-Path $PSScriptRoot 'dist\store'
$layout = Join-Path $out 'layout'
if (Test-Path $layout) { Remove-Item -Recurse -Force $layout }
New-Item -ItemType Directory -Force $out | Out-Null
dotnet publish src/Nettle.Desktop/Nettle.Desktop.csproj -c Release -r win-x64 --self-contained true `
  -p:Version=$Version -o $layout -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

# Logos, drawn by the app itself so they match the current logo and default theme.
$assets = Join-Path $layout 'Assets'
$r = Start-Process (Join-Path $layout 'Nettle.exe') -ArgumentList '--render-store', "`"$assets`"" -PassThru
if (-not $r.WaitForExit(60000) -or $r.ExitCode -ne 0 -or -not (Test-Path (Join-Path $assets 'StoreLogo.scale-100.png'))) { if (-not $r.HasExited) { $r.Kill() }; throw "logo render failed" }

$manifest = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'src\Nettle.Desktop\Store\AppxManifest.xml')).Replace('{VERSION}', $PackageVersion)
[IO.File]::WriteAllText((Join-Path $layout 'AppxManifest.xml'), $manifest, (New-Object Text.UTF8Encoding $false))

# resources.pri: the index that lets Windows pick the right logo size (scale-200, targetsize-32, ...).
# Built from a folder holding only the manifest and the logos, so the app's own files aren't indexed.
$pri = Join-Path $out 'pri'
if (Test-Path $pri) { Remove-Item -Recurse -Force $pri }
New-Item -ItemType Directory -Force $pri | Out-Null
Copy-Item -Recurse $assets (Join-Path $pri 'Assets')
Copy-Item (Join-Path $layout 'AppxManifest.xml') $pri
$priConfig = Join-Path $out 'priconfig.xml'
& $makepri createconfig /cf $priConfig /dq en-US /pv 10.0.0 /o | Out-Null
if ($LASTEXITCODE -ne 0) { throw "makepri createconfig failed" }
# One package, so no splitting of the index into per-scale resource packs.
$cfg = [IO.File]::ReadAllText($priConfig) -replace '(?s)<packaging>.*?</packaging>', ''
[IO.File]::WriteAllText($priConfig, $cfg, (New-Object Text.UTF8Encoding $false))
& $makepri new /pr $pri /cf $priConfig /mn (Join-Path $pri 'AppxManifest.xml') /of (Join-Path $layout 'resources.pri') /o | Out-Null
if ($LASTEXITCODE -ne 0) { throw "makepri new failed" }
Remove-Item -Recurse -Force $pri
Remove-Item -Force $priConfig

$msix = Join-Path $out "Nettle-$PackageVersion-x64.msix"
& $makeappx pack /d $layout /p $msix /o | Out-Null
if ($LASTEXITCODE -ne 0) { throw "makeappx pack failed" }

if ($Register) {
  # Installs the unpacked package for this Windows user (Developer Mode must be on). Remove it again with:
  #   Get-AppxPackage 26880Trikada.Nettle | Remove-AppxPackage
  Add-AppxPackage -Register (Join-Path $layout 'AppxManifest.xml')
  Write-Host "== Installed for testing: Nettle is in the Start menu" -ForegroundColor Green
}
Write-Host "== Done. Upload to Partner Center: $msix" -ForegroundColor Green
