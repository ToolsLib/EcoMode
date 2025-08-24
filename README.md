# EcoMode Switch UI

# Build and Run (development)

```
dotnet build
dotnet run
```

# Deployment

```
# if you want to rely on user's .NET Desktop Runtime (smaller):
dotnet publish -c Release -r win-x64 --self-contained false

# OR ship everything (bigger, no runtime needed):
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
```

..then build it with Inno Setup Compiler.

## Code signing

Sign your binaries with a code signing certificate to ensure integrity and authenticity.

```
signtool.exe sign /f "path\to\your\certificate.pfx" /p "your_certificate_password" EcoModeSetup.exe
```

# Notes & tweaks

Tuning values: EPP/Boost numbers are conservative defaults; adjust in ApplyPowerPlan.
