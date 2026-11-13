param(
    [Parameter(Mandatory = $true)]
    [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'
$sourceDirectory = $PSScriptRoot
$aliasRoot = Join-Path $env:LOCALAPPDATA 'SteeringTrainer'
$sourceAlias = Join-Path $aliasRoot 'NativeSource'
$buildDirectory = Join-Path $env:USERPROFILE 'SteeringTrainerNativeBuild'
New-Item -ItemType Directory -Force -Path $aliasRoot | Out-Null

if (Test-Path -LiteralPath $sourceAlias) {
    $aliasItem = Get-Item -LiteralPath $sourceAlias -Force
    if (-not ($aliasItem.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Native source alias path exists and is not a junction: $sourceAlias"
    }
    $target = (Resolve-Path -LiteralPath $sourceAlias).Path
    if ($target -ne (Resolve-Path -LiteralPath $sourceDirectory).Path) {
        [IO.Directory]::Delete($sourceAlias, $false)
        New-Item -ItemType Junction -Path $sourceAlias -Target $sourceDirectory | Out-Null
    }
}
else {
    New-Item -ItemType Junction -Path $sourceAlias -Target $sourceDirectory | Out-Null
}

$cmakeCommand = Get-Command cmake.exe -ErrorAction SilentlyContinue
if ($cmakeCommand) {
    $cmake = $cmakeCommand.Source
}
else {
    $cmake = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\18\BuildTools\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
}
if (-not (Test-Path -LiteralPath $cmake)) { throw 'CMake was not found. Install Visual Studio Build Tools with the C++ and CMake components.' }

$flex = Join-Path $sourceAlias 'build-tools\win_flex.exe'
$bison = Join-Path $sourceAlias 'build-tools\win_bison.exe'
$flexHeaders = Join-Path $sourceAlias 'build-tools'
if (-not (Test-Path -LiteralPath $flex) -or -not (Test-Path -LiteralPath $bison) -or -not (Test-Path -LiteralPath (Join-Path $flexHeaders 'FlexLexer.h'))) {
    throw 'The pinned WinFlexBison build tools are missing from native/build-tools.'
}

$configureArguments = @(
    '-S', $sourceAlias,
    '-B', $buildDirectory,
    '-G', 'Visual Studio 18 2026',
    '-A', 'x64',
    '-DBUILD_TESTING=OFF',
    "-DFLEX_EXECUTABLE=$flex",
    "-DBISON_EXECUTABLE=$bison",
    "-DFLEX_INCLUDE_DIR=$flexHeaders"
)
Push-Location $env:USERPROFILE
& $cmake @configureArguments
if ($LASTEXITCODE -ne 0) { throw "CMake configuration failed with exit code $LASTEXITCODE." }

$buildArguments = @('--build', $buildDirectory, '--config', 'Release', '--target', 'steering_engine_bridge', '--parallel', '6')
& $cmake @buildArguments
if ($LASTEXITCODE -ne 0) { throw "Open Engine Simulator build failed with exit code $LASTEXITCODE." }

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$nativeDll = Join-Path $buildDirectory 'Release\SteeringTrainer.EngineSim.dll'
Copy-Item -LiteralPath $nativeDll -Destination (Join-Path $OutputDirectory 'SteeringTrainer.EngineSim.dll') -Force
Pop-Location
