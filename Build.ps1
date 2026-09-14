param()

$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $MyInvocation.MyCommand.Path
$buildRoot = Join-Path $workspace 'work\build'
$outputs = Join-Path $workspace 'outputs'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$csc = Join-Path $framework 'csc.exe'
$gacRoots = @(
    (Join-Path $env:WINDIR 'Microsoft.NET\assembly\GAC_MSIL'),
    (Join-Path $env:WINDIR 'Microsoft.NET\assembly\GAC_64')
)

if (-not (Test-Path -LiteralPath $csc)) { throw "Framework C# compiler not found: $csc" }
New-Item -ItemType Directory -Force -Path $buildRoot, $outputs | Out-Null

function Find-GacAssembly([string]$name) {
    foreach ($gacRoot in $gacRoots) {
        $candidate = Get-ChildItem -LiteralPath (Join-Path $gacRoot $name) -Recurse -Filter ($name + '.dll') -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($candidate) { return $candidate.FullName }
    }
    throw "Assembly not found in the Global Assembly Cache: $name"
}

function Invoke-Csc([string]$name, [string[]]$lines) {
    $responseFile = Join-Path $buildRoot ($name + '-' + [Guid]::NewGuid().ToString('N') + '.rsp')
    [System.IO.File]::WriteAllLines($responseFile, $lines, [System.Text.Encoding]::ASCII)
    & $csc /nologo /noconfig ('@' + $responseFile)
    if ($LASTEXITCODE -ne 0) { throw "csc failed for $name (exit $LASTEXITCODE)" }
}

$coreOutput = Join-Path $buildRoot 'RenameGuard.Core.dll'
$coreLines = @(
    '/target:library',
    '/platform:x64',
    ('/out:"' + $coreOutput + '"'),
    ('/reference:"' + (Join-Path $framework 'System.dll') + '"'),
    ('/reference:"' + (Join-Path $framework 'System.Core.dll') + '"'),
    ('/reference:"' + (Find-GacAssembly 'System.Web.Extensions') + '"')
) + (Get-ChildItem -LiteralPath (Join-Path $workspace 'RenameGuard.Core') -Filter '*.cs' | ForEach-Object { '"' + $_.FullName + '"' })
Invoke-Csc 'core' $coreLines

$appOutput = Join-Path $buildRoot 'RenameGuard.exe'
$appLines = @(
    '/target:winexe',
    '/platform:x64',
    ('/out:"' + $appOutput + '"'),
    ('/reference:"' + $coreOutput + '"'),
    ('/reference:"' + (Join-Path $framework 'System.dll') + '"'),
    ('/reference:"' + (Join-Path $framework 'System.Core.dll') + '"'),
    ('/reference:"' + (Join-Path $framework 'System.Windows.Forms.dll') + '"'),
    ('/reference:"' + (Join-Path $framework 'System.Drawing.dll') + '"')
) + (@('PresentationFramework', 'PresentationCore', 'WindowsBase', 'System.Xaml', 'System.Windows.Presentation') | ForEach-Object { '/reference:"' + (Find-GacAssembly $_) + '"' }) + (Get-ChildItem -LiteralPath (Join-Path $workspace 'RenameGuard.App') -Filter '*.cs' | ForEach-Object { '"' + $_.FullName + '"' })
Invoke-Csc 'app' $appLines
Copy-Item -LiteralPath (Join-Path $workspace 'RenameGuard.App\App.config') -Destination ($appOutput + '.config') -Force

