param([ValidateSet('win-x64', 'win-arm64')][string]$Runtime = 'win-x64')
$ErrorActionPreference = 'Stop'
$rootDirectory = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $rootDirectory
try {
    dotnet build CursorJail.slnx --configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    dotnet run --project tests/CursorJail.Specs --configuration Release --no-build
    if ($LASTEXITCODE -ne 0) { throw 'Specifications failed.' }
    $outputDirectory = Join-Path $rootDirectory "dist/CursorJail-$Runtime"
    dotnet publish src/CursorJail --configuration Release --runtime $Runtime --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true --output $outputDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    Copy-Item -LiteralPath (Join-Path $rootDirectory 'README.md') -Destination $outputDirectory
    Compress-Archive -Path (Join-Path $outputDirectory '*') -DestinationPath (Join-Path $rootDirectory "dist/CursorJail-$Runtime.zip") -Force
    Write-Output "Portable app: $outputDirectory/CursorJail.exe"
} finally { Pop-Location }
