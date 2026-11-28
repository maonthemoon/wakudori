# 作ったexeを本物のWindowsで動かし, キーの動きを確かめる(GitHub Actionsのwindows-latestで動かす. H11).
#   pwsh -File tests\e2e.ps1 [-Exe out\Wakudori.exe]
# %APPDATA%\Wakudori\settings.json を書き換えるので, 自分のPCでは動かさない(Actionsの使い捨ての環境用).
# キーはSendInput(keybd_event)で送り, 結果は保存先にできたPNGと, 開いている画面で判定する.
param([string]$Exe = "out\Wakudori.exe")

$ErrorActionPreference = "Stop"
if ($env:GITHUB_ACTIONS -ne "true" -and $env:E2E_ALLOW_LOCAL -ne "1") {
    throw "e2eはGitHub Actionsで動かします. 使い捨ての仮想マシンで動かす場合は E2E_ALLOW_LOCAL=1 を付けてください."
}
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
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr wParam,
        IntPtr lParam, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll", SetLastError = true)] static extern bool BitBlt(IntPtr dest, int x, int y,
        int width, int height, IntPtr source, int sourceX, int sourceY, uint operation);
    delegate bool EnumProc(IntPtr h, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

    const uint KEYUP = 2;

    // 透過ウィンドウの枠と札も含めて撮る.
    public static void CopyDesktop(IntPtr dest, int x, int y, int width, int height)
    {
        const uint SRCCOPY = 0x00CC0020;
        const uint CAPTUREBLT = 0x40000000;
        IntPtr source = GetDC(IntPtr.Zero);
        if (source == IntPtr.Zero) throw new InvalidOperationException("画面のDCを取得できません");
        try
        {
            if (!BitBlt(dest, 0, 0, width, height, source, x, y, SRCCOPY | CAPTUREBLT))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        finally { ReleaseDC(IntPtr.Zero, source); }
    }

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

    // 指定したプロセスの前面にある窓だけを返す.
    public static IntPtr ForegroundWindow(int pid)
    {
        IntPtr window = GetForegroundWindow();
        if (!IsWindowVisible(window)) return IntPtr.Zero;
        uint owner;
        GetWindowThreadProcessId(window, out owner);
        return owner == pid ? window : IntPtr.Zero;
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

$VK = @{ Enter = 0x0D; Esc = 0x1B; Ctrl = 0x11; F12 = 0x7B; A = 0x41; E = 0x45; Q = 0x51; W = 0x57; One = 0x31 }
$cfgDir = Join-Path $env:APPDATA "Wakudori"
$shots = Join-Path ([IO.Path]::GetTempPath()) ("wakudori-e2e-" + [Guid]::NewGuid().ToString("N"))
$screens = $env:E2E_SCREENS
if (-not $screens) { $screens = Join-Path ([IO.Path]::GetTempPath()) "e2e-screens" }
New-Item -ItemType Directory -Force -Path $cfgDir, $shots | Out-Null
$script:failed = 0
$script:proc = $null
$script:screenNumber = 0
$script:firstHotkeyAfterStart = $false

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
function Test-Mark([switch]$Foreground) {
    [bool](Get-Wins | Where-Object {
        $_ -like 'Wakudori - 書き込み|*' -and (-not $Foreground -or $_.EndsWith('|*'))
    })
}
# お知らせ(小さな窓)
function Test-Notice([switch]$Foreground) {
    [bool](Get-Wins | Where-Object {
        $_ -match '^Wakudori\|(\d+)x' -and [int]$Matches[1] -lt 640 -and (-not $Foreground -or $_.EndsWith('|*'))
    })
}
function Test-Watch { [bool](Get-Wins | Where-Object { $_ -like 'Wakudori - 見張り中|*' }) }
function Test-WatchDone { [bool](Get-Wins | Where-Object { $_ -like 'Wakudori - 見張りを終えました|*' }) }
function Get-Shots { @(Get-ChildItem $shots -Filter *.png | Where-Object { $_.Name -notlike '*_edit.png' }).Count }
function Get-Edits { @(Get-ChildItem $shots -Filter *_edit.png).Count }

function Wait-Until([scriptblock]$cond, [int]$ms = 10000) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $ms) {
        [System.Windows.Forms.Application]::DoEvents()
        if (& $cond) { return $true }
        Start-Sleep -Milliseconds 100
    }
    return [bool](& $cond)
}

function Save-Screen([string]$name) {
    New-Item -ItemType Directory -Force -Path $screens | Out-Null
    $script:screenNumber++
    $safe = $name -replace '[\\/:*?"<>|\x00-\x1F]', '_'
    $path = Join-Path $screens ("{0:D2}_{1}.png" -f $script:screenNumber, $safe)
    $bounds = [System.Windows.Forms.SystemInformation]::VirtualScreen
    $bitmap = [System.Drawing.Bitmap]::new($bounds.Width, $bounds.Height)
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $dc = $graphics.GetHdc()
            try { [E2E]::CopyDesktop($dc, $bounds.X, $bounds.Y, $bounds.Width, $bounds.Height) }
            finally { $graphics.ReleaseHdc($dc) }
        }
        finally { $graphics.Dispose() }
        $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $bitmap.Dispose() }
    Write-Host "  画面: $path"
}

