# 作ったexeを本物のWindowsで動かし, キーの動きを確かめる(GitHub Actionsのwindows-latestで動かす. H11).
#   pwsh -File tests\e2e.ps1 [-Exe out\Wakudori.exe]
# %APPDATA%\Wakudori\settings.json を書き換えるので, 自分のPCでは動かさない(Actionsの使い捨ての環境用).
# キーはSendInput(keybd_event)で送り, 結果は保存先にできたPNGと, 開いている画面で判定する.
param([string]$Exe = "out\Wakudori.exe")

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$Exe = (Resolve-Path $Exe).Path

Add-Type -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class E2E
{
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr wParam,
        IntPtr lParam, uint flags, uint timeout, out IntPtr result);
    delegate bool EnumProc(IntPtr h, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

    const uint KEYUP = 2;

    // 修飾キーを押したまま, キーを1回押す
    public static void Press(byte vk, params byte[] mods)
    {
        foreach (byte m in mods) keybd_event(m, 0, 0, UIntPtr.Zero);
        keybd_event(vk, 0, 0, UIntPtr.Zero);
        keybd_event(vk, 0, KEYUP, UIntPtr.Zero);
        for (int i = mods.Length - 1; i >= 0; i--) keybd_event(mods[i], 0, KEYUP, UIntPtr.Zero);
    }

    // ホットキーの窓にRestart Managerと同じ終了メッセージを送る.
    public static void SendEndSession(int pid, bool confirmed)
    {
        IntPtr window = IntPtr.Zero;
        EnumWindows(delegate(IntPtr h, IntPtr l)
        {
            uint p;
            GetWindowThreadProcessId(h, out p);
            if (p != pid || IsWindowVisible(h)) return true;
            var title = new StringBuilder(256);
            GetWindowText(h, title, title.Capacity);
            if (title.ToString() != "Wakudori HotkeyWindow") return true;
            window = h;
            return false;
        }, IntPtr.Zero);
        if (window == IntPtr.Zero) throw new InvalidOperationException("ホットキーの窓が見つかりません");
        const uint WM_ENDSESSION = 0x0016;
        const int ENDSESSION_CLOSEAPP = 0x1;
        const uint SMTO_ABORTIFHUNG = 0x2;
        IntPtr result;
        IntPtr sent = SendMessageTimeout(window, WM_ENDSESSION, new IntPtr(confirmed ? 1 : 0),
            new IntPtr(ENDSESSION_CLOSEAPP), SMTO_ABORTIFHUNG, 2000, out result);
        if (!confirmed && sent == IntPtr.Zero) throw new InvalidOperationException("終了取り消しを送れません");
    }

    // そのプロセスの見えている窓: "タイトル|幅x高さ|前面なら*"
    public static string[] Windows(int pid)
    {
        List<string> list = new List<string>();
        IntPtr fg = GetForegroundWindow();
        EnumWindows(delegate(IntPtr h, IntPtr l)
        {
            uint p;
            GetWindowThreadProcessId(h, out p);
            if (p != pid || !IsWindowVisible(h)) return true;
            StringBuilder sb = new StringBuilder(256);
            GetWindowText(h, sb, 256);
            RECT r;
            GetWindowRect(h, out r);
            int w = r.Right - r.Left, ht = r.Bottom - r.Top;
            if (w <= 0 || ht <= 0) return true;
            list.Add(sb.ToString() + "|" + w + "x" + ht + "|" + (h == fg ? "*" : ""));
            return true;
        }, IntPtr.Zero);
        return list.ToArray();
    }
}
"@

