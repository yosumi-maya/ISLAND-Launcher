param([Parameter(Mandatory = $true)][string]$JdkDirectory)
$ErrorActionPreference = 'Stop'
$launcherRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$classes = Join-Path $launcherRoot 'artifacts/quickplay-classes'
[System.IO.Directory]::CreateDirectory($classes) | Out-Null
& (Join-Path $JdkDirectory 'bin/javac.exe') --release 8 -d $classes (Join-Path $PSScriptRoot 'QuickPlayAgent.java')
if ($LASTEXITCODE -ne 0) { throw 'Quick Play helper compilation failed.' }
& (Join-Path $JdkDirectory 'bin/jar.exe') cfm (Join-Path $launcherRoot 'src/Island.Core/Assets/island-quick-play-1.0.jar') (Join-Path $PSScriptRoot 'MANIFEST.MF') -C $classes . -C $launcherRoot LICENSE
if ($LASTEXITCODE -ne 0) { throw 'Quick Play helper packaging failed.' }
