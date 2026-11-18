param([string]$Mode = "-shots", [int]$TimeoutSec = 400)
# Runs Build/ADDRESS.exe in batchmode (GPU, no window, muted) for a test mode and ALWAYS kills it afterwards.
$exe = "C:\Users\aurel\ADDRESS\Build\ADDRESS.exe"
$log = "C:\Users\aurel\ADDRESS\Docs\reports\logs\build$($Mode.TrimStart('-')).log"
$p = Start-Process $exe -ArgumentList '-batchmode', $Mode, '-logFile', $log -PassThru
if (-not $p.WaitForExit($TimeoutSec * 1000)) { "TIMEOUT - killed" }
Get-Process ADDRESS -ErrorAction SilentlyContinue | Stop-Process -Force
Select-String $log -Pattern '\[ADDRESS\]|Exception' | Select-Object -Last 80 | ForEach-Object { $_.Line }
