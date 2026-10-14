<#
.SYNOPSIS
    Pipeline CLI RugWash Simulator : compilation, initialisation, génération de scène et tests EditMode, sans ouvrir l'éditeur.

.EXAMPLE
    ./Tools/RugWash-CI.ps1 -Step all
    ./Tools/RugWash-CI.ps1 -Step test -UnityPath "C:\Program Files\Unity\Hub\Editor\6000.0.58f1\Editor\Unity.exe"

.NOTES
    Résolution de Unity.exe : -UnityPath > $env:UNITY_EXE > version de ProjectSettings/ProjectVersion.txt dans Unity Hub > éditeur Hub le plus récent.
    Logs et résultats : Logs/ci/
    Commandes Unity équivalentes :
      Compilation : Unity.exe -batchmode -nographics -quit -projectPath . -executeMethod RugWash.Editor.RugWashCI.CompileCheck -logFile Logs/ci/compile.log
      Tests       : Unity.exe -batchmode -nographics -projectPath . -runTests -testPlatform EditMode -testResults Logs/ci/editmode-results.xml -logFile Logs/ci/tests.log
      PlayMode    : Unity.exe -batchmode -projectPath . -runTests -testPlatform PlayMode -testResults Logs/ci/playmode-results.xml -logFile Logs/ci/playmode.log
#>
param(
    [ValidateSet('compile', 'bootstrap', 'scene', 'test', 'playmode', 'build', 'benchmark', 'multiplayer', 'all')]
    [string]$Step = 'all',
    [string]$UnityPath
)

$ErrorActionPreference = 'Stop'
$ProjectPath = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$LogDir = Join-Path $ProjectPath 'Logs\ci'
New-Item -ItemType Directory -Force $LogDir | Out-Null

function Get-HubEditorRoots {
    $roots = @('C:\Program Files\Unity\Hub\Editor')
    $secondary = Join-Path $env:APPDATA 'UnityHub\secondaryInstallPath.json'
    if (Test-Path $secondary) {
        $path = (Get-Content $secondary -Raw | ConvertFrom-Json)
        if ($path) { $roots += $path }
    }
    $roots | Where-Object { $_ -and (Test-Path $_) }
}

function Resolve-UnityExe {
    if ($UnityPath) { return $UnityPath }
    if ($env:UNITY_EXE) { return $env:UNITY_EXE }

    $versionFile = Join-Path $ProjectPath 'ProjectSettings\ProjectVersion.txt'
    $roots = @(Get-HubEditorRoots)
    if (Test-Path $versionFile) {
        $version = ((Get-Content $versionFile | Select-String 'm_EditorVersion:') -split ':\s*')[1].Trim()
        foreach ($root in $roots) {
            $candidate = Join-Path $root "$version\Editor\Unity.exe"
            if (Test-Path $candidate) { return $candidate }
        }
        Write-Warning "Unity $version (ProjectVersion.txt) introuvable, recherche d'un autre éditeur."
    }
    foreach ($root in $roots) {
        $latest = Get-ChildItem $root -Directory | Sort-Object Name -Descending |
            Where-Object { Test-Path (Join-Path $_.FullName 'Editor\Unity.exe') } | Select-Object -First 1
        if ($latest) { return (Join-Path $latest.FullName 'Editor\Unity.exe') }
    }
    throw "Unity.exe introuvable. Installez Unity 6 via Unity Hub ou passez -UnityPath / `$env:UNITY_EXE."
}

function Invoke-Unity([string]$Name, [string[]]$UnityArgs, [switch]$Graphics) {
    $log = Join-Path $LogDir "$Name.log"
    $allArgs = @('-batchmode')
    if (-not $Graphics) { $allArgs += '-nographics' }
    $allArgs += @('-projectPath', "`"$ProjectPath`"", '-logFile', "`"$log`"") + $UnityArgs
    Write-Host "==> [$Name] $Unity $($allArgs -join ' ')"
    $process = Start-Process -FilePath $Unity -ArgumentList $allArgs -PassThru -NoNewWindow
    $null = $process.Handle  # PowerShell 5.1 : sans cela, ExitCode reste vide.
    # WaitForExit : -Wait attendrait aussi les processus enfants de Unity (workers) qui peuvent survivre.
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) {
        Get-Content $log -Tail 40 -ErrorAction SilentlyContinue
        throw "[$Name] échec (code $($process.ExitCode)). Log : $log"
    }
    Write-Host "==> [$Name] OK"
}

