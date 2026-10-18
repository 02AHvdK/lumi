$ErrorActionPreference='Stop'
$fx=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$refs=@('System.dll','System.Core.dll','System.Management.dll','System.Xaml.dll') | ForEach-Object { '/reference:'+(Join-Path $fx $_) }
$refs+=@('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll') | ForEach-Object { '/reference:'+(Join-Path "$fx\WPF" $_) }
$resources=@('Theme.xaml','MainWindow.xaml','QuickWindow.xaml','tray.png','option1.png','option2.png','option3.png') | ForEach-Object {"/resource:$PSScriptRoot\$_,$_"}
$source=Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | ForEach-Object FullName
& "$fx\csc.exe" /nologo /target:winexe /platform:x64 /optimize+ "/out:$PSScriptRoot\lumi.exe" "/win32icon:$PSScriptRoot\lumi.ico" "/win32manifest:$PSScriptRoot\app.manifest" $refs $resources $source
if($LASTEXITCODE -ne 0){throw '编译失败'}

