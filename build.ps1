[CmdletBinding()]
param(
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Remove-DirectoryWithRetry {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    for ($attempt = 1; $attempt -le 5; $attempt++) {
        try {
            [System.IO.Directory]::Delete($Path, $true)
            return
        }
        catch [System.IO.IOException] {
            if ($attempt -eq 5) { throw }
        }
        catch [System.UnauthorizedAccessException] {
            if ($attempt -eq 5) { throw }
        }
        Start-Sleep -Milliseconds 250
    }
}

$repositoryRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
$solutionPath = Join-Path $repositoryRoot 'CmdsManager.sln'
$vswherePath = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswherePath)) {
    throw 'Visual Studio Installer vswhere.exe was not found.'
}

$visualStudioPath = & $vswherePath -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
if ([string]::IsNullOrWhiteSpace($visualStudioPath)) {
    throw 'Visual Studio Build Tools with MSBuild were not found.'
}

$msbuildPath = Join-Path $visualStudioPath 'MSBuild\Current\Bin\MSBuild.exe'
if (-not (Test-Path -LiteralPath $msbuildPath)) {
    throw "MSBuild was not found: $msbuildPath"
}

& $msbuildPath $solutionPath -restore -t:Rebuild -p:Configuration=Release -p:Platform=x64 -m -nologo -verbosity:minimal
if ($LASTEXITCODE -ne 0) {
    throw "Release build failed with exit code $LASTEXITCODE."
}

if (-not $SkipTests) {
    $testsPath = Join-Path $repositoryRoot 'tests\CmdsManager.Tests\bin\Release\CmdsManager.Tests.exe'
    & $testsPath
    if ($LASTEXITCODE -ne 0) {
        throw "Tests failed with exit code $LASTEXITCODE."
    }
}

$artifactsRoot = Join-Path $repositoryRoot 'artifacts'
$expectedArtifactsRoot = [System.IO.Path]::GetFullPath($artifactsRoot).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
$stagingRoot = Join-Path $artifactsRoot ('staging-' + [guid]::NewGuid().ToString('N'))
$resolvedStagingRoot = [System.IO.Path]::GetFullPath($stagingRoot)
if (-not $resolvedStagingRoot.StartsWith($expectedArtifactsRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Unsafe staging output path: $resolvedStagingRoot"
}
[System.IO.Directory]::CreateDirectory($resolvedStagingRoot) | Out-Null

$releaseRoot = Join-Path $repositoryRoot 'src\CmdsManager\bin\Release'
$files = @(
    'CmdsManager.exe',
    'CmdsManager.exe.config',
    'CmdsManager.ini.example'
)
foreach ($file in $files) {
    $source = Join-Path $releaseRoot $file
    if (-not (Test-Path -LiteralPath $source)) {
        throw "Required release file is missing: $source"
    }
    Copy-Item -LiteralPath $source -Destination (Join-Path $resolvedStagingRoot $file)
}
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'README.md') -Destination (Join-Path $resolvedStagingRoot 'README.md')

$zipPath = Join-Path $artifactsRoot 'CmdsManager-portable-0.1.0-win-x64.zip'
$resolvedZipPath = [System.IO.Path]::GetFullPath($zipPath)
if (-not $resolvedZipPath.StartsWith($expectedArtifactsRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Unsafe ZIP output path: $resolvedZipPath"
}
if ([System.IO.File]::Exists($resolvedZipPath)) {
    [System.IO.File]::Delete($resolvedZipPath)
}

Compress-Archive -Path (Join-Path $resolvedStagingRoot '*') -DestinationPath $resolvedZipPath -CompressionLevel Optimal

$exePath = Join-Path $releaseRoot 'CmdsManager.exe'
$exeInfo = Get-Item -LiteralPath $exePath
$zipInfo = Get-Item -LiteralPath $resolvedZipPath
$zipHash = Get-FileHash -LiteralPath $resolvedZipPath -Algorithm SHA256

try {
    Remove-DirectoryWithRetry -Path $resolvedStagingRoot
}
catch {
    Write-Warning "The ZIP is ready, but antivirus software is still holding the temporary staging directory: $resolvedStagingRoot"
}

[pscustomobject]@{
    Executable = $exePath
    ExeBytes = $exeInfo.Length
    PortableZip = $resolvedZipPath
    ZipBytes = $zipInfo.Length
    ZipSha256 = $zipHash.Hash
} | Format-List
