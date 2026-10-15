$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$source = Join-Path $root 'src\NaiwaPetSprite.cs'
$framesDir = Join-Path $root 'src\laugh_frames'
$output = Join-Path $root 'NaiwaPet.exe'
$image = Get-ChildItem -LiteralPath $root -Filter '*.png' -File | Select-Object -First 1

if (-not (Test-Path -LiteralPath $csc)) {
    throw 'The .NET Framework 4.x compiler was not found.'
}
if (-not $image) { throw 'The character PNG was not found in the project root.' }

function Find-GacAssembly([string] $name) {
    $gacRoots = @(
        (Join-Path $env:WINDIR 'Microsoft.NET\assembly\GAC_64'),
        (Join-Path $env:WINDIR 'Microsoft.NET\assembly\GAC_MSIL')
    )
    foreach ($gacRoot in $gacRoots) {
        if (Test-Path -LiteralPath $gacRoot) {
            $match = Get-ChildItem -LiteralPath $gacRoot -Filter ($name + '.dll') -File -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($match) { return $match.FullName }
        }
    }
    throw "Assembly not found: $name.dll"
}

$presentationCore = Find-GacAssembly 'PresentationCore'
$presentationFramework = Find-GacAssembly 'PresentationFramework'
$windowsBase = Find-GacAssembly 'WindowsBase'
$systemXaml = Find-GacAssembly 'System.Xaml'
$frames = @(Get-ChildItem -LiteralPath $framesDir -Filter 'laugh-*.png' -File | Sort-Object Name)
if ($frames.Count -ne 92) { throw "Expected 92 animation frames, found $($frames.Count)." }

$arguments = @(
    '/nologo', '/target:winexe', "/out:$output",
    '/reference:System.dll',
    '/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll',
    "/reference:$presentationCore",
    "/reference:$presentationFramework",
    "/reference:$windowsBase",
    "/reference:$systemXaml",
    "/resource:$($image.FullName),naiwa-cutout.png"
)

foreach ($frame in $frames) {
    $arguments += "/resource:$($frame.FullName),$($frame.Name)"
}
$arguments += $source

& $csc @arguments
if ($LASTEXITCODE -ne 0) { throw "Compilation failed with exit code $LASTEXITCODE." }
Write-Host "Created $output"
