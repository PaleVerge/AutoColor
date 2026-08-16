$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
if (-not (Test-Path $compiler)) { throw 'csc.exe not found (.NET Framework).' }
New-Item -ItemType Directory -Force -Path .\dist | Out-Null
& $compiler /nologo /target:winexe /optimize+ /out:dist\AutoColor.exe /win32icon:icon.ico /win32manifest:app.manifest /r:System.Windows.Forms.dll /r:System.Drawing.dll AutoColor.cs
if ($LASTEXITCODE -ne 0) { throw "csc failed: $LASTEXITCODE" }
Write-Host 'OK: dist\AutoColor.exe'
