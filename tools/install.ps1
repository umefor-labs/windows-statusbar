# Installs WinStatusBar for the current user. Run once from an elevated PowerShell.
# - The bar starts at logon with normal rights (HKCU Run), so its web logins work.
# - A small helper (WinStatusBar.exe --sensors) starts at logon with admin rights via a
#   scheduled task, only to read the CPU temperature into sensors.json.
param([string]$Source = (Join-Path $PSScriptRoot '..\publish\WinStatusBar.exe'))

$ErrorActionPreference = 'Stop'
$dir = Join-Path $env:LOCALAPPDATA 'Programs\WinStatusBar'
$exe = Join-Path $dir 'WinStatusBar.exe'
$log = Join-Path $env:TEMP 'WinStatusBar-install.log'

try {
    New-Item -ItemType Directory -Force -Path $dir | Out-Null

    # Close the running bar (it releases its screen strip on close) and the sensors helper.
    $running = Get-Process WinStatusBar -ErrorAction SilentlyContinue
    if ($running) {
        Add-Type 'using System;using System.Runtime.InteropServices;public class W{[DllImport("user32.dll",CharSet=CharSet.Unicode)]public static extern IntPtr FindWindow(string c,string t);[DllImport("user32.dll")]public static extern bool PostMessage(IntPtr h,int m,IntPtr w,IntPtr l);}'
        $h = [W]::FindWindow($null, 'WinStatusBar')
        if ($h -ne [IntPtr]::Zero) { [W]::PostMessage($h, 0x10, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null }
        Start-Sleep -Seconds 2
        Get-Process WinStatusBar -ErrorAction SilentlyContinue | Stop-Process -Force
    }

    # The old exe can stay locked for a moment after its process exits.
    for ($i = 0; $i -lt 20; $i++) {
        try { Copy-Item -Force $Source $exe; break }
        catch { if ($i -eq 19) { throw }; Start-Sleep -Milliseconds 500 }
    }

    # Old setup ran the whole bar elevated; replace it with the sensors-only task.
    Unregister-ScheduledTask -TaskName 'WinStatusBar' -Confirm:$false -ErrorAction SilentlyContinue

    $user = "$env:USERDOMAIN\$env:USERNAME"
    $action = New-ScheduledTaskAction -Execute $exe -Argument '--sensors' -WorkingDirectory $dir
    $trigger = New-ScheduledTaskTrigger -AtLogOn -User $user
    $principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Highest
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
        -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew -Priority 4
    Register-ScheduledTask -TaskName 'WinStatusBar Sensors' -Action $action -Trigger $trigger `
        -Principal $principal -Settings $settings -Force | Out-Null

    $run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    Set-ItemProperty -Path $run -Name 'WinStatusBar' -Value "`"$exe`""

    Start-ScheduledTask -TaskName 'WinStatusBar Sensors'
    # explorer.exe starts the bar with the user's normal (non-admin) rights.
    Start-Process explorer.exe -ArgumentList "`"$exe`""
    "OK $(Get-Date -Format s) installed to $exe" | Set-Content $log
}
catch {
    "FAIL $(Get-Date -Format s) $($_.Exception.Message)" | Set-Content $log
}
