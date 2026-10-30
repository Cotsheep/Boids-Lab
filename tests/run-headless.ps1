param(
    [Parameter(Mandatory = $true)][string]$GodotExecutable,
    [string[]]$Scenes = @()
)

$ErrorActionPreference = 'Stop'
$projectDirectory = Split-Path -Parent $PSScriptRoot
$enginePath = (Resolve-Path -LiteralPath $GodotExecutable).Path
$previousDirectory = Get-Location
$previousResolver = $env:MSBuildEnableWorkloadResolver
$previousNotification = $env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE
$preferenceDirectory = Join-Path $env:APPDATA 'Godot/app_userdata/BoidsLab'
$preferences = @{}
foreach ($name in @('boids_lab.cfg', 'boids_lab_3d.cfg')) {
    $path = Join-Path $preferenceDirectory $name
    $preferences[$path] = if (Test-Path -LiteralPath $path) { [System.IO.File]::ReadAllBytes($path) } else { $null }
}

try {
    Set-Location -LiteralPath $projectDirectory
    # Process-local workaround for the existing machine workload resolver issue.
    $env:MSBuildEnableWorkloadResolver = 'false'
    $env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
    dotnet build BoidsLab.sln --no-restore --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw 'C# build failed.' }
    $logDirectory = Join-Path $projectDirectory '.godot/validation/full-regression'
    [System.IO.Directory]::CreateDirectory($logDirectory) | Out-Null
    if ($Scenes.Count -eq 0) {
        $Scenes = @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*_checks.tscn' | Sort-Object Name | ForEach-Object BaseName)
    }
    $failures = @()
    foreach ($scene in $Scenes) {
        if ($scene -notmatch '^[a-z0-9_]+_checks$') { throw "Invalid check scene: $scene" }
        $output = & $enginePath --headless --path . "res://tests/$scene.tscn" --quit-after 600 2>&1
        $code = $LASTEXITCODE
        $output | Set-Content -LiteralPath (Join-Path $logDirectory "$scene.log") -Encoding UTF8
        $joined = $output -join "`n"
        if ($code -ne 0 -or $joined -match '(?m)^ERROR:' -or $joined -notmatch 'PASS:') {
            $failures += $scene
            Write-Output "FAIL: $scene (exit $code)"
            Write-Output $joined
        } else {
            Write-Output "PASS: $scene"
        }
    }
    if ($failures.Count -gt 0) { throw "Failed checks: $($failures -join ', ')" }
    Write-Output "PASS: all $($Scenes.Count) check scenes. Logs: $logDirectory"
}
finally {
    # Older 2D integration checks use the real default path; restore exact prior contents.
    foreach ($path in $preferences.Keys) {
        if ($null -ne $preferences[$path]) {
            [System.IO.Directory]::CreateDirectory($preferenceDirectory) | Out-Null
            [System.IO.File]::WriteAllBytes($path, [byte[]]$preferences[$path])
        } elseif (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path
        }
    }
    $env:MSBuildEnableWorkloadResolver = $previousResolver
    $env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = $previousNotification
    Set-Location -LiteralPath $previousDirectory.Path
}
