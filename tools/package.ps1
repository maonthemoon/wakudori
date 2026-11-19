# 配る用のZIPを作る. 先に build.cmd で bin\Wakudori.exe を作っておく.
#   powershell -ExecutionPolicy Bypass -File tools\package.ps1
# 中身: Wakudori.exe, manual\index.html と manual\images\, LICENSE.txt
# バージョンは src\AssemblyInfo.cs の AssemblyInformationalVersion から読む.
param([string]$Version = "")

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not $Version) {
    $m = Select-String -Path "src\AssemblyInfo.cs" -Pattern 'AssemblyInformationalVersion\("([^"]+)"\)'
    if (-not $m) { throw "src\AssemblyInfo.cs にバージョンがありません" }
    $Version = $m.Matches[0].Groups[1].Value
}
if (-not (Test-Path "bin\Wakudori.exe")) { throw "bin\Wakudori.exe がありません. 先に build.cmd を実行してください" }

$name = "Wakudori-$Version"
$stage = Join-Path "dist" $name
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path "$stage\manual\images" -Force | Out-Null

Copy-Item "bin\Wakudori.exe" $stage
Copy-Item "LICENSE" "$stage\LICENSE.txt"
if (Test-Path "docs\index.html") { Copy-Item "docs\index.html" "$stage\manual\index.html" }
if (Test-Path "docs\images") { Copy-Item "docs\images\*" "$stage\manual\images\" -Recurse }

$zip = Join-Path "dist" "$name.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $stage -DestinationPath $zip
Write-Host "Created $zip"
if ($env:GITHUB_OUTPUT) {
    "zip=$zip" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
    "version=$Version" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
}