$VK = @{ Enter = 0x0D; Ctrl = 0x11; F12 = 0x7B; A = 0x41; E = 0x45; Q = 0x51; W = 0x57; One = 0x31 }
$cfgDir = Join-Path $env:APPDATA "Wakudori"
$shots = Join-Path ([IO.Path]::GetTempPath()) ("wakudori-e2e-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $cfgDir, $shots | Out-Null
$script:failed = 0
$script:proc = $null

function Write-Settings([string]$captureKey, [string]$saveDir = $shots) {
    $s = [ordered]@{ hotkey = "Ctrl+F12"; saveDir = $saveDir }
    if ($captureKey) { $s.captureKey = $captureKey }
    $s | ConvertTo-Json | Set-Content -Encoding utf8NoBOM (Join-Path $cfgDir "settings.json")
}

function Get-Wins { @([E2E]::Windows($script:proc.Id)) }
# 画面全体を覆う窓(選択画面か書き込みの画面). 名前で分ける
function Test-Overlay([switch]$Foreground) {
    [bool](Get-Wins | Where-Object {
        $_ -match '^Wakudori\|(\d+)x' -and [int]$Matches[1] -ge 640 -and (-not $Foreground -or $_.EndsWith('|*'))
    })
}
function Test-Mark { [bool](Get-Wins | Where-Object { $_ -like 'Wakudori - *' }) }
# お知らせ(小さな窓)
function Test-Notice { [bool](Get-Wins | Where-Object { $_ -match '^Wakudori\|(\d+)x' -and [int]$Matches[1] -lt 640 }) }
function Test-Watch { [bool](Get-Wins | Where-Object { $_ -like 'Wakudori - 見張り中|*' }) }
function Test-WatchDone { [bool](Get-Wins | Where-Object { $_ -like 'Wakudori - 見張りを終えました|*' }) }
function Get-Shots { @(Get-ChildItem $shots -Filter *.png | Where-Object { $_.Name -notlike '*_edit.png' }).Count }
function Get-Edits { @(Get-ChildItem $shots -Filter *_edit.png).Count }

function Wait-Until([scriptblock]$cond, [int]$ms = 10000) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $ms) {
        if (& $cond) { return $true }
        Start-Sleep -Milliseconds 100
    }
    return $false
}

function Check([string]$name, [bool]$ok) {
    if ($ok) { Write-Host "PASS $name" }
    else {
        Write-Host "FAIL $name"
        Write-Host ("  窓: " + ((Get-Wins) -join " / "))
        $script:failed++
    }
}

function Open-Selection {
    [E2E]::Press($VK.F12, $VK.Ctrl)
    return (Wait-Until { Test-Overlay })
}

# 選択画面などが残っていれば閉じる(次の確認に持ち越さない)
function Reset-Screens {
    for ($i = 0; $i -lt 3 -and (Get-Wins).Count -gt 0; $i++) {
        [E2E]::Press(0x1B) # Esc
        Start-Sleep -Milliseconds 500
    }
}

function Start-Watch([switch]$Named) {
    if (-not (Open-Selection)) { throw "見張りを始めるための選択画面が開きません" }
    if (-not (Wait-Until { Test-Overlay -Foreground })) { throw "選択画面が前面に来ません" }
    if ($Named) { [E2E]::Press($VK.One) }
    [E2E]::Press($VK.W)
    if (-not (Wait-Until { (Test-Watch) -and -not (Test-Overlay) })) { throw "見張りが始まりません" }
}

