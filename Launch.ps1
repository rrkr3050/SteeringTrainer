$ErrorActionPreference = 'Stop'
$logDirectory = Join-Path $env:LOCALAPPDATA 'SteeringTrainer\logs'
New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
$runId = '{0}-{1}' -f (Get-Date -Format 'yyyyMMdd-HHmmss'), $PID
$launchLog = Join-Path $logDirectory "launch-$runId.log"
$stdoutLog = Join-Path $logDirectory "stdout-$runId.log"
$stderrLog = Join-Path $logDirectory "stderr-$runId.log"
function Write-LaunchLog([string] $message) {
    Add-Content -LiteralPath $launchLog -Value "$(Get-Date -Format o) $message" -Encoding UTF8
}
try {
    Write-LaunchLog "Launcher started. Project=$PSScriptRoot"
    $dotnetPath = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
    if (-not (Test-Path -LiteralPath $dotnetPath)) {
        $dotnetPath = (Get-Command dotnet.exe -ErrorAction Stop).Source
    }
    Write-LaunchLog "Using .NET: $dotnetPath"
    $appDirectory = Join-Path $PSScriptRoot 'SteeringTrainer.App\bin\x64\Release\net8.0-windows'
    $appDll = Join-Path $appDirectory 'SteeringTrainer.App.dll'
    if (-not (Test-Path -LiteralPath $appDll)) {
        Write-LaunchLog 'Application files missing. Building Release.'
        & $dotnetPath build (Join-Path $PSScriptRoot 'SteeringTrainer.sln') -c Release *> (Join-Path $logDirectory "build-$runId.log")
        if ($LASTEXITCODE -ne 0) { throw 'Build failed. See the build log.' }
    }
    $process = Start-Process -FilePath $dotnetPath -ArgumentList ('"' + $appDll + '"') -WorkingDirectory $appDirectory -WindowStyle Hidden -RedirectStandardOutput $stdoutLog -RedirectStandardError $stderrLog -PassThru
    Write-LaunchLog "Application started. PID=$($process.Id)"
    $windowReported = $false
    while (-not $process.WaitForExit(1000)) {
        $process.Refresh()
        if (-not $windowReported -and $process.MainWindowHandle -ne 0) {
            Write-LaunchLog 'Application window detected.'
            $windowReported = $true
        }
    }
    Write-LaunchLog "Application exited. Code=$($process.ExitCode)"
    if ($process.ExitCode -ne 0) { throw "Application exited with code $($process.ExitCode). See app/stderr logs." }
} catch {
    Write-LaunchLog "Launch failure: $($_.Exception)"
    Add-Type -AssemblyName PresentationFramework
    [System.Windows.MessageBox]::Show("Steering Trainer could not run.`n`n$($_.Exception.Message)`n`nLogs: $logDirectory", 'Steering Trainer', 'OK', 'Error') | Out-Null
    exit 1
}