function Check([string]$name, [bool]$ok, [switch]$Required) {
    if ($ok) { Write-Host "PASS $name" }
    else {
        if ($Required) { throw $name }
        Write-Host "FAIL $name"
        $script:failed++
        if ($script:proc) { Write-Host ("  窓: " + ((Get-Wins) -join " / ")) }
        try { Save-Screen "FAIL_$name" }
        catch { Write-Host "  画面を保存できません: $_" }
    }
}

function Open-FromHotkey([scriptblock]$expected) {
    $retry = $script:firstHotkeyAfterStart
    $script:firstHotkeyAfterStart = $false
    [E2E]::Press($VK.F12, $VK.Ctrl)
    if (-not $retry) { return (Wait-Until $expected) }

    # 起動直後は登録が間に合わないことがある. 窓が出たらキーを送り直さない.
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        if (Wait-Until { (& $expected) -or (Get-Wins).Count -gt 0 } 3500) {
            return (Wait-Until $expected)
        }
        if ($attempt -lt 3 -and (Get-Wins).Count -eq 0 -and -not $script:proc.HasExited) {
            [E2E]::Press($VK.F12, $VK.Ctrl)
        }
    }
    return $false
}

function Open-Selection {
    return (Open-FromHotkey { Test-Overlay -Foreground })
}

function Restart-App {
    if ($script:proc -and -not $script:proc.HasExited) {
        $script:proc | Stop-Process -Force
        if (-not $script:proc.WaitForExit(5000)) { throw "Wakudoriが終了しません" }
    }
    $showFrame = $env:WAKUDORI_E2E_SHOW_WATCH_FRAME
    try {
        $env:WAKUDORI_E2E_SHOW_WATCH_FRAME = "1"
        $script:proc = Start-Process -FilePath $Exe -PassThru
    }
    finally { $env:WAKUDORI_E2E_SHOW_WATCH_FRAME = $showFrame }
    if ($script:proc.HasExited) { throw "Wakudoriが起動しません" }
    $script:firstHotkeyAfterStart = $true
}

# 対象が前面に来るまで待ち, Escを送った窓が閉じたことを確かめる.
function Reset-Screens([string]$name) {
    for ($i = 0; $i -lt 3 -and -not $script:proc.HasExited; $i++) {
        # 見張りの枠は前面に来ないため, 残っていれば再起動する.
        if (Test-Watch) { break }
        $target = [ref]([IntPtr]::Zero)
        $ready = Wait-Until {
            if ((Get-Wins).Count -eq 0) { return $true }
            $target.Value = [E2E]::ForegroundWindow($script:proc.Id)
            return $target.Value -ne [IntPtr]::Zero
        }
        if (-not $ready -or $script:proc.HasExited) { break }
        if ((Get-Wins).Count -eq 0) { return }

        if ($target.Value -eq [IntPtr]::Zero -or $target.Value -ne [E2E]::ForegroundWindow($script:proc.Id)) {
            continue
        }
        [E2E]::Press($VK.Esc)
        if (-not (Wait-Until { -not [E2E]::IsWindowVisible($target.Value) })) { break }
        # 設定のお知らせが閉じると選択画面が開くので, 続けて出る窓も待つ.
        [void](Wait-Until { (Get-Wins).Count -gt 0 } 500)
    }
    if (-not $script:proc.HasExited -and (Get-Wins).Count -eq 0) { return }
    Check "$name ケース後にWakudoriの窓を閉じる" $false
    Restart-App
    Check "$name 再起動して窓が残っていない" ((Get-Wins).Count -eq 0) -Required
}

