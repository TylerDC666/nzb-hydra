$ErrorActionPreference = 'Stop'
$appRoot = Split-Path -Parent $PSScriptRoot
$outputDir = Join-Path $appRoot 'bin'
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ "/win32manifest:$PSScriptRoot\app.manifest" "/win32icon:$appRoot\assets\kestrel.ico" "/resource:$appRoot\assets\kestrel.png,kestrel.png" "/resource:$appRoot\assets\kestrel.ico,kestrel.ico" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.dll /reference:System.Xml.Linq.dll /reference:System.Core.dll /reference:System.Web.Extensions.dll "/out:$outputDir\NZB Kestrel.exe" "$PSScriptRoot\NZBKestrel.cs" "$PSScriptRoot\Engine.cs" "$PSScriptRoot\Tests.cs" "$PSScriptRoot\Theme.cs" "$PSScriptRoot\Localization.cs"
if ($LASTEXITCODE -ne 0) { throw 'Build fehlgeschlagen' }

Copy-Item -LiteralPath "$PSScriptRoot\app.config" -Destination "$outputDir\NZB Kestrel.exe.config" -Force
