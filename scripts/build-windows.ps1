param([string]$Output = (Join-Path $PSScriptRoot '..\dist\windows'))

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).ProviderPath
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
$programFilesX86 = [Environment]::GetFolderPath('ProgramFilesX86')
$sdk = Join-Path $programFilesX86 'Windows Kits\10\UnionMetadata'
$winMd = Get-ChildItem -LiteralPath $sdk -Filter Windows.winmd -File -Recurse |
    Where-Object { $_.Directory.Name -match '^\d+\.\d+\.\d+\.\d+$' } |
    Sort-Object { [version]$_.Directory.Name } -Descending | Select-Object -First 1
if (-not $winMd) { throw 'Install the Windows SDK with the Windows.winmd unified metadata file.' }
$facades = Join-Path $programFilesX86 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\Facades'
$assets = Join-Path $root 'assets'
$sources = @(Get-ChildItem -LiteralPath (Join-Path $root 'windows') -Filter '*.cs' -File | Select-Object -ExpandProperty FullName)
$reference = @('System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'System.Xml.dll',
    'System.Xml.Linq.dll', 'System.Numerics.dll', 'System.Numerics.Vectors.dll', 'Microsoft.CSharp.dll',
    (Join-Path $framework 'System.Runtime.WindowsRuntime.dll'), (Join-Path $facades 'System.Runtime.dll'),
    (Join-Path $facades 'System.Runtime.InteropServices.WindowsRuntime.dll'), $winMd.FullName) |
    ForEach-Object { '/reference:' + $_ }
$null = New-Item -ItemType Directory -Force -Path $Output
$destination = Join-Path (Resolve-Path -LiteralPath $Output).ProviderPath 'HappyNewIsland.exe'
$options = @('/nologo', '/nowarn:1701', '/target:winexe', '/platform:x64', '/optimize+', '/codepage:65001',
    ('/out:' + $destination), ('/win32icon:' + (Join-Path $assets 'WinterIsland.ico')),
    ('/win32manifest:' + (Join-Path $root 'windows\WinterIsland.manifest')),
    ('/resource:' + (Join-Path $assets 'garland.png') + ',WinterIsland.Garland'),
    ('/resource:' + (Join-Path $assets 'snowdrift-atlas.png') + ',WinterIsland.SnowDrifts'),
    ('/resource:' + (Join-Path $assets 'leopard-model.bin') + ',WinterIsland.LeopardModel'))
& $compiler @options @reference @sources
if ($LASTEXITCODE -ne 0) { throw "Windows build failed ($LASTEXITCODE)." }
Get-Item -LiteralPath $destination
