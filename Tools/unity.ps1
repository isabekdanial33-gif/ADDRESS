param([string]$Method = "", [string]$Extra = "", [switch]$NoGraphics, [int]$TimeoutMin = 60)
# Runs the ADDRESS Unity project in batchmode and prints compile errors / exceptions / [ADDRESS] lines.
$unity = "C:\Program Files\Unity\Hub\Editor\6000.4.6f1\Editor\Unity.exe"
$proj = "C:\Users\aurel\ADDRESS\Game"
$logDir = "C:\Users\aurel\ADDRESS\Docs\reports\logs"
New-Item -ItemType Directory -Force $logDir | Out-Null
$name = if ($Method) { ($Method -split '\.')[-1] } else { "open" }
$log = Join-Path $logDir "$name.log"
$a = @('-batchmode', '-projectPath', $proj, '-logFile', $log)
if ($NoGraphics) { $a += '-nographics' }
if ($Method) { $a += @('-executeMethod', $Method) }
if ($Extra) { $a += ($Extra -split ' ') }
$a += '-quit'
$p = Start-Process $unity -ArgumentList $a -PassThru
if (-not $p.WaitForExit($TimeoutMin * 60000)) { $p.Kill(); "TIMEOUT" }
"exit=$($p.ExitCode)"
Select-String -Path $log -Pattern 'error CS|Exception:|\[ADDRESS\]|Aborting batchmode|executeMethod method|Compilation failed' | Where-Object { $_.Line -notmatch 'Start importing' } | Select-Object -First 60 | ForEach-Object { $_.Line }
