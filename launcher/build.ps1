param([string]$Runtime = 'win-x64', [switch]$SkipTests, [string]$OutputDirectory = '', [switch]$NoRestore)
$ErrorActionPreference = 'Stop'
$launcherRoot = $PSScriptRoot
$offlineOptions = if ($NoRestore) { @('--no-restore', '-m:1', '-nodeReuse:false', '-p:UseSharedCompilation=false') } else { @() }
if ($NoRestore -and -not $SkipTests) {
    & dotnet build (Join-Path $launcherRoot 'Island.sln') -c Release @offlineOptions
    if ($LASTEXITCODE -ne 0) { throw '启动器构建失败。' }
}
if (-not $SkipTests) {
    if ($NoRestore) { & dotnet (Join-Path $launcherRoot 'tests/Island.Tests/bin/Release/net8.0-windows/Island.Tests.dll') }
    else { & dotnet run --project (Join-Path $launcherRoot 'tests/Island.Tests') -c Release }
    if ($LASTEXITCODE -ne 0) { throw '启动核心测试失败。' }
    if ($NoRestore) { & dotnet (Join-Path $launcherRoot 'tests/Island.App.Tests/bin/Release/net8.0-windows/Island.App.Tests.dll') }
    else { & dotnet run --project (Join-Path $launcherRoot 'tests/Island.App.Tests') -c Release }
    if ($LASTEXITCODE -ne 0) { throw '界面和启动流程测试失败。' }
}
$outputPath = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { Join-Path $launcherRoot "dist/$Runtime" } else { [System.IO.Path]::GetFullPath($OutputDirectory) }
& dotnet publish (Join-Path $launcherRoot 'src/Island.App/Island.App.csproj') -c Release -r $Runtime --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -o $outputPath @offlineOptions
if ($LASTEXITCODE -ne 0) { throw '启动器发布失败。' }
Copy-Item -LiteralPath (Join-Path $launcherRoot 'README.md'),(Join-Path $launcherRoot 'REFERENCES.md'),(Join-Path $launcherRoot 'VERIFICATION.md'),(Join-Path $launcherRoot 'design-polish.md'),(Join-Path $launcherRoot 'LICENSE') -Destination $outputPath -Force
Get-ChildItem -LiteralPath (Join-Path $launcherRoot 'third-party') -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $outputPath -Force }
$dotnetDirectory = Split-Path (Get-Command dotnet).Source
foreach ($notice in @(@('LICENSE.txt','DOTNET-LICENSE.txt'), @('ThirdPartyNotices.txt','DOTNET-THIRD-PARTY-NOTICES.txt'))) {
    $noticeSource = Join-Path $dotnetDirectory $notice[0]
    if (Test-Path -LiteralPath $noticeSource) { Copy-Item -LiteralPath $noticeSource -Destination (Join-Path $outputPath $notice[1]) -Force }
}
$hash = Get-FileHash -LiteralPath (Join-Path $outputPath '岛屿启动器.exe') -Algorithm SHA256
[System.IO.File]::WriteAllText((Join-Path $outputPath 'SHA256SUMS.txt'), "$($hash.Hash.ToLowerInvariant())  岛屿启动器.exe`n", [System.Text.UTF8Encoding]::new($false))
$applicationProject = [xml](Get-Content -LiteralPath (Join-Path $launcherRoot 'src/Island.App/Island.App.csproj') -Raw)
$applicationVersion = $applicationProject.SelectSingleNode('//Version').InnerText
$archivePath = Join-Path $launcherRoot "dist/IslandLauncher-$applicationVersion-$Runtime.zip"
$releaseFiles = Get-ChildItem -LiteralPath $outputPath -File | Where-Object { $_.Extension -ne '.pdb' }
Compress-Archive -LiteralPath $releaseFiles.FullName -DestinationPath $archivePath -Force
Write-Output "启动器：$(Join-Path $outputPath '岛屿启动器.exe')"
Write-Output "便携包：$archivePath"
Write-Output "SHA256：$($hash.Hash)"