try {
    Get-Process Wakudori -ErrorAction SilentlyContinue | Stop-Process -Force
    Remove-Item (Join-Path $cfgDir "regions.json") -ErrorAction SilentlyContinue
    Write-Settings $null
    $script:proc = Start-Process -FilePath $Exe -PassThru
    Start-Sleep -Seconds 3
    Check "起動してお知らせが出ない" (-not $script:proc.HasExited -and (Get-Wins).Count -eq 0)

    # 1. captureKeyなし: Aで撮る
    $n = Get-Shots
    Check "1a 選択画面が開く" (Open-Selection)
    [E2E]::Press($VK.A)
    Check "1a Aで撮る" (Wait-Until { (Get-Shots) -eq $n + 1 })
    Check "1a 撮ったら閉じる" (Wait-Until { -not (Test-Overlay) })
    Reset-Screens

    # 1. Enterもこれまでどおり撮る
    $n = Get-Shots
    Check "1b 選択画面が開く" (Open-Selection)
    [E2E]::Press($VK.Enter)
    Check "1b Enterで撮る" (Wait-Until { (Get-Shots) -eq $n + 1 })
    Reset-Screens

    # 1. 書き込みの画面: Aで完了(書いていないので_edit.pngはできない)
    $n = Get-Shots
    Check "1c 選択画面が開く" (Open-Selection)
    [E2E]::Press($VK.E)
    Check "1c Eで撮って書き込みの画面が開く" ((Wait-Until { (Get-Shots) -eq $n + 1 }) -and (Wait-Until { Test-Mark }))
    [E2E]::Press($VK.A)
    Check "1c Aで書き込みの画面が閉じる" (Wait-Until { -not (Test-Mark) })
    Check "1c 書いていなければ_edit.pngはできない" ((Get-Edits) -eq 0)
    Reset-Screens

    # 2. captureKeyをqにすると, 開き直すだけでQで撮れる. Aでは撮らない
    Write-Settings "q"
    $n = Get-Shots
    Check "2 選択画面が開く" (Open-Selection)
    [E2E]::Press($VK.A)
    Start-Sleep -Seconds 2
    Check "2 Aでは撮らない" ((Get-Shots) -eq $n -and (Test-Overlay))
    [E2E]::Press($VK.Q)
    Check "2 Qで撮る" (Wait-Until { (Get-Shots) -eq $n + 1 })
    Reset-Screens

    # 3. 使えないキー(E)なら, お知らせを出してAを使う
    Write-Settings "E"
    $n = Get-Shots
    [E2E]::Press($VK.F12, $VK.Ctrl)
    $notice = Wait-Until { Test-Notice }
    Check "3 お知らせが出る" $notice
    # お知らせが出なかったときにEnterを送ると, 選択画面で撮れてしまい, 次の確認が偶然通る
    if ($notice) { [E2E]::Press($VK.Enter) }
    Check "3 お知らせを閉じると選択画面が開く" (Wait-Until { Test-Overlay })
    [E2E]::Press($VK.A)
    Check "3 Aで撮る" (Wait-Until { (Get-Shots) -eq $n + 1 })
    Reset-Screens

    # T5の回帰確認. 見張りが安定して撮れるよう, 主モニターに白い窓を出す.
    Add-Type -AssemblyName System.Windows.Forms
    $background = New-Object System.Windows.Forms.Form
    try {
        $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
        $background.FormBorderStyle = 'None'
        $background.StartPosition = 'Manual'
        $background.Bounds = $bounds
        $background.BackColor = [System.Drawing.Color]::White
        $background.Show()
        $background.Refresh()
        [System.Windows.Forms.Application]::DoEvents()
        [System.Windows.Forms.Cursor]::Position = [System.Drawing.Point]::new(($bounds.X + 200), ($bounds.Y + 200))

        Write-Settings $null
        $regionsPath = Join-Path $cfgDir "regions.json"
        $region = @{ name = "watch-regression"; x = ($bounds.X + 200); y = ($bounds.Y + 200); width = 320; height = 240 }
        @{ regions = @($region) } | ConvertTo-Json | Set-Content -Encoding utf8NoBOM $regionsPath
        Start-Watch -Named
        $last = (Get-Content -Raw $regionsPath | ConvertFrom-Json).last
        Check "T5 R2 見張った範囲の座標だけを保存する" ($last.x -eq $region.x -and $last.y -eq $region.y -and
            $last.width -eq $region.width -and $last.height -eq $region.height -and $null -eq $last.name)
        $singleShotDir = Join-Path $shots "single"
        New-Item -ItemType Directory -Path $singleShotDir | Out-Null
        Write-Settings $null $singleShotDir
        [E2E]::Press($VK.F12, $VK.Ctrl)
        if (-not (Wait-Until { Test-WatchDone })) { throw "見張りの終了通知が出ません" }
        [E2E]::Press($VK.F12, $VK.Ctrl)
        if (-not (Wait-Until { Test-Overlay } 3000)) { throw "終了通知が出た後に選択画面が開きません" }
        Check "T5 R1 撮影を開くと終了通知がすぐ閉じる" (-not (Test-WatchDone))
        if (-not (Wait-Until { Test-Overlay -Foreground })) { throw "選択画面が前面に来ません" }
        [E2E]::Press($VK.Enter)
        if (-not (Wait-Until { @(Get-ChildItem $singleShotDir -Filter *.png).Count -eq 1 })) {
            throw "前回の範囲の画像が保存されません"
        }
        $newShot = @(Get-ChildItem $singleShotDir -Filter *.png -Name)
        Check "T5 R2 前回の範囲のファイル名は日時だけ" ($newShot.Count -eq 1 -and $newShot[0] -match '^\d{8}_\d{6}\.png$')

        # ファイルを保存先に指定して, 画像の保存を失敗させる.
        $blockedPath = Join-Path $shots "blocked"
        Set-Content -Path $blockedPath -Value "file"
        Write-Settings $null $blockedPath
        Start-Watch
        if (-not (Wait-Until { (Test-Notice) -and -not (Test-Watch) })) { throw "見張りの保存エラーが出ません" }
        [E2E]::Press($VK.F12, $VK.Ctrl)
        Start-Sleep -Milliseconds 500
        Check "T5 R4 保存エラーを表示中は選択画面を開かない" ((Test-Notice) -and -not (Test-Overlay))
        Reset-Screens
        if (Test-Notice) { throw "見張りの保存エラーを閉じられません" }

        Write-Settings $null
        $olderFolders = @(Get-ChildItem $shots -Directory -Name)
        Start-Watch
        $savedFolder = [ref]''
        if (-not (Wait-Until {
            $folder = Get-ChildItem $shots -Directory | Where-Object {
                $_.Name -match '^見張り_\d{8}_\d{6}(_\d+)?$' -and
                    $_.Name -notin $olderFolders -and (Test-Path (Join-Path $_.FullName "001.png"))
            } | Select-Object -First 1
            if ($null -eq $folder) { return $false }
            $savedFolder.Value = $folder.FullName
            return $true
        })) { throw "終了前に見張りの画像が保存されません" }
        [E2E]::SendEndSession($script:proc.Id, $false)
        Check "T5 Restart Managerの終了要求が取り消されたら見張りを続ける" (Test-Watch)
        [E2E]::SendEndSession($script:proc.Id, $true)
        Check "T5 Restart Managerの終了要求でプロセスが正常に終了する" (
            (Wait-Until { $script:proc.HasExited }) -and $script:proc.ExitCode -eq 0)
        $folderRecord = "`tfolder`t1`t" + $savedFolder.Value
        $records = @(Get-Content (Join-Path $cfgDir "captures.log") | Where-Object { $_.EndsWith($folderRecord) })
        Check "T5 終了確定時は確定済みフォルダーを記録する" (
            $records.Count -eq 1 -and -not (Test-Watch) -and -not (Test-WatchDone) -and -not (Test-Notice))
    }
    finally {
        $background.Dispose()
    }
}
catch {
    Write-Host "FAIL 例外: $_"
    $script:failed++
}
finally {
    if ($script:proc -and -not $script:proc.HasExited) { $script:proc | Stop-Process -Force }
    $log = Join-Path $cfgDir "error.log"
    if (Test-Path $log) { Write-Host "error.log:"; Get-Content $log }
    Remove-Item $shots -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ($(if ($script:failed -eq 0) { "ALL PASS" } else { "$($script:failed) FAILED" }))
exit $(if ($script:failed -eq 0) { 0 } else { 1 })
