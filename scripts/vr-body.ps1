[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$GameRoot,
    [ValidateSet('Build','Install','Restore')][string]$Action = 'Build',
    [ValidateSet('Legacy','ThreePoint','SixPoint','EightPoint','TenPoint','ElevenPoint')][string]$Mode
)
$ErrorActionPreference = 'Stop'
$repoPath = Split-Path -Parent $PSScriptRoot
$gamePath = (Resolve-Path -LiteralPath $GameRoot).ProviderPath
if (-not (Test-Path -LiteralPath (Join-Path $gamePath 'BepInEx\interop\Assembly-CSharp.dll'))) { throw 'BepInEx IL2CPP game installation required.' }
if ($Action -ne 'Build' -and (Get-Process SecretFlasherManaka -ErrorAction SilentlyContinue)) { throw 'Close the game before installation or restoration.' }
$backupPath = Join-Path $gamePath 'BepInEx\config\ManakaVRBody\backup'
$manifestPath = Join-Path $backupPath 'session.json'
$files = @('BepInEx\plugins\SecretFlasherManakaVR.dll', 'BepInEx\plugins\SecretFlasherManakaPoseLab.dll', 'BepInEx\config\com.codex.secretflashermanaka.vr.cfg', 'BepInEx\plugins\SecretFlasherManakaVR_Input\actions.json', 'BepInEx\plugins\SecretFlasherManakaVR_Input\bindings_oculus_touch.json')
if ($Action -eq 'Restore') {
    if (-not (Test-Path -LiteralPath $manifestPath)) { throw 'No VR body installation backup found.' }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    foreach ($entry in $manifest.Files) {
        # Relative targets are taken from this script, never arbitrary paths in a backup file.
        if ($entry.Path -notin $files) { throw 'Unexpected backup target.' }
        $target = Join-Path $gamePath $entry.Path
        if ($entry.Existed) { Copy-Item -LiteralPath (Join-Path $backupPath $entry.Backup) -Destination $target -Force }
        elseif (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target }
    }
    Remove-Item -LiteralPath $manifestPath
    Write-Host 'Restored the plugin/configuration state before VR body installation.'
    return
}
$project = Join-Path $repoPath 'src\SecretFlasherManakaVR\SecretFlasherManakaVR.csproj'
& dotnet build $project -c Release "-p:GameRoot=$gamePath" --nologo -clp:ErrorsOnly
if ($LASTEXITCODE -ne 0) { throw 'VR body build failed.' }
$built = Join-Path $repoPath 'src\SecretFlasherManakaVR\bin\Release\SecretFlasherManakaVR.dll'
if ($Action -eq 'Build') { Write-Host $built; return }
New-Item -ItemType Directory -Path $backupPath -Force | Out-Null
if (-not (Test-Path -LiteralPath $manifestPath)) {
    $records = @()
    for ($i=0; $i -lt $files.Count; $i++) {
        $target = Join-Path $gamePath $files[$i]
        $exists = Test-Path -LiteralPath $target
        $backupName = "file-$i.bak"
        if ($exists) { Copy-Item -LiteralPath $target -Destination (Join-Path $backupPath $backupName) -Force }
        $records += @{ Path = $files[$i]; Existed = $exists; Backup = $backupName }
    }
    @{ Files = $records } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath
}
$configPath = Join-Path $gamePath $files[2]
$desktopOriginal = Join-Path $gamePath 'BepInEx\config\ManakaPoseLab\backup\vr.cfg'
# Reuse the user's pre-desktop VR settings on first transition; keep future VR tuning on reinstalls.
if (-not (Test-Path -LiteralPath (Join-Path $gamePath $files[0])) -and (Test-Path -LiteralPath $desktopOriginal)) {
    Copy-Item -LiteralPath $desktopOriginal -Destination $configPath -Force
}
if (-not (Test-Path -LiteralPath $configPath)) { Copy-Item -LiteralPath (Join-Path $repoPath 'config\com.codex.secretflashermanaka.vr.cfg') -Destination $configPath }
$content = Get-Content -LiteralPath $configPath -Raw
foreach ($key in @('EnableVR','AutoStartSteamVR')) {
    if ($content -notmatch "(?m)^$key\s*=") { throw "Missing required configuration key $key" }
    $content = $content -replace "(?m)^$key\s*=.*$", "$key = true"
}
# Preserve an explicit profile on reinstall. Migrate the former opt-in body installer to ThreePoint.
$existingMode = [regex]::Match($content, '(?m)^TrackingMode\s*=\s*(\w+)')
$selectedMode = if ($Mode) { $Mode } elseif ($existingMode.Success) { $existingMode.Groups[1].Value } else { 'ThreePoint' }
if ($existingMode.Success) { $content = $content -replace '(?m)^TrackingMode\s*=.*$', "TrackingMode = $selectedMode" }
else { $content += "`n`n[07 Tracking Mode - 追踪模式]`nTrackingMode = $selectedMode`n" }
Set-Content -LiteralPath $configPath -Value $content -NoNewline
Copy-Item -LiteralPath $built -Destination (Join-Path $gamePath $files[0]) -Force
$desktopDll = Join-Path $gamePath $files[1]
if (Test-Path -LiteralPath $desktopDll) { Remove-Item -LiteralPath $desktopDll }
$inputPath = Join-Path $gamePath 'BepInEx\plugins\SecretFlasherManakaVR_Input'
New-Item -ItemType Directory -Path $inputPath -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repoPath 'input\actions.json') -Destination $inputPath -Force
Copy-Item -LiteralPath (Join-Path $repoPath 'input\bindings_oculus_touch.json') -Destination $inputPath -Force
Write-Host "Installed VR tracking profiles. Selected: $selectedMode. ThreePoint: F6 calibrate/toggle, F7 recalibrate. Legacy retains original controls. 6/8/10/11-point hardware input is reserved."
