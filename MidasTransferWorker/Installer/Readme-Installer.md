WiX Installer README

Prerequisites
- WiX Toolset 3.11 installed: https://wixtoolset.org/releases/
- Build environment with msbuild or WiX tools on PATH.

How it works
- Product.wxs contains the MSI definition for installing files into
  %ProgramFiles(x86)%\MAM Software\MidasTransferService, registering the Windows Service
  "MidasTransferWorker", creating an EventLog source, and setting basic permissions.
- The Build-Installer.ps1 script harvests the publish folder with heat.exe (excluding the main exe),
  compiles the WiX sources with candle.exe and links with light.exe to produce the MSI.

Steps to build the MSI
1. Publish the worker project to a folder (example):
   dotnet publish MidasTransferWorker -c Release -r win-x64 --self-contained false -o C:\Services\MidasTransferWorker
2. Run the build script (from repository root):
   .\MidasTransferWorker\Installer\Build-Installer.ps1 -PublishedFolder 'C:\Services\MidasTransferWorker' -OutputMsi 'C:\Temp\MidasTransferWorker.msi'

Notes
- The Product.wxs registers the service to run as LocalSystem by default. Adjust Product.wxs if you require a custom service account.
- The installer grants broad permissions to SYSTEM and NETWORK SERVICE for the install folder. Review and tighten permissions to meet your security requirements.
- If you prefer an MSI that includes the .NET runtime, publish as self-contained and pass that publish folder to the build script.
