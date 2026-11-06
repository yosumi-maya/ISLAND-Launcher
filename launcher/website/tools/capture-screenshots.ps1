$ErrorActionPreference = 'Stop'
$website = Split-Path $PSScriptRoot
$launcher = Split-Path $website
$workspace = Split-Path $launcher
& python (Join-Path $PSScriptRoot 'prepare-screenshots.py')
if ($LASTEXITCODE -ne 0) { throw 'Screenshot fixture creation failed.' }
$fixture = Join-Path $launcher 'artifacts/island-website/fixture'
$assets = Join-Path $website 'assets'
New-Item -ItemType Directory -Path $assets -Force | Out-Null
$env:TEMP = Join-Path $launcher 'artifacts/test-temp'; $env:TMP = $env:TEMP
$env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = Join-Path $env:TEMP 'bundle-extract'
foreach ($theme in @(@('forest','#2D7568'),@('rose','#AD526E'),@('glass','#5DAF98'))) {
    $settings = Join-Path $fixture ('settings-' + $theme[0])
    $existingCache = Join-Path $launcher 'artifacts/java-cache-published-settings/java-runtimes.json'
    if (Test-Path -LiteralPath $existingCache) { Copy-Item -LiteralPath $existingCache -Destination $settings -Force }
    $shots = Join-Path $launcher ('artifacts/island-website/screenshots-' + $theme[0])
    $process = Start-Process -FilePath (Join-Path $launcher 'dist/win-x64-0.10.22/岛屿启动器.exe') -ArgumentList @('--capture',('"{0}"' -f $shots),'--data-dir',('"{0}"' -f $settings),'--capture-preset',$theme[1]) -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(45000)) { $process.Kill($true); throw 'Owned screenshot capture timed out.' }
    if ($process.ExitCode -ne 0 -or (Test-Path -LiteralPath (Join-Path $shots 'error.txt'))) { throw ('Screenshot capture failed: ' + $theme[0]) }
    Copy-Item -LiteralPath (Join-Path $shots 'NavLibrary-1240.png') -Destination (Join-Path $assets ('screen-' + $theme[0] + '.png')) -Force
    if ($theme[0] -eq 'forest') { Copy-Item -LiteralPath (Join-Path $shots 'Appearance-1240.png') -Destination (Join-Path $assets 'screen-appearance.png') -Force }
    $process.Dispose()
    Write-Output ('Captured public demo theme: ' + $theme[0])
}

