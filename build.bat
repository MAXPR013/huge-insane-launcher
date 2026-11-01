@echo off
rem HIL build: in-box csc (C# 5) -> single self-contained exe.
rem Kill HugeInsaneLauncher.exe first, otherwise csc cannot overwrite it.
rem UI block/loader icons are embedded as img.* resources and released to
rem HugeInsaneLauncher\Images on startup (see Paths.EnsureImages).
"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" -nologo -target:winexe -out:HugeInsaneLauncher.exe %* ^
 -lib:"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF" ^
 -r:PresentationFramework.dll -r:PresentationCore.dll -r:WindowsBase.dll ^
 -r:System.Web.Extensions.dll -r:System.Xml.dll ^
 -r:System.IO.Compression.dll -r:System.IO.Compression.FileSystem.dll ^
 -r:System.Windows.Forms.dll -r:System.Drawing.dll -r:System.Xaml.dll ^
 -win32icon:"HugeInsaneLauncher\Images\hil.ico" ^
 -resource:"HugeInsaneLauncher\Images\steve.png",stevepng ^
 -resource:"HugeInsaneLauncher\tools\lan-agent\hil-lan-agent.jar",hil-lan-agent.jar ^
 -resource:"HugeInsaneLauncher\Images\Blocks\Anvil.png",img.Blocks.Anvil.png ^
 -resource:"HugeInsaneLauncher\Images\Blocks\CobbleStone.png",img.Blocks.CobbleStone.png ^
 -resource:"HugeInsaneLauncher\Images\Blocks\CommandBlock.png",img.Blocks.CommandBlock.png ^
 -resource:"HugeInsaneLauncher\Images\Blocks\CraftingTable.png",img.Blocks.CraftingTable.png ^
 -resource:"HugeInsaneLauncher\Images\Blocks\Egg.png",img.Blocks.Egg.png ^
 -resource:"HugeInsaneLauncher\Images\Blocks\Furnace.png",img.Blocks.Furnace.png ^
 -resource:"HugeInsaneLauncher\Images\Blocks\GoldBlock.png",img.Blocks.GoldBlock.png ^
 -resource:"HugeInsaneLauncher\Images\Blocks\Grass.png",img.Blocks.Grass.png ^
 -resource:"HugeInsaneLauncher\Images\Blocks\GrassPath.png",img.Blocks.GrassPath.png ^
 -resource:"HugeInsaneLauncher\Images\Blocks\RedstoneBlock.png",img.Blocks.RedstoneBlock.png ^
 -resource:"HugeInsaneLauncher\Images\Loaders\Fabric.png",img.Loaders.Fabric.png ^
 -resource:"HugeInsaneLauncher\Images\Loaders\NeoForge.png",img.Loaders.NeoForge.png ^
 -recurse:"HugeInsaneLauncher\src\*.cs"