$testsOutput = Join-Path $buildRoot 'RenameGuard.Tests.exe'
$testLines = @(
    '/target:exe',
    '/platform:x64',
    ('/out:"' + $testsOutput + '"'),
    ('/reference:"' + $coreOutput + '"'),
    ('/reference:"' + (Join-Path $framework 'System.dll') + '"'),
    ('/reference:"' + (Join-Path $framework 'System.Core.dll') + '"')
) + (Get-ChildItem -LiteralPath (Join-Path $workspace 'RenameGuard.Tests') -Filter '*.cs' | ForEach-Object { '"' + $_.FullName + '"' })
Invoke-Csc 'tests' $testLines
$testRoot = Join-Path $workspace 'work\testdata'
New-Item -ItemType Directory -Force -Path $testRoot | Out-Null
& $testsOutput $testRoot
if ($LASTEXITCODE -ne 0) { throw "RenameGuard tests failed (exit $LASTEXITCODE)" }

$payloadDirectory = Join-Path $buildRoot ('payload-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $payloadDirectory | Out-Null
Copy-Item -LiteralPath $appOutput, $coreOutput, ($appOutput + '.config'), (Join-Path $workspace 'README.md'), (Join-Path $workspace 'LICENSE') -Destination $payloadDirectory -Force
$portableZip = Join-Path $outputs 'RenameGuard-Portable-1.0.0-x64.zip'
Compress-Archive -Path (Join-Path $payloadDirectory '*') -DestinationPath $portableZip -Force -CompressionLevel Optimal

$payloadZip = Join-Path $buildRoot ('payload-' + [Guid]::NewGuid().ToString('N') + '.zip')
Compress-Archive -Path (Join-Path $payloadDirectory '*') -DestinationPath $payloadZip -Force -CompressionLevel Optimal
$setupOutput = Join-Path $outputs 'RenameGuard-Setup-1.0.0-x64.exe'
$setupLines = @(
    '/target:winexe',
    '/platform:x64',
    ('/out:"' + $setupOutput + '"'),
    ('/reference:"' + (Join-Path $framework 'System.dll') + '"'),
    ('/reference:"' + (Join-Path $framework 'System.Core.dll') + '"'),
    ('/reference:"' + (Join-Path $framework 'System.Windows.Forms.dll') + '"'),
    ('/reference:"' + (Join-Path $framework 'System.Drawing.dll') + '"'),
    ('/reference:"' + (Find-GacAssembly 'System.IO.Compression') + '"'),
    ('/reference:"' + (Find-GacAssembly 'System.IO.Compression.FileSystem') + '"'),
    ('/resource:"' + $payloadZip + '",RenameGuard.Setup.Payload.zip'),
    ('"' + (Join-Path $workspace 'Installer\Setup.cs') + '"')
)
Invoke-Csc 'setup' $setupLines

$extractSmoke = Join-Path $buildRoot ('setup-extract-' + [Guid]::NewGuid().ToString('N'))
$extractProcess = Start-Process -FilePath $setupOutput -ArgumentList ('/extract=' + $extractSmoke) -Wait -PassThru
if ($extractProcess.ExitCode -ne 0) { throw "Setup payload extraction smoke test failed (exit $($extractProcess.ExitCode))" }
foreach ($file in @('RenameGuard.exe', 'RenameGuard.Core.dll', 'RenameGuard.exe.config', 'README.md', 'LICENSE')) {
    if (-not (Test-Path -LiteralPath (Join-Path $extractSmoke $file))) { throw "Setup payload missing $file" }
}

Copy-Item -LiteralPath (Join-Path $workspace 'README.md') -Destination (Join-Path $outputs 'README.md') -Force
Copy-Item -LiteralPath (Join-Path $workspace 'LICENSE') -Destination (Join-Path $outputs 'LICENSE') -Force
$hashLines = @($setupOutput, $portableZip, (Join-Path $outputs 'README.md'), (Join-Path $outputs 'LICENSE')) | ForEach-Object {
    $hash = Get-FileHash -LiteralPath $_ -Algorithm SHA256
    $hash.Hash.ToLowerInvariant() + '  ' + [System.IO.Path]::GetFileName($_)
}
Set-Content -LiteralPath (Join-Path $outputs 'SHA256SUMS.txt') -Value $hashLines -Encoding ASCII
Get-ChildItem -LiteralPath $outputs -File | Select-Object Name, Length
