# CenterHub Installer

## Prerequisites

1. Install WiX Toolset v5:
   ```powershell
   dotnet tool install --global wix
   ```

2. Ensure .NET 8 SDK is installed

## Building the MSI

### Option 1: Using the build script (Recommended)
From the project root directory:
```powershell
.\build-installer.ps1
```

### Option 2: Manual build
```powershell
# Build the main application
dotnet build ..\CenterHubNew.csproj -c Release -p:Platform=x64

# Build the installer
dotnet build CenterHub.wixproj -c Release
```

## Output
The MSI installer will be created at:
```
installer\bin\Release\CenterHub.msi
```

## Customization

### Changing the version
Update the `Version` attribute in `Package.wxs` and `CenterHubNew.csproj`

### Adding files to the installer
Edit `Package.wxs` to add new components to the `ProductComponents` ComponentGroup.

## Troubleshooting

### "WixToolset.Sdk not found"
Run: `dotnet tool install --global wix`

### Missing dependencies
The installer references files from the build output. Make sure to build the main project first.

