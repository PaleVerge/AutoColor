$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
if (-not (Test-Path $compiler)) { throw '未找到 .NET Framework C# 编译器。' }
New-Item -ItemType Directory -Force -Path .\dist | Out-Null
& $compiler /nologo /target:winexe /optimize+ /out:dist\AutoColor.exe /win32icon:icon.ico /r:System.Windows.Forms.dll /r:System.Drawing.dll AutoColor.cs
Write-Host '构建完成：dist\AutoColor.exe'
