@echo off
"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" -nologo -target:winexe -out:LiquidGlass.exe %* -lib:"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF" -r:PresentationFramework.dll -r:PresentationCore.dll -r:WindowsBase.dll -r:System.Xaml.dll LiquidGlass.cs
