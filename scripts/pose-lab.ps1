[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$GameRoot,
    [ValidateSet('Build','Install','Restore')][string]$Action = 'Build'
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$gamePath = (Resolve-Path -LiteralPath $GameRoot).ProviderPath
if (-not (Test-Path -LiteralPath (Join-Path $gamePath 'BepInEx\interop\Assembly-CSharp.dll'))) { throw 'A BepInEx IL2CPP game installation is required.' }
$pluginPath = Join-Path $gamePath 'BepInEx\plugins\SecretFlasherManakaPoseLab.dll'
$vrPlugin = Join-Path $gamePath 'BepInEx\plugins\SecretFlasherManakaVR.dll'
$vrConfig = Join-Path $gamePath 'BepInEx\config\com.codex.secretflashermanaka.vr.cfg'
$backupDir = Join-Path $gamePath 'BepInEx\config\ManakaPoseLab\backup'
$backupConfig = Join-Path $backupDir 'vr.cfg'
$sessionManifest = Join-Path $backupDir 'session.json'
if ($Action -ne 'Build' -and (Get-Process SecretFlasherManaka -ErrorAction SilentlyContinue)) { throw 'Close the game before installing or restoring Pose Lab.' }
if ($Action -eq 'Restore') {
    if (-not (Test-Path -LiteralPath $sessionManifest)) { throw 'No Pose Lab installation backup found.' }
    $manifest = Get-Content -LiteralPath $sessionManifest -Raw | ConvertFrom-Json
    if ($manifest.HadVrConfig) { Copy-Item -LiteralPath $backupConfig -Destination $vrConfig -Force }
    if (Test-Path -LiteralPath (Join-Path $backupDir 'SecretFlasherManakaVR.dll')) { Copy-Item -LiteralPath (Join-Path $backupDir 'SecretFlasherManakaVR.dll') -Destination $vrPlugin -Force }
    if ($manifest.HadPlugin) { Copy-Item -LiteralPath (Join-Path $backupDir 'plugin.dll') -Destination $pluginPath -Force }
    elseif (Test-Path -LiteralPath $pluginPath) { Remove-Item -LiteralPath $pluginPath }
    Remove-Item -LiteralPath $sessionManifest
    Write-Host 'Restored previous VR configuration and plugin state. Pose input and diagnostics are retained.'
    return
}
$project = Join-Path $repoRoot 'src\SecretFlasherManakaPoseLab\SecretFlasherManakaPoseLab.csproj'
& dotnet build $project -c Release "-p:GameRoot=$gamePath" --nologo
if ($LASTEXITCODE -ne 0) { throw 'Pose Lab build failed.' }
$built = Join-Path $repoRoot 'src\SecretFlasherManakaPoseLab\bin\Release\net6.0\SecretFlasherManakaPoseLab.dll'
if ($Action -eq 'Build') { Write-Host $built; return }
New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
if (-not (Test-Path -LiteralPath $sessionManifest)) {
    $hadConfig = Test-Path -LiteralPath $vrConfig
    $hadPlugin = Test-Path -LiteralPath $pluginPath
    if ($hadConfig) { Copy-Item -LiteralPath $vrConfig -Destination $backupConfig -Force }
    if ($hadPlugin) { Copy-Item -LiteralPath $pluginPath -Destination (Join-Path $backupDir 'plugin.dll') -Force }
    @{ HadVrConfig = $hadConfig; HadPlugin = $hadPlugin } | ConvertTo-Json | Set-Content -LiteralPath $sessionManifest
}
if (Test-Path -LiteralPath $vrConfig) {
    $content = Get-Content -LiteralPath $vrConfig -Raw
    $content = $content -replace '(?m)^EnableVR\s*=.*$', 'EnableVR = false'
    $content = $content -replace '(?m)^AutoStartSteamVR\s*=.*$', 'AutoStartSteamVR = false'
    Set-Content -LiteralPath $vrConfig -Value $content -NoNewline
}
Copy-Item -LiteralPath $built -Destination $pluginPath -Force
if (Test-Path -LiteralPath $vrPlugin) {
    Move-Item -LiteralPath $vrPlugin -Destination (Join-Path $backupDir 'SecretFlasherManakaVR.dll') -Force
}
Write-Host "Installed desktop Pose Lab: $pluginPath"
Write-Host 'VR startup disabled for the test. Use -Action Restore after closing the game to undo installation.'