$Unity = Resolve-UnityExe

if ($Step -in 'compile', 'all') {
    Invoke-Unity 'compile' @('-quit', '-executeMethod', 'RugWash.Editor.RugWashCI.CompileCheck')
}
if ($Step -in 'bootstrap', 'all') {
    Invoke-Unity 'bootstrap' @('-quit', '-executeMethod', 'RugWash.Editor.RugWashCI.Bootstrap')
}
if ($Step -in 'scene', 'all') {
    # Processus séparé : le changement de backend d'entrée nécessite un redémarrage de l'éditeur.
    Invoke-Unity 'scene' @('-quit', '-executeMethod', 'RugWash.Editor.RugWashCI.BuildAllScenes')
}
if ($Step -in 'test', 'all') {
    $results = Join-Path $LogDir 'editmode-results.xml'
    # -runTests quitte de lui-même : ne pas ajouter -quit. Code de sortie 2 = tests en échec.
    Invoke-Unity 'tests' @('-runTests', '-testPlatform', 'EditMode', '-testResults', "`"$results`"")
    Write-Host "Résultats : $results"
}
if ($Step -in 'playmode', 'all') {
    $results = Join-Path $LogDir 'playmode-results.xml'
    # Avec GPU : la catégorie « Capture » écrit des captures d'écran dans Logs/ci/screens.
    Invoke-Unity 'playmode' @('-runTests', '-testPlatform', 'PlayMode', '-testResults', "`"$results`"") -Graphics
    Write-Host "Résultats : $results"
}
if ($Step -eq 'build') {
    Invoke-Unity 'build' @('-quit', '-executeMethod', 'RugWash.Editor.RugWashBuild.BuildWindows')
    Write-Host "Build : $(Join-Path $ProjectPath 'Builds\Windows\RugWashSimulator.exe')"
}
if ($Step -eq 'multiplayer') {
    # Deux processus du jeu compilé (hôte puis invité) en connexion directe locale, scénario automatique, rapports comparés.
    $exe = Join-Path $ProjectPath 'Builds\Windows\RugWashSimulator.exe'
    if (-not (Test-Path $exe)) { throw "Build absent : lancez d'abord -Step build." }
    $port = '7795'
    $roles = @('host', 'guest')
    $processes = @{}
    foreach ($role in $roles) {
        $report = Join-Path $LogDir "mp-$role.json"
        if (Test-Path $report) { Remove-Item $report }
        $playerLog = Join-Path $LogDir "mp-$role.log"
        $mpArgs = @('-rugwash-mp-test', $role, '-rugwash-mp-port', $port, '-rugwash-mp-report', "`"$report`"",
            '-screen-width', '960', '-screen-height', '540', '-screen-fullscreen', '0', '-logFile', "`"$playerLog`"")
        Write-Host "==> [multiplayer] $role : $exe $($mpArgs -join ' ')"
        $processes[$role] = Start-Process -FilePath $exe -ArgumentList $mpArgs -PassThru
        $null = $processes[$role].Handle
        Start-Sleep -Seconds 3
    }
    $failed = $false
    foreach ($role in $roles) {
        if (-not $processes[$role].WaitForExit(420000)) { $processes[$role].Kill(); Write-Host "[multiplayer] $role : délai dépassé (7 min)."; $failed = $true }
    }
    $reports = @{}
    foreach ($role in $roles) {
        $report = Join-Path $LogDir "mp-$role.json"
        if (-not (Test-Path $report)) { Write-Host "[multiplayer] $role : aucun rapport."; $failed = $true; continue }
        $reports[$role] = Get-Content $report -Raw -Encoding UTF8 | ConvertFrom-Json
        Write-Host "--- $role"
        foreach ($check in $reports[$role].checks) {
            $mark = if ($check.passed) { 'OK ' } else { 'ÉCHEC'; $failed = $true }
            Write-Host ("  [{0}] {1} {2}" -f $mark, $check.name, $check.detail)
        }
        if ($reports[$role].fatal) { Write-Host "  Arrêt : $($reports[$role].fatal)"; $failed = $true }
        $reports[$role].values.PSObject.Properties | ForEach-Object { Write-Host ("  {0} = {1}" -f $_.Name, $_.Value) }
    }
    if ($reports.ContainsKey('host') -and $reports.ContainsKey('guest')) {
        $same = $reports['host'].values.empreinteTapis -eq $reports['guest'].values.empreinteTapis
        Write-Host ("  [{0}] Tapis identique chez l'hôte et l'invité (empreinte {1} / {2})" -f ($(if ($same) { 'OK ' } else { 'ÉCHEC' })), $reports['host'].values.empreinteTapis, $reports['guest'].values.empreinteTapis)
        if (-not $same) { $failed = $true }
        # Progression personnelle : la carrière de l'invité tenue par l'hôte est exactement celle que l'invité affiche.
        $career = $reports['host'].values.carriereInviteVueParHote -eq $reports['guest'].values.argentInvite
        Write-Host ("  [{0}] Carrière de l'invité identique chez l'hôte et chez l'invité ({1} / {2})" -f ($(if ($career) { 'OK ' } else { 'ÉCHEC' })), $reports['host'].values.carriereInviteVueParHote, $reports['guest'].values.argentInvite)
        if (-not $career) { $failed = $true }
        # Missions générées : l'invité régénère exactement les offres du tableau de l'hôte.
        $missions = $reports['host'].values.missionsTableau -and ($reports['host'].values.missionsTableau -eq $reports['guest'].values.missionsTableau)
        Write-Host ("  [{0}] Missions du tableau identiques chez l'hôte et l'invité ({1} / {2})" -f ($(if ($missions) { 'OK ' } else { 'ÉCHEC' })), $reports['host'].values.missionsTableau, $reports['guest'].values.missionsTableau)
        if (-not $missions) { $failed = $true }
    }
    if ($failed) { throw "[multiplayer] des vérifications ont échoué." }
    Write-Host "==> [multiplayer] OK"
}
if ($Step -eq 'benchmark') {
    # Lance le jeu compilé (fenêtre visible) en mode vérification : parcours automatique + mesure en 1080p.
    $exe = Join-Path $ProjectPath 'Builds\Windows\RugWashSimulator.exe'
    if (-not (Test-Path $exe)) { throw "Build absent : lancez d'abord -Step build." }
    $report = Join-Path $LogDir 'benchmark.json'
    $playerLog = Join-Path $LogDir 'player.log'
    if (Test-Path $report) { Remove-Item $report }
    $benchArgs = @('-rugwash-benchmark', "`"$report`"", '-screen-width', '1920', '-screen-height', '1080', '-screen-fullscreen', '1', '-logFile', "`"$playerLog`"")
    Write-Host "==> [benchmark] $exe $($benchArgs -join ' ')"
    $process = Start-Process -FilePath $exe -ArgumentList $benchArgs -PassThru
    $null = $process.Handle
    if (-not $process.WaitForExit(600000)) { $process.Kill(); throw "[benchmark] délai dépassé (10 min)." }
    if (-not (Test-Path $report)) { throw "[benchmark] aucun rapport produit (code $($process.ExitCode)). Log : $playerLog" }
    Get-Content $report
    if ($process.ExitCode -ne 0) { throw "[benchmark] des vérifications ont échoué (code $($process.ExitCode))." }
    Write-Host "==> [benchmark] OK"
}
