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
    [string]$UnityPath,
    # Protocole de mesure (benchmark) : voir Documentation/VALIDATION.md, « Protocole de benchmark ».
    [ValidateSet('full', 'foam', 'graphics', 'quick')]
    [string]$BenchmarkMode = 'full',
    # Niveau de qualité imposé (-1 : celui du jeu) et échelle de rendu imposée (0 : celle du niveau).
    [int]$Quality = -1,
    [double]$RenderScale = 0,
    # Nom du rapport (Logs/ci/<nom>.json).
    [string]$ReportName = 'benchmark',
    # Masque d'affinité CPU du jeu mesuré (0 : tous les cœurs) — limitation de ressources pour approcher un PC moins puissant.
    [long]$CpuAffinity = 0,
    # Build Development (Builds/WindowsProfiling) et captures du Profiler Unity par scénario, analysées dans l'éditeur.
    [switch]$Development,
    # Mesure lancée même si le GPU est déjà occupé (résultats alors faussés).
    [switch]$Force
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
if ($Step -eq 'build' -and $Development) {
    Invoke-Unity 'build-profiling' @('-quit', '-executeMethod', 'RugWash.Editor.RugWashBuild.BuildWindowsProfiling')
}
elseif ($Step -eq 'build') {
    Invoke-Unity 'build' @('-quit', '-executeMethod', 'RugWash.Editor.RugWashBuild.BuildWindows')
    Write-Host "Build : $(Join-Path $ProjectPath 'Builds\Windows\RugWashSimulator.exe')"
}
if ($Step -eq 'multiplayer') {
    # Processus du jeu compilé en connexion directe locale : hôte, invité, puis le même invité relancé après son crash
    # (même identité de joueur, même dossier de sauvegarde). Scénario automatique, rapports comparés, sauvegardes relues.
    $exe = Join-Path $ProjectPath 'Builds\Windows\RugWashSimulator.exe'
    if (-not (Test-Path $exe)) { throw "Build absent : lancez d'abord -Step build." }
    $port = '7795'
    $roles = @('host', 'guest', 'guest2')
    $processes = @{}
    function Start-Role([string]$role) {
        $report = Join-Path $LogDir "mp-$role.json"
        if (Test-Path $report) { Remove-Item $report }
        $playerLog = Join-Path $LogDir "mp-$role.log"
        $mpArgs = @('-rugwash-mp-test', $role, '-rugwash-mp-port', $port, '-rugwash-mp-report', "`"$report`"",
            '-screen-width', '960', '-screen-height', '540', '-screen-fullscreen', '0', '-logFile', "`"$playerLog`"")
        if ($role -ne 'host') { $mpArgs += @('-rugwash-player-key', 'mp-guest') }
        Write-Host "==> [multiplayer] $role : $exe $($mpArgs -join ' ')"
        $process = Start-Process -FilePath $exe -ArgumentList $mpArgs -PassThru
        $null = $process.Handle
        return $process
    }
    $failed = $false
    $processes['host'] = Start-Role 'host'
    Start-Sleep -Seconds 3
    $processes['guest'] = Start-Role 'guest'
    # L'invité « crashe » (processus tué) : le même joueur revient aussitôt.
    if (-not $processes['guest'].WaitForExit(420000)) { $processes['guest'].Kill(); Write-Host "[multiplayer] guest : délai dépassé (7 min)."; $failed = $true }
    $processes['guest2'] = Start-Role 'guest2'
    foreach ($role in @('host', 'guest2')) {
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
    function Compare-Values([string]$label, $a, $b) {
        $same = $a -and ($a -eq $b)
        Write-Host ("  [{0}] {1} ({2} / {3})" -f ($(if ($same) { 'OK ' } else { 'ÉCHEC' })), $label, $a, $b)
        return $same
    }
    if ($reports.ContainsKey('host') -and $reports.ContainsKey('guest')) {
        if (-not (Compare-Values "Tapis identique chez l'hôte et l'invité" $reports['host'].values.empreinteTapis $reports['guest'].values.empreinteTapis)) { $failed = $true }
        if (-not (Compare-Values "Carrière de l'invité : la même chez l'hôte (qui la valide) et chez l'invité, après son achat" $reports['host'].values.argentInviteTenuParHote $reports['guest'].values.argentApresAchatVuParInvite)) { $failed = $true }
        if (-not (Compare-Values "Missions du tableau identiques chez l'hôte et l'invité" $reports['host'].values.missionsTableau $reports['guest'].values.missionsTableau)) { $failed = $true }
    }
    if ($reports.ContainsKey('host') -and $reports.ContainsKey('guest2')) {
        if (-not (Compare-Values "Invité revenu : sa progression, la même chez l'hôte et chez lui" $reports['host'].values.argentInviteApresReconnexion $reports['guest2'].values.argentVuApresReconnexion)) { $failed = $true }
    }
    # Sauvegarde de l'hôte relue après sa fermeture brutale : lisible et conforme à la fin de la session.
    if ($reports.ContainsKey('host') -and $reports['host'].values.fichierSauvegardeHote) {
        $savePath = $reports['host'].values.fichierSauvegardeHote
        $ok = $false
        $detail = 'absente'
        if (Test-Path $savePath) {
            try {
                $save = Get-Content $savePath -Raw -Encoding UTF8 | ConvertFrom-Json
                $ok = "$($save.money)" -eq "$($reports['host'].values.argentFinalHote)"
                $detail = "$($save.money) € pour $($reports['host'].values.argentFinalHote) €"
            } catch { $detail = "illisible : $_" }
        }
        Write-Host ("  [{0}] Sauvegarde de l'hôte intacte après sa fermeture brutale ({1})" -f ($(if ($ok) { 'OK ' } else { 'ÉCHEC' })), $detail)
        if (-not $ok) { $failed = $true }
    }
    if ($failed) { throw "[multiplayer] des vérifications ont échoué." }
    Write-Host "==> [multiplayer] OK"
}
if ($Step -eq 'benchmark') {
    # Lance le jeu compilé (fenêtre visible) en mode vérification : parcours automatique + mesure en 1080p.
    $buildDir = if ($Development) { 'Builds\WindowsProfiling' } else { 'Builds\Windows' }
    $exe = Join-Path $ProjectPath "$buildDir\RugWashSimulator.exe"
    if (-not (Test-Path $exe)) { throw "Build absent ($buildDir) : lancez d'abord -Step build$(if ($Development) { ' -Development' })." }
    # Garde : un GPU déjà occupé (jeu, vidéo, autre mesure) fausse les résultats et gênerait l'utilisateur.
    $smi = Get-Command nvidia-smi -ErrorAction SilentlyContinue
    if ($smi -and -not $Force) {
        $samples = 1..3 | ForEach-Object { [int](& $smi.Source --query-gpu=utilization.gpu --format=csv,noheader,nounits | Select-Object -First 1); Start-Sleep -Milliseconds 700 }
        $busy = ($samples | Measure-Object -Average).Average
        if ($busy -gt 35) { throw "[benchmark] GPU déjà occupé à $([math]::Round($busy)) % : mesure annulée (relancer plus tard, ou -Force)." }
    }
    $report = Join-Path $LogDir "$ReportName.json"
    $playerLog = Join-Path $LogDir "$ReportName-player.log"
    if (Test-Path $report) { Remove-Item $report }
    $benchArgs = @('-rugwash-benchmark', "`"$report`"", '-screen-width', '1920', '-screen-height', '1080', '-screen-fullscreen', '1', '-logFile', "`"$playerLog`"",
        '-rugwash-benchmark-mode', $BenchmarkMode)
    if ($Quality -ge 0) { $benchArgs += @('-rugwash-benchmark-quality', "$Quality") }
    if ($RenderScale -gt 0) { $benchArgs += @('-rugwash-benchmark-renderscale', $RenderScale.ToString([Globalization.CultureInfo]::InvariantCulture)) }
    $profileDir = Join-Path $LogDir "profile-$ReportName"
    if ($Development) {
        if (Test-Path $profileDir) { Remove-Item $profileDir -Recurse -Force }
        $benchArgs += @('-rugwash-benchmark-profile', "`"$profileDir`"")
    }
    Write-Host "==> [benchmark] $exe $($benchArgs -join ' ')"
    $process = Start-Process -FilePath $exe -ArgumentList $benchArgs -PassThru
    $null = $process.Handle
    if ($CpuAffinity -gt 0) { $process.ProcessorAffinity = [IntPtr]$CpuAffinity; Write-Host "    affinité CPU : 0x$('{0:X}' -f $CpuAffinity)" }
    if (-not $process.WaitForExit(900000)) { $process.Kill(); throw "[benchmark] délai dépassé (15 min)." }
    if (-not (Test-Path $report)) { throw "[benchmark] aucun rapport produit (code $($process.ExitCode)). Log : $playerLog" }
    Get-Content $report
    if ($Development -and (Test-Path $profileDir)) {
        # Analyse des captures du Profiler Unity dans l'éditeur (marqueurs les plus coûteux par scénario).
        $analysis = Join-Path $LogDir "profile-$ReportName.txt"
        Invoke-Unity 'profile-analysis' @('-quit', '-executeMethod', 'RugWash.Editor.RugWashProfileAnalysis.Analyze',
            '-rugwash-profile-dir', "`"$profileDir`"", '-rugwash-profile-report', "`"$analysis`"")
        Write-Host "Analyse du profileur : $analysis"
    }
    if ($process.ExitCode -ne 0) { throw "[benchmark] des vérifications ont échoué (code $($process.ExitCode))." }
    Write-Host "==> [benchmark] OK"
}
