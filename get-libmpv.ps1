# Downloads the pinned libmpv build for Windows into deps\libmpv\win-x64\ (not committed).
#
#   powershell -ExecutionPolicy Bypass -File get-libmpv.ps1
#
# The build is shinchiro's mpv-winbuild-cmake "mpv-dev" package (GPL build of mpv with FFmpeg,
# libplacebo, libdovi etc. statically linked into one DLL). The archive is checked against a
# pinned SHA-256, so a changed or tampered file is refused.
#   mpv source:      https://github.com/mpv-player/mpv/tree/9028187538
#   build scripts:   https://github.com/shinchiro/mpv-winbuild-cmake
$ErrorActionPreference = 'Stop'

$Build   = '20261107'
$Commit  = '9028187538'
$Archive = "mpv-dev-x86_64-$Build-git-$Commit.7z"
$Sha256  = 'DF7C433A2D481FDAF6163B201F26ABBB0EFF0C215C51368397088C6D8F7A001B'
$Sources = @(
    "https://github.com/shinchiro/mpv-winbuild-cmake/releases/download/$Build/$Archive",
    "https://downloads.sourceforge.net/project/mpv-player-windows/libmpv/$Archive"
)

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dest = Join-Path $root 'deps\libmpv\win-x64'
$dll  = Join-Path $dest 'libmpv-2.dll'
$stamp = Join-Path $dest 'VERSION.txt'

if ((Test-Path $dll) -and (Test-Path $stamp) -and ((Get-Content $stamp -Raw).Trim() -eq "$Build-$Commit")) {
    Write-Host "libmpv $Build-$Commit already present."
    exit 0
}

New-Item -ItemType Directory -Force $dest | Out-Null
$tmp = Join-Path ([IO.Path]::GetTempPath()) ("slopfin-libmpv-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $tmp | Out-Null
try {
    $file = Join-Path $tmp $Archive
    $ok = $false
    foreach ($url in $Sources) {
        try {
            Write-Host "Downloading $url"
            [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
            Invoke-WebRequest -Uri $url -OutFile $file -UseBasicParsing -UserAgent 'Slopfin-build'
            $hash = (Get-FileHash $file -Algorithm SHA256).Hash
            if ($hash -ne $Sha256) { Write-Warning "Hash mismatch from $url ($hash)"; continue }
            $ok = $true; break
        } catch { Write-Warning "Download failed from ${url}: $($_.Exception.Message)" }
    }
    if (-not $ok) { throw "Couldn't download a verified copy of $Archive." }

    # Windows 10/11 tar (bsdtar) reads .7z archives.
    & tar -xf $file -C $tmp libmpv-2.dll
    if ($LASTEXITCODE -ne 0) { throw "Extracting $Archive failed." }
    Copy-Item (Join-Path $tmp 'libmpv-2.dll') $dll -Force
    Set-Content -Path $stamp -Value "$Build-$Commit" -Encoding ascii
    Write-Host "libmpv $Build-$Commit -> $dll"
} finally {
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
