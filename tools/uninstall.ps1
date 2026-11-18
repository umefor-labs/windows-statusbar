# Removes autostart (Run key + sensors task) and the installed exe. Run from an elevated PowerShell.
Unregister-ScheduledTask -TaskName 'WinStatusBar' -Confirm:$false -ErrorAction SilentlyContinue
Unregister-ScheduledTask -TaskName 'WinStatusBar Sensors' -Confirm:$false -ErrorAction SilentlyContinue
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'WinStatusBar' -ErrorAction SilentlyContinue
Get-Process WinStatusBar -ErrorAction SilentlyContinue | ForEach-Object { $_.CloseMainWindow() | Out-Null; $_.WaitForExit(5000) | Out-Null }
Get-Process WinStatusBar -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-Item -Recurse -Force (Join-Path $env:LOCALAPPDATA 'Programs\WinStatusBar') -ErrorAction SilentlyContinue
'WinStatusBar removed.'
