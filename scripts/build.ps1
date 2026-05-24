[CmdletBinding()]
param(
    [string]$GameRoot,
    [string]$ProjectDir,
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$DotNetPath,
    [switch]$NoRestore
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
            throw "Game root could not be inferred. Run from VRModSrc or pass -GameRoot `"E:\erogame\SecretFlasherManaka v1.1.3`"."
        }

        $root = $candidate.ProviderPath
    }

    if (-not (Test-Path -LiteralPath (Join-Path $root "SecretFlasherManaka.exe"))) {
        throw "Game root is missing SecretFlasherManaka.exe: $root"
    }

    if (-not (Test-Path -LiteralPath (Join-Path $root "BepInEx\core"))) {
        throw "Game root is missing BepInEx\core. Install BepInEx 6 IL2CPP first or pass the correct -GameRoot."
    }

    if (-not (Test-Path -LiteralPath (Join-Path $root "BepInEx\interop"))) {
        throw "Game root is missing BepInEx\interop. Launch the game once with BepInEx so interop assemblies are generated."
    }

    return $root
}

function Resolve-DotNet {
    param(
        [string]$ExplicitDotNetPath,
        [string]$GameRootPath
    )

    if ($ExplicitDotNetPath) {
        if (-not (Test-Path -LiteralPath $ExplicitDotNetPath -PathType Leaf)) {
            throw ".NET CLI not found at -DotNetPath: $ExplicitDotNetPath"
        }

        return (Resolve-Path -LiteralPath $ExplicitDotNetPath).ProviderPath
    }

    $bundledDotNet = Join-Path $GameRootPath "dotnet\dotnet.exe"
    if (Test-Path -LiteralPath $bundledDotNet -PathType Leaf) {
        $sdkList = & $bundledDotNet --list-sdks 2>$null
        if ($LASTEXITCODE -eq 0 -and $sdkList) {
            return $bundledDotNet
        }
    }

    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnetCommand) {
        throw @"
.NET SDK is required but dotnet.exe was not found.
Install the .NET SDK, then reopen PowerShell and rerun:
  .\build.ps1 -GameRoot "$GameRootPath"
"@
    }

    return $dotnetCommand.Source
}

function Resolve-ProjectFile {
    param([string]$ProjectRoot)

    $root = Resolve-ExistingDirectory -Path $ProjectRoot -Description "Project directory"
    $preferred = Join-Path $root "SecretFlasherManakaVR.csproj"
    if (Test-Path -LiteralPath $preferred -PathType Leaf) {
        return (Resolve-Path -LiteralPath $preferred).ProviderPath
    }

    $projects = @(Get-ChildItem -LiteralPath $root -Filter "*.csproj" -File)
    if ($projects.Count -eq 1) {
        return $projects[0].FullName
    }

    if ($projects.Count -gt 1) {
        throw "Multiple .csproj files found in $root. Pass -ProjectDir with the SecretFlasherManakaVR project directory."
    }

    throw "No .csproj found in $root. Expected VRModSrc\SecretFlasherManakaVR\SecretFlasherManakaVR.csproj."
}

function Add-TrailingDirectorySeparator {
    param([string]$Path)

    if ($Path.EndsWith("\") -or $Path.EndsWith("/")) {
        return $Path
    }

    return "$Path\"
}

$scriptDir = Split-Path -Parent $PSCommandPath
$packageRoot = Resolve-Path -LiteralPath (Join-Path $scriptDir "..")
$gameRootPath = Resolve-GameRoot -ExplicitGameRoot $GameRoot
$projectRoot = if ($ProjectDir) { $ProjectDir } else { Join-Path $packageRoot.ProviderPath "src\SecretFlasherManakaVR" }
$projectFile = Resolve-ProjectFile -ProjectRoot $projectRoot
$dotnet = Resolve-DotNet -ExplicitDotNetPath $DotNetPath -GameRootPath $gameRootPath

$sdkList = & $dotnet --list-sdks 2>$null
if ($LASTEXITCODE -ne 0 -or -not $sdkList) {
    throw @"
dotnet was found, but no .NET SDK is installed for this CLI:
  $dotnet
Install a .NET SDK, not only the runtime. If a project global.json pins an SDK, install that exact version or remove/update the pin during integration.
"@
}

Write-Host "Building SecretFlasherManakaVR"
Write-Host "  Game root:    $gameRootPath"
Write-Host "  Project file: $projectFile"
Write-Host "  Configuration: $Configuration"
Write-Host "  dotnet:       $dotnet"

$buildArgs = @(
    "build",
    $projectFile,
    "--configuration",
    $Configuration,
    "/p:GameRoot=$gameRootPath",
    "/p:GameRootDir=$gameRootPath",
    "/p:BepInExRoot=$(Join-Path $gameRootPath "BepInEx")"
)

if ($NoRestore) {
    $buildArgs += "--no-restore"
}

& $dotnet @buildArgs
$exitCode = $LASTEXITCODE

if ($exitCode -ne 0) {
    Write-Host ""
    Write-Host "Build failed." -ForegroundColor Red
    Write-Host "Common fixes:"
    Write-Host "  - Missing .NET SDK: install the SDK shown by global.json or the latest supported SDK for the project target."
    Write-Host "  - Missing BepInEx references: confirm BepInEx\core exists under the game root."
    Write-Host "  - Missing interop references: launch the game once with BepInEx so BepInEx\interop\Assembly-CSharp.dll is generated."
    Write-Host "  - Missing OpenVR wrapper/native files: place them where the project file expects them, then rerun this script."
    exit $exitCode
}

Write-Host ""
Write-Host "Build succeeded. Next:"
Write-Host "  .\install.ps1 -GameRoot `"$gameRootPath`""
