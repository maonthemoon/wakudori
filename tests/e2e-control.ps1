# e2eの制御を, アプリやデスクトップを操作せずに確かめる.
$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $false
$e2ePath = Join-Path $PSScriptRoot "e2e.ps1"
$tokens = $null
$errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($e2ePath, [ref]$tokens, [ref]$errors)
if ($errors.Count -gt 0) { throw ($errors -join "`n") }

function Assert([bool]$ok, [string]$name) {
    if (-not $ok) { throw $name }
    Write-Host "PASS $name"
}

# 別プロセスで実行し, exeを探すより前に起動条件を確認する.
$missingExe = "missing-" + [Guid]::NewGuid().ToString("N") + ".exe"
$actions = $env:GITHUB_ACTIONS
$allow = $env:E2E_ALLOW_LOCAL
try {
    $env:GITHUB_ACTIONS = $null
    $env:E2E_ALLOW_LOCAL = $null
    $output = & (Join-Path $PSHOME "pwsh.exe") -NoProfile -File $e2ePath -Exe $missingExe 2>&1 | Out-String
    Assert ($LASTEXITCODE -ne 0 -and $output.Contains("e2eはGitHub Actionsで動かします")) "許可のないローカル実行は準備前に止まる"
    $env:E2E_ALLOW_LOCAL = "1"
    $output = & (Join-Path $PSHOME "pwsh.exe") -NoProfile -File $e2ePath -Exe $missingExe 2>&1 | Out-String
    Assert ($LASTEXITCODE -ne 0 -and $output.Contains($missingExe)) "明示的な許可があればexeの確認に進む"
}
finally {
    $env:GITHUB_ACTIONS = $actions
    $env:E2E_ALLOW_LOCAL = $allow
}

# 本体の関数を読み込み, 窓, キー入力, 再起動を置き換える.
foreach ($name in "Check", "Open-FromHotkey", "Reset-Screens", "Invoke-Case") {
    $definition = $ast.Find({
        param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name
    }, $false)
    . ([scriptblock]::Create($definition.Extent.Text))
}
Add-Type -TypeDefinition @"
using System;
public static class E2E
{
    public static bool Visible = true;
    public static bool CanClose = true;
    public static int FocusQueries;
    public static int EscPresses;
    public static int HotkeyPresses;
    public static int WatchPresses;
    public static int ShowOnHotkey;
    public static int VisibilityQueries;
    public static IntPtr ForegroundWindow(int pid)
    {
        return ++FocusQueries > 1 && Visible ? new IntPtr(123) : IntPtr.Zero;
    }
    public static void Press(int key)
    {
        if (key == 0x57)
        {
            WatchPresses++;
            return;
        }
        if (key != 0x1B || FocusQueries < 2) throw new InvalidOperationException("Unexpected input");
        EscPresses++;
    }
    public static void Press(int key, int modifier)
    {
        if (key != 0x7B || modifier != 0x11) throw new InvalidOperationException("Unexpected hotkey");
        if (++HotkeyPresses == ShowOnHotkey) Visible = true;
    }
    public static bool IsWindowVisible(IntPtr handle)
    {
        if (handle != new IntPtr(123)) throw new InvalidOperationException("Unexpected window");
        if (++VisibilityQueries > 1 && CanClose) Visible = false;
        return Visible;
    }
}
"@
$VK = @{ Esc = 0x1B; F12 = 0x7B; Ctrl = 0x11; W = 0x57 }
$script:proc = @{ HasExited = $false; Id = 1 }
$script:watch = $false
$script:restarts = 0
$script:failed = 0
$script:failureScreens = @()
$script:backgroundOpen = $false
$script:backgroundAtFailure = $false
function Get-Wins { if ([E2E]::Visible) { "Wakudori|800x600|" } }
function Test-Watch { $script:watch }
function Wait-Until([scriptblock]$cond, [int]$ms) {
    for ($i = 0; $i -lt 3; $i++) { if (& $cond) { return $true } }
    return $false
}
function Save-Screen([string]$name) {
    $script:failureScreens += $name
    $script:backgroundAtFailure = $script:backgroundOpen
}
function Restart-App {
    $script:restarts++
    $script:firstHotkeyAfterStart = $true
    $script:watch = $false
    [E2E]::Visible = $false
}

Reset-Screens "選択画面"
Assert ([E2E]::EscPresses -eq 1 -and [E2E]::VisibilityQueries -eq 2 -and
    -not [E2E]::Visible -and $script:restarts -eq 0
) "背後の選択画面が前面に来てからEscを送り, 閉じるまで待つ"
[E2E]::Visible = $true
[E2E]::CanClose = $false
Reset-Screens "選択画面"
Assert ($script:failed -eq 1 -and $script:restarts -eq 1 -and -not [E2E]::Visible) "Escで閉じなければ失敗を記録して再起動する"

[E2E]::Visible = $true
$script:watch = $true
$script:continued = $false
Invoke-Case "4" {
    Check "4 003.pngができる" $false -Required
    $script:continued = $true
} ([ref]$true)
Assert (-not $script:continued -and $script:failureScreens -contains "FAIL_4 003.pngができる" -and
    [E2E]::EscPresses -eq 2 -and $script:restarts -eq 2 -and -not $script:watch -and -not [E2E]::Visible
) "見張りで前提が崩れたらケース内の操作を打ち切り, 再起動して後続のケースに進む"
# ケース4のtry/finallyをそのまま実行し, 見張りの窓が出ないときの順序を確かめる.
$case4 = $ast.Find({
    param($node)
    $node -is [System.Management.Automation.Language.TryStatementAst] -and $node.Finally -and
        $node.Finally.Extent.Text.Contains('$watchBackground.Dispose()')
}, $true)
$watchKeySent = [ref]$false
$watchBackground = [pscustomobject]@{}
$watchBackground | Add-Member -MemberType ScriptMethod -Name Dispose -Value { $script:backgroundOpen = $false }
function Write-Settings([string]$captureKey) {}
function Show-TestBackground($form) { $script:backgroundOpen = $true }
function Open-Selection { $true }
function Test-Overlay { $false }
. ([scriptblock]::Create($case4.Extent.Text))
Assert ([E2E]::WatchPresses -eq 1 -and $script:restarts -eq 3 -and [E2E]::EscPresses -eq 2 -and
    $script:failureScreens -contains "FAIL_4 選択画面が閉じて見張りの窓が出る" -and
    $script:backgroundAtFailure -and -not $script:backgroundOpen
) "見張りの窓が出なくても, 背景が見えている間に失敗画面を保存し, 再起動する"

[E2E]::ShowOnHotkey = 2
$opened = Open-FromHotkey { [E2E]::Visible }
Assert ($opened -and [E2E]::HotkeyPresses -eq 2) "再起動後に最初のホットキーを取りこぼしても送り直す"
[E2E]::Visible = $false
$opened = Open-FromHotkey { [E2E]::Visible }
Assert (-not $opened -and [E2E]::HotkeyPresses -eq 3) "初回以外のホットキーは1回だけ送る"

Restart-App
[E2E]::ShowOnHotkey = 4
$opened = Open-FromHotkey { $false }
Assert (-not $opened -and [E2E]::HotkeyPresses -eq 4) "窓が出たら前面に来るのを待ち, ホットキーを送り直さない"

Restart-App
$opened = Open-FromHotkey { [E2E]::Visible }
Assert (-not $opened -and [E2E]::HotkeyPresses -eq 7) "再起動後も窓が出なければ3回で打ち切る"
exit 0
