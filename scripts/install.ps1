[CmdletBinding()]
param(
    [string]$GameRoot,
    [string]$ProjectDir,
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$AllowMissingOpenVR,
    [switch]$Force
)

$ErrorActionPreference = "Stop"

function Resolve-ExistingDirectory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    $resolved = Resolve-Path -LiteralPath $Path -ErrorAction SilentlyContinue
    if (-not $resolved) {
        throw "$Description not found: $Path"
    }

    return $resolved.ProviderPath
}

function Resolve-GameRoot {
    param([string]$ExplicitGameRoot)

    if ($ExplicitGameRoot) {
        $root = Resolve-ExistingDirectory -Path $ExplicitGameRoot -Description "Game root"
    }
    else {
        $scriptDir = Split-Path -Parent $PSCommandPath
        $candidate = Resolve-Path -LiteralPath (Join-Path $scriptDir "..") -ErrorAction SilentlyContinue
        if (-not $candidate -or -not (Test-Path -LiteralPath (Join-Path $candidate.ProviderPath "SecretFlasherManaka.exe"))) {
            throw "Game root could not be inferred. Run from the package root or pass -GameRoot `"<path to SecretFlasherManaka game root>`"."
        }

        $root = $candidate.ProviderPath
    }

    if (-not (Test-Path -LiteralPath (Join-Path $root "SecretFlasherManaka.exe"))) {
        throw "Game root is missing SecretFlasherManaka.exe: $root"
    }

    if (-not (Test-Path -LiteralPath (Join-Path $root "BepInEx\plugins"))) {
        throw "Game root is missing BepInEx\plugins: $root"
    }

    return $root
}

function Find-BuiltPlugin {
    param(
        [string]$ProjectRoot,
        [string]$BuildConfiguration
    )

    $binRoot = Join-Path $ProjectRoot "bin\$BuildConfiguration"
    if (-not (Test-Path -LiteralPath $binRoot -PathType Container)) {
        throw "Build output was not found: $binRoot. Run .\build.ps1 first."
    }

    $plugins = @(Get-ChildItem -LiteralPath $binRoot -Recurse -Filter "SecretFlasherManaka*.dll" -File |
        Sort-Object LastWriteTimeUtc -Descending)

    if ($plugins.Count -eq 0) {
        throw "SecretFlasherManaka plugin DLL was not found under $binRoot. Run .\build.ps1 -Configuration $BuildConfiguration first."
    }

    return $plugins[0]
}

function Copy-PluginIfChanged {
    param(
        [System.IO.FileInfo]$Plugin,
        [string]$PluginInstallDir,
        [switch]$Force
    )

    $pluginDestination = Join-Path $pluginInstallDir $Plugin.Name
    if ((Test-Path -LiteralPath $pluginDestination -PathType Leaf) -and -not $Force) {
        $sourceHash = (Get-FileHash -LiteralPath $Plugin.FullName -Algorithm SHA256).Hash
        $destHash = (Get-FileHash -LiteralPath $pluginDestination -Algorithm SHA256).Hash
        if ($sourceHash -eq $destHash) {
            Write-Host "Unchanged: $pluginDestination"
            return $pluginDestination
        }
    }

    Copy-Item -LiteralPath $Plugin.FullName -Destination $pluginDestination -Force
    Write-Host "Copied: $($Plugin.FullName)"
    Write-Host "     -> $pluginDestination"
    return $pluginDestination
}

function Find-Dependency {
    param(
        [string]$Name,
        [string[]]$SearchRoots
    )

    foreach ($root in $SearchRoots) {
        if (-not $root -or -not (Test-Path -LiteralPath $root -PathType Container)) {
            continue
        }

        $direct = Join-Path $root $Name
        if (Test-Path -LiteralPath $direct -PathType Leaf) {
            return (Get-Item -LiteralPath $direct)
        }

        $found = @(Get-ChildItem -LiteralPath $root -Recurse -Filter $Name -File -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTimeUtc -Descending)
        if ($found.Count -gt 0) {
            return $found[0]
        }
    }

    return $null
}

$scriptDir = Split-Path -Parent $PSCommandPath
$packageRoot = Resolve-Path -LiteralPath (Join-Path $scriptDir "..")
$gameRootPath = Resolve-GameRoot -ExplicitGameRoot $GameRoot
$projectRoot = if ($ProjectDir) { Resolve-ExistingDirectory -Path $ProjectDir -Description "Project directory" } else { Resolve-ExistingDirectory -Path (Join-Path $packageRoot.ProviderPath "src\SecretFlasherManakaVR") -Description "Project directory" }

$plugin = Find-BuiltPlugin -ProjectRoot $projectRoot -BuildConfiguration $Configuration
$isVrPlugin = $plugin.Name -ieq "SecretFlasherManakaVR.dll"
$pluginOutputDir = $plugin.Directory.FullName
$pluginInstallDir = Join-Path $gameRootPath "BepInEx\plugins"
$dependencyInstallDir = $pluginInstallDir
$inputSourceDir = Join-Path $packageRoot.ProviderPath "input"
$inputInstallDir = Join-Path $pluginInstallDir "SecretFlasherManakaVR_Input"
$existingLegacyPlugin = Join-Path $gameRootPath "BepInEx\plugins\SecretFlasherManakaMod.dll"

if (Test-Path -LiteralPath $existingLegacyPlugin -PathType Leaf) {
    Write-Host "Existing SecretFlasherManakaMod.dll detected and will not be touched:"
    Write-Host "  $existingLegacyPlugin"
}

New-Item -ItemType Directory -Path $pluginInstallDir -Force | Out-Null
New-Item -ItemType Directory -Path $inputInstallDir -Force | Out-Null

$dependencyNames = @(
    "openvr_api.dll",
    "Valve.VR.dll",
    "OpenVR.NET.dll",
    "OpenVR.NET.API.dll"
)

$dependencySearchRoots = @(
    $pluginOutputDir,
    $projectRoot,
    (Join-Path $packageRoot.ProviderPath "dependencies"),
    (Join-Path $packageRoot.ProviderPath "libs"),
    (Join-Path $packageRoot.ProviderPath "openvr"),
    (Join-Path $projectRoot "Dependencies"),
    (Join-Path $projectRoot "Libs"),
    (Join-Path $projectRoot "OpenVR"),
    (Join-Path $projectRoot "Native")
)

$dependencies = @()
foreach ($dependencyName in $dependencyNames) {
    $dependency = Find-Dependency -Name $dependencyName -SearchRoots $dependencySearchRoots
    if ($dependency) {
        $dependencies += $dependency
    }
}

$hasOpenVRNative = $dependencies | Where-Object { $_.Name -ieq "openvr_api.dll" } | Select-Object -First 1
if ($isVrPlugin -and -not $hasOpenVRNative -and -not $AllowMissingOpenVR) {
    throw @"
openvr_api.dll was not found in the build output or dependency folders.
Place Valve's x64 openvr_api.dll in one of these locations, then rerun install.ps1:
  $pluginOutputDir
  $(Join-Path $projectRoot "Dependencies")
  $(Join-Path $projectRoot "Libs")
Use -AllowMissingOpenVR only if the final OpenVR bridge intentionally does not require the native DLL beside the plugin.
"@
}

$staleNestedPlugin = Join-Path $pluginInstallDir "SecretFlasherManakaVR\SecretFlasherManakaVR.dll"
if ($isVrPlugin -and (Test-Path -LiteralPath $staleNestedPlugin -PathType Leaf)) {
    Remove-Item -LiteralPath $staleNestedPlugin -Force
    Write-Host "Removed stale nested plugin copy: $staleNestedPlugin"
}

$installedPlugins = @()
$installedPlugins += Copy-PluginIfChanged -Plugin $plugin -PluginInstallDir $pluginInstallDir -Force:$Force

if ($isVrPlugin) {
    foreach ($file in ($dependencies | Sort-Object FullName -Unique)) {
        $destination = Join-Path $dependencyInstallDir $file.Name
        if ((Test-Path -LiteralPath $destination -PathType Leaf) -and -not $Force) {
            $sourceHash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
            $destHash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
            if ($sourceHash -eq $destHash) {
                Write-Host "Unchanged: $destination"
                continue
            }
        }

        Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
        Write-Host "Copied: $($file.FullName)"
        Write-Host "     -> $destination"
    }

    if (Test-Path -LiteralPath $inputSourceDir -PathType Container) {
        foreach ($file in (Get-ChildItem -LiteralPath $inputSourceDir -File)) {
            $destination = Join-Path $inputInstallDir $file.Name
            if ((Test-Path -LiteralPath $destination -PathType Leaf) -and -not $Force) {
                $sourceHash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
                $destHash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
                if ($sourceHash -eq $destHash) {
                    Write-Host "Unchanged: $destination"
                    continue
                }
            }

            Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
            Write-Host "Copied: $($file.FullName)"
            Write-Host "     -> $destination"
        }
    }
}

Write-Host ""
Write-Host "Install complete:"
Write-Host "  Plugins:"
foreach ($installedPlugin in $installedPlugins) {
    Write-Host "    $installedPlugin"
}
Write-Host "  Dependencies: $dependencyInstallDir"
if ($isVrPlugin) {
    Write-Host "  Input:        $inputInstallDir"
}
Write-Host ""
Write-Host "Launch SteamVR first, then start SecretFlasherManaka.exe for headset testing."
