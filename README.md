# F1 2012 Track Editor

This repository contains a WPF editor for manipulating PSSG files used in Codemasters games.

## Building

The project is a Windows-only WPF application. The csproj sets `RuntimeIdentifier` to `win-x64`, so publishing is intended for 64‑bit Windows.

To build a release build for distribution you can use the provided publish profile:

```bash
cd "PSSG Editor"
dotnet publish -p:PublishProfile=Properties/PublishProfiles/FolderProfile.pubxml
```

This generates binaries under `bin/Release/net8.0-windows/publish/win-x64/`.
