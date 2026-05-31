[CmdletBinding()]
param(
    [string]$GameRoot,
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$OutputDir,
    [string]$PackageName,
    [switch]$SkipBuild,
    [switch]$IncludeDebugSymbols,
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
        $candidate = Resolve-Path -LiteralPath (Join-Path $scriptDir "..\..") -ErrorAction SilentlyContinue
        if (-not $candidate -or -not (Test-Path -LiteralPath (Join-Path $candidate.ProviderPath "SecretFlasherManaka.exe"))) {
            throw "Game root could not be inferred. Pass -GameRoot `"<path to SecretFlasherManaka game root>`"."
        }

        $root = $candidate.ProviderPath
    }

    if (-not (Test-Path -LiteralPath (Join-Path $root "SecretFlasherManaka.exe") -PathType Leaf)) {
        throw "Game root is missing SecretFlasherManaka.exe: $root"
    }

    return $root
}

function Copy-RequiredFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Source,
        [Parameter(Mandatory = $true)]
        [string]$Destination
    )

    if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) {
        throw "Required release file not found: $Source"
    }

    $destinationDir = Split-Path -Parent $Destination
    New-Item -ItemType Directory -Path $destinationDir -Force | Out-Null
    Copy-Item -LiteralPath $Source -Destination $Destination -Force
}

function Get-AssemblyVersionText {
    param([string]$AssemblyInfoPath)

    if (-not (Test-Path -LiteralPath $AssemblyInfoPath -PathType Leaf)) {
        return $null
    }

    $match = Select-String -LiteralPath $AssemblyInfoPath -Pattern 'AssemblyInformationalVersion\("([^"]+)"\)' -AllMatches | Select-Object -First 1
    if ($match -and $match.Matches.Count -gt 0) {
        return $match.Matches[0].Groups[1].Value
    }

    $match = Select-String -LiteralPath $AssemblyInfoPath -Pattern 'AssemblyVersion\("([^"]+)"\)' -AllMatches | Select-Object -First 1
    if ($match -and $match.Matches.Count -gt 0) {
        return $match.Matches[0].Groups[1].Value
    }

    return $null
}

$scriptDir = Split-Path -Parent $PSCommandPath
$packageRoot = (Resolve-Path -LiteralPath (Join-Path $scriptDir "..")).ProviderPath
$gameRootPath = Resolve-GameRoot -ExplicitGameRoot $GameRoot

if (-not $SkipBuild) {
    & (Join-Path $scriptDir "build.ps1") -GameRoot $gameRootPath -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

$version = Get-AssemblyVersionText -AssemblyInfoPath (Join-Path $packageRoot "src\SecretFlasherManakaVR\Properties\AssemblyInfo.cs")
if (-not $version) {
    $version = "0.0.0"
}

if (-not $OutputDir) {
    $OutputDir = Join-Path $packageRoot "release"
}

if (-not $PackageName) {
    $safeVersion = $version -replace '[^\w\.-]+', '-'
    $PackageName = "SecretFlasherManakaVR-$safeVersion-$Configuration.zip"
}

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
$outputRoot = (Resolve-Path -LiteralPath $OutputDir).ProviderPath
$zipPath = Join-Path $outputRoot $PackageName
if ([System.IO.Path]::GetExtension($zipPath) -ne ".zip") {
    $zipPath = "$zipPath.zip"
}

if ((Test-Path -LiteralPath $zipPath -PathType Leaf) -and -not $Force) {
    throw "Package already exists: $zipPath. Pass -Force to overwrite it."
}

$stagingRoot = Join-Path $outputRoot "_package_staging"
if (Test-Path -LiteralPath $stagingRoot) {
    Remove-Item -LiteralPath $stagingRoot -Recurse -Force
}

$pluginsDir = Join-Path $stagingRoot "BepInEx\plugins"
$inputDir = Join-Path $pluginsDir "SecretFlasherManakaVR_Input"
$configDir = Join-Path $stagingRoot "BepInEx\config"

Copy-RequiredFile -Source (Join-Path $packageRoot "src\SecretFlasherManakaVR\bin\$Configuration\SecretFlasherManakaVR.dll") -Destination (Join-Path $pluginsDir "SecretFlasherManakaVR.dll")
Copy-RequiredFile -Source (Join-Path $packageRoot "src\SecretFlasherManakaRingMenuLongPress\bin\$Configuration\SecretFlasherManakaRingMenuLongPress.dll") -Destination (Join-Path $pluginsDir "SecretFlasherManakaRingMenuLongPress.dll")
Copy-RequiredFile -Source (Join-Path $packageRoot "dependencies\openvr_api.dll") -Destination (Join-Path $pluginsDir "openvr_api.dll")

Copy-RequiredFile -Source (Join-Path $packageRoot "input\actions.json") -Destination (Join-Path $inputDir "actions.json")
Copy-RequiredFile -Source (Join-Path $packageRoot "input\bindings_oculus_touch.json") -Destination (Join-Path $inputDir "bindings_oculus_touch.json")

Copy-RequiredFile -Source (Join-Path $packageRoot "config\com.codex.secretflashermanaka.vr.cfg") -Destination (Join-Path $configDir "com.codex.secretflashermanaka.vr.cfg")
Copy-RequiredFile -Source (Join-Path $packageRoot "config\com.codex.secretflashermanaka.ringmenulongpress.cfg") -Destination (Join-Path $configDir "com.codex.secretflashermanaka.ringmenulongpress.cfg")

if ($IncludeDebugSymbols) {
    $symbolsDir = Join-Path $stagingRoot "debug_symbols"
    Copy-RequiredFile -Source (Join-Path $packageRoot "src\SecretFlasherManakaVR\bin\$Configuration\SecretFlasherManakaVR.pdb") -Destination (Join-Path $symbolsDir "SecretFlasherManakaVR.pdb")
    Copy-RequiredFile -Source (Join-Path $packageRoot "src\SecretFlasherManakaRingMenuLongPress\bin\$Configuration\SecretFlasherManakaRingMenuLongPress.pdb") -Destination (Join-Path $symbolsDir "SecretFlasherManakaRingMenuLongPress.pdb")
}

if (Test-Path -LiteralPath $zipPath -PathType Leaf) {
    Remove-Item -LiteralPath $zipPath -Force
}

$itemsToCompress = Get-ChildItem -LiteralPath $stagingRoot -Force
Compress-Archive -LiteralPath $itemsToCompress.FullName -DestinationPath $zipPath -CompressionLevel Optimal -Force

$releaseFiles = @(Get-ChildItem -LiteralPath $stagingRoot -Recurse -File | Sort-Object FullName)
Write-Host ""
Write-Host "Release package created:"
Write-Host "  $zipPath"
Write-Host ""
Write-Host "Zip layout is relative to the game root. Players should extract it into the folder that contains SecretFlasherManaka.exe."
Write-Host ""
Write-Host "Included files:"
foreach ($file in $releaseFiles) {
    $relative = $file.FullName.Substring($stagingRoot.Length).TrimStart("\", "/")
    Write-Host "  $relative"
}

Remove-Item -LiteralPath $stagingRoot -Recurse -Force
