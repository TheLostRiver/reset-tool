param([switch]$FrameworkDependent)

$ErrorActionPreference = 'Stop'
$projectDocument = [xml][System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Frostbound.csproj'))
$packageVersion = [string]$projectDocument.Project.PropertyGroup.Version
$publishDirectory = Join-Path $PSScriptRoot "artifacts\v$packageVersion\win-x64"
$packagePath = Join-Path $PSScriptRoot "artifacts\Frostbound-v$packageVersion-win-x64.zip"
$checksumPath = Join-Path $PSScriptRoot "artifacts\Frostbound-v$packageVersion-SHA256.txt"
$selfContained = if ($FrameworkDependent) { 'false' } else { 'true' }

Push-Location -LiteralPath $PSScriptRoot
try {
    & dotnet publish 'Frostbound.csproj' -c Release -r win-x64 --self-contained $selfContained --nologo -o $publishDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Windows publish failed.' }
    $executablePath = Join-Path $publishDirectory 'Frostbound.exe'
    $readmePath = Join-Path $PSScriptRoot 'README.md'
    $assetsPath = Join-Path $PSScriptRoot 'Assets'
    Compress-Archive -LiteralPath @($executablePath, $readmePath, $assetsPath) -DestinationPath $packagePath -Force
    $checksums = foreach ($artifactPath in @($executablePath, $packagePath)) {
        $artifactHash = Get-FileHash -LiteralPath $artifactPath -Algorithm SHA256
        "$($artifactHash.Hash)  $([System.IO.Path]::GetFileName($artifactPath))"
    }
    [System.IO.File]::WriteAllLines($checksumPath, $checksums, [System.Text.UTF8Encoding]::new($false))
    Write-Output "Executable: $executablePath"
    Write-Output "Package: $packagePath"
    Write-Output "Checksums: $checksumPath"
}
finally { Pop-Location }
