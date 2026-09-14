[CmdletBinding()]
param(
    [string]$GameRoot,
    [ValidateSet('Build','Launch','Install')][string]$Action = 'Launch',
    [switch]$NoBrowser
)
$ErrorActionPreference = 'Stop'
$repoPath = Split-Path -Parent $PSScriptRoot
$outputPath = Join-Path $repoPath 'artifacts\config-ui\publish'
$localSettingsPath = Join-Path $repoPath 'artifacts\config-ui\launcher.json'
if (-not $GameRoot -and (Test-Path -LiteralPath $localSettingsPath)) {
    $GameRoot = (Get-Content -LiteralPath $localSettingsPath -Raw -Encoding UTF8 | ConvertFrom-Json).GameRoot
}
if (-not $GameRoot) { throw 'Set the game directory once: scripts\config-ui.ps1 -GameRoot "<game directory>" -Action Launch' }
$gamePath = (Resolve-Path -LiteralPath $GameRoot).ProviderPath
if (-not (Test-Path -LiteralPath (Join-Path $gamePath 'SecretFlasherManaka.exe'))) { throw 'Game executable not found.' }
New-Item -ItemType Directory -Path (Split-Path -Parent $localSettingsPath) -Force | Out-Null
@{ GameRoot = $gamePath } | ConvertTo-Json | Set-Content -LiteralPath $localSettingsPath -Encoding UTF8
$project = Join-Path $repoPath 'src\ManakaVR.Configurator\ManakaVR.Configurator.csproj'
$executable = Join-Path $outputPath 'ManakaVR.Configurator.exe'
if ($Action -ne 'Launch' -or -not (Test-Path -LiteralPath $executable)) {
    & dotnet publish $project -c Release -r win-x64 --self-contained false -o $outputPath --nologo -clp:ErrorsOnly
    if ($LASTEXITCODE -ne 0) { throw 'Configuration tool build failed.' }
}
if ($Action -eq 'Build') { Write-Host $outputPath; return }
if ($Action -eq 'Launch') {
    $launchArguments = '--game-root "' + $gamePath + '"'
    if ($NoBrowser) { $launchArguments += ' --no-browser' }
    Start-Process -FilePath $executable -ArgumentList $launchArguments -WorkingDirectory $repoPath -WindowStyle Hidden
    return
}
$installPath = Join-Path $gamePath 'VR配置工具'
if (Get-Process 'ManakaVR.Configurator' -ErrorAction SilentlyContinue) { throw 'Close the configuration tool before installing an update.' }
New-Item -ItemType Directory -Path $installPath -Force | Out-Null
Copy-Item -Path (Join-Path $outputPath '*') -Destination $installPath -Recurse -Force
# A normal Windows shortcut opens the GUI directly; no console or file chooser.
$shortcutPath = Join-Path $gamePath 'Manaka VR 配置.lnk'
$taskShell = New-Object -ComObject WScript.Shell
$shortcut = $taskShell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = Join-Path $installPath 'ManakaVR.Configurator.exe'
$shortcut.WorkingDirectory = $installPath
$shortcut.Description = 'Manaka VR 可视化配置；自动读取本游戏配置'
$shortcut.Save()
Write-Host "Installed: $shortcutPath"