function Invoke-Case([string]$name, [scriptblock]$run, [ref]$watchKeySent) {
    $failedBefore = $script:failed
    try { & $run }
    catch { Check "$_" $false }
    finally {
        # Wのあとに失敗したら, 枠が見えなくても見張りが動いている可能性がある.
        if ($watchKeySent.Value -and $script:failed -gt $failedBefore) {
            Restart-App
            Check "$name 再起動して窓が残っていない" ((Get-Wins).Count -eq 0) -Required
        }
        else { Reset-Screens $name }
    }
}

function Check-InvalidCaptureKey([string]$number) {
    $n = Get-Shots
    $notice = Open-FromHotkey { Test-Notice -Foreground }
    Check "$number お知らせが出る" $notice
    if (-not $notice) { return }

    [E2E]::Press($VK.Enter)
    $selected = Wait-Until { Test-Overlay -Foreground }
    Check "$number お知らせを閉じると選択画面が開く" $selected
    if (-not $selected) { return }

    [E2E]::Press($VK.A)
    Check "$number Aで撮る" (Wait-Until { (Get-Shots) -eq $n + 1 })
}

function Test-RedRectangle([System.Drawing.Bitmap]$bitmap) {
    $centerX = [int][Math]::Floor($bitmap.Width / 2)
    $centerY = [int][Math]::Floor($bitmap.Height / 2)
    if ($centerX -lt 10 -or $centerX + 10 -ge $bitmap.Width) { return $false }

    $red = [System.Drawing.Color]::Red.ToArgb()
    foreach ($offset in -10, 0, 10) {
        if ($bitmap.GetPixel($centerX + $offset, $centerY).ToArgb() -ne $red) { return $false }
    }
    return $true
}

function Show-TestBackground($form) {
    $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $form.FormBorderStyle = 'None'
    $form.StartPosition = 'Manual'
    $form.Bounds = $bounds
    $form.BackColor = [System.Drawing.Color]::White
    $form.Show()
    $form.Refresh()
    [System.Windows.Forms.Application]::DoEvents()
}

function Start-Watch([switch]$Named) {
    if (-not (Open-Selection)) { throw "見張りを始めるための選択画面が開きません" }
    if ($Named) { [E2E]::Press($VK.One) }
    [E2E]::Press($VK.W)
    if (-not (Wait-Until { (Test-Watch) -and -not (Test-Overlay) })) { throw "見張りが始まりません" }
}

