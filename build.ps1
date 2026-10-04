param([switch]$FrameworkDependent)

$ErrorActionPreference = 'Stop'
$publishDirectory = Join-Path $PSScriptRoot 'artifacts\win-x64'
$packagePath = Join-Path $PSScriptRoot 'artifacts\Frostbound-win-x64.zip'
$selfContained = if ($FrameworkDependent) { 'false' } else { 'true' }

Push-Location -LiteralPath $PSScriptRoot
try {
    & dotnet publish 'Frostbound.csproj' -c Release -r win-x64 --self-contained $selfContained --nologo -o $publishDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Windows publish failed.' }
    $executablePath = Join-Path $publishDirectory 'Frostbound.exe'
    $readmePath = Join-Path $PSScriptRoot 'README.md'
    $assetsPath = Join-Path $PSScriptRoot 'Assets'
    Compress-Archive -LiteralPath @($executablePath, $readmePath, $assetsPath) -DestinationPath $packagePath -Force
    Write-Output "Executable: $executablePath"
    Write-Output "Package: $packagePath"
}
finally { Pop-Location }