try {
    Add-Type -AssemblyName System.Windows.Forms
    Get-Process Wakudori -ErrorAction SilentlyContinue | Stop-Process -Force
    Remove-Item (Join-Path $cfgDir "regions.json") -ErrorAction SilentlyContinue
    # 初期範囲が主モニターの中央に来るようにして, 見張りまで同じ範囲を使う.
    $primary = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    [System.Windows.Forms.Cursor]::Position = [System.Drawing.Point]::new(
        ($primary.X + [int]($primary.Width / 2)), ($primary.Y + [int]($primary.Height / 2)))
    Write-Settings $null
    Restart-App
    Check "起動してお知らせが出ない" (
        $script:proc.WaitForInputIdle(10000) -and -not $script:proc.HasExited -and (Get-Wins).Count -eq 0)

    # 1. captureKeyなし: Aで撮る
    $n = Get-Shots
    $selected = Open-Selection
    Check "1a 選択画面が開く" $selected
    if ($selected) {
        [E2E]::Press($VK.A)
        Check "1a Aで撮る" (Wait-Until { (Get-Shots) -eq $n + 1 })
        Check "1a 撮ったら閉じる" (Wait-Until { -not (Test-Overlay) })
    }
    Reset-Screens "1a"

    # 1. Enterもこれまでどおり撮る
    $n = Get-Shots
    $selected = Open-Selection
    Check "1b 選択画面が開く" $selected
    if ($selected) {
        [E2E]::Press($VK.Enter)
        Check "1b Enterで撮る" (Wait-Until { (Get-Shots) -eq $n + 1 })
    }
    Reset-Screens "1b"

    # 1. 書き込みの画面: Aで完了(書いていないので_edit.pngはできない)
    $n = Get-Shots
    $selected = Open-Selection
    Check "1c 選択画面が開く" $selected
    if ($selected) {
        [E2E]::Press($VK.E)
        $mark = (Wait-Until { (Get-Shots) -eq $n + 1 }) -and (Wait-Until { Test-Mark -Foreground })
        Check "1c Eで撮って書き込みの画面が開く" $mark
        if ($mark) {
            [E2E]::Press($VK.A)
            Check "1c Aで書き込みの画面が閉じる" (Wait-Until { -not (Test-Mark) })
            Check "1c 書いていなければ_edit.pngはできない" ((Get-Edits) -eq 0)
        }
    }
    Reset-Screens "1c"

    # 2. captureKeyをqにすると, 開き直すだけでQで撮れる. Aでは撮らない
    Write-Settings "q"
    $n = Get-Shots
    $selected = Open-Selection
    Check "2 選択画面が開く" $selected
    if ($selected) {
        [E2E]::Press($VK.A)
        Start-Sleep -Seconds 2
        $selected = (Get-Shots) -eq $n -and (Test-Overlay -Foreground)
        Check "2 Aでは撮らない" $selected
        if ($selected) {
            [E2E]::Press($VK.Q)
            Check "2 Qで撮る" (Wait-Until { (Get-Shots) -eq $n + 1 })
        }
    }
    Reset-Screens "2"

    # 3. 使えないキー(E)なら, お知らせを出してAを使う
    Write-Settings "E"
    Check-InvalidCaptureKey "3"
    Reset-Screens "3"

    # 4. 前回の範囲を見張り, 白・青・青地に赤の3枚を撮る.
    $watchKeySent = [ref]$false
    $watchBackground = [System.Windows.Forms.Form]::new()
    try {
        Invoke-Case "4" {
            Write-Settings $null
            $watchBackground.TopMost = $true
            Show-TestBackground $watchBackground
            $watchBackground.Activate()
            Write-Host ("  4 テスト用の窓にフォーカスがある: " +
                ([E2E]::ForegroundWindow($PID) -eq $watchBackground.Handle))
            Check "4 選択画面が開く" (Open-Selection) -Required
            $watchKeySent.Value = $true
            [E2E]::Press($VK.W)
            Check "4 選択画面が閉じて見張りの窓が出る" (Wait-Until { -not (Test-Overlay) -and (Test-Watch) }) -Required

            $watchFolder = [ref]''
            Check "4 001.pngができる" (Wait-Until {
                $folder = Get-ChildItem -LiteralPath $shots -Directory -Filter '見張り_*' | Select-Object -First 1
                if ($null -eq $folder -or -not (Test-Path -LiteralPath (Join-Path $folder.FullName '001.png') -PathType Leaf)) {
                    return $false
                }
                $watchFolder.Value = $folder.FullName
                return $true
            } 10000) -Required
            $second = Join-Path $watchFolder.Value '002.png'
            Check "4 色を変える前は002.pngがない" (-not (Test-Path -LiteralPath $second)) -Required
            $watchBackground.BackColor = [System.Drawing.Color]::Blue
            $watchBackground.Refresh()
            [System.Windows.Forms.Application]::DoEvents()
            Check "4 青に変えると002.pngができる" (Wait-Until { Test-Path -LiteralPath $second -PathType Leaf } 10000) -Required

            $third = Join-Path $watchFolder.Value '003.png'
            Check "4 長方形を出す前は003.pngがない" (-not (Test-Path -LiteralPath $third)) -Required
            $panel = [System.Windows.Forms.Panel]::new()
            $panel.BackColor = [System.Drawing.Color]::Red
            # 800x600のうち60x20は0.25%. 2分の1に縮小すると30x10で,
            # 全体のしきい値0.5%に届かず, 16x16タイルの12画素以上で検出される.
            $panel.Size = [System.Drawing.Size]::new(60, 20)
            $panel.Location = [System.Drawing.Point]::new(
                ([int](($watchBackground.ClientSize.Width - $panel.Width) / 2)),
                ([int](($watchBackground.ClientSize.Height - $panel.Height) / 2)))
            $watchBackground.Controls.Add($panel)
            $watchBackground.Refresh()
            [System.Windows.Forms.Application]::DoEvents()
            Write-Host ("  4 赤い長方形を出した窓にフォーカスがある: " +
                ([E2E]::ForegroundWindow($PID) -eq $watchBackground.Handle))
            Check "4 赤い長方形を出すと003.pngができる" (Wait-Until { Test-Path -LiteralPath $third -PathType Leaf } 10000) -Required
            # 色判定で中断しても, 判定対象の画像を成果物に残す.
            New-Item -ItemType Directory -Force -Path $screens | Out-Null
            Copy-Item -LiteralPath $third -Destination (Join-Path $screens '4 撮影 003.png')
            $bitmap = [System.Drawing.Bitmap]::new($third)
            try { Check "4 003.pngの中央付近に赤い長方形が写る" (Test-RedRectangle $bitmap) -Required }
            finally { $bitmap.Dispose() }

            $fourth = Join-Path $watchFolder.Value '004.png'
            Check "4 5秒間変化がなければ004.pngはできない" (-not (Wait-Until { Test-Path -LiteralPath $fourth } 5000)) -Required
            Check "4 見張りが続いている" (Test-Watch) -Required
            Save-Screen "4 見張り中"
            [E2E]::Press($VK.F12, $VK.Ctrl)
            Check "4 ホットキーで見張りが止まり終了の知らせが出る" (Wait-Until { -not (Test-Watch) -and (Test-WatchDone) }) -Required
            Save-Screen "4 止めた"

            $captureLog = Join-Path $cfgDir "captures.log"
            $folderRecord = "`tfolder`t3`t" + $watchFolder.Value
            $records = @(if (Test-Path -LiteralPath $captureLog -PathType Leaf) {
                Get-Content -LiteralPath $captureLog | Where-Object { $_.EndsWith($folderRecord) }
            })
            Check "4 captures.logに3枚と記録される" ($records.Count -eq 1)
            # 止めたあとも範囲を変えて, 撮影が再開しないことを確かめる.
            $panel.Hide()
            $watchBackground.Refresh()
            [System.Windows.Forms.Application]::DoEvents()
            Check "4 止めたあと5秒待っても画像は3枚のまま" (-not (Wait-Until {
                @(Get-ChildItem -LiteralPath $watchFolder.Value -File -Filter '*.png').Count -ne 3
            } 5000))
            # 枠と札が保存範囲に入っていないかを, 画面全体の画像と見比べられるようにする.
            foreach ($file in '001.png', '002.png') {
                Copy-Item -LiteralPath (Join-Path $watchFolder.Value $file) -Destination (Join-Path $screens "4 撮影 $file")
            }
        } $watchKeySent
    }
    finally { $watchBackground.Dispose() }

    # 5. Wは見張りのキーなので, 撮影キーに設定するとお知らせが出る.
    Write-Settings "w"
    Check-InvalidCaptureKey "5"
    Reset-Screens "5"

    # T5の回帰確認. 見張りが安定して撮れるよう, 主モニターに白い窓を出す.
    $background = New-Object System.Windows.Forms.Form
    try {
        Show-TestBackground $background
        $bounds = $background.Bounds
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
        Reset-Screens "T5 R4"
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
    Check "$_" $false
}
finally {
    if ($script:proc -and -not $script:proc.HasExited) { $script:proc | Stop-Process -Force }
    $log = Join-Path $cfgDir "error.log"
    if (Test-Path $log) { Write-Host "error.log:"; Get-Content $log }
    Remove-Item $shots -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ($(if ($script:failed -eq 0) { "ALL PASS" } else { "$($script:failed) FAILED" }))
exit $(if ($script:failed -eq 0) { 0 } else { 1 })
