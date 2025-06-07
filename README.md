# F1 2012 Track Editor

This repository contains a small proof-of-concept editor for the PSSG file format used by Codemasters' EGO Engine (for example in *F1 2012*). The WPF application inside the `PSSG Editor` directory can open a `.pssg` archive and show its node hierarchy, attributes and raw data.

## Sample files

The `catalunya/` directory provides reference assets extracted from the Barcelona–Catalunya track. These files can be opened with the editor to explore typical PSSG structures:

```
catalunya/ground_cover.pssg
catalunya/objects.pssg
catalunya/sky.pssg
```

There are also XML configuration files, DAT tables and other resources used by the track. They are included purely as examples.

## Building

The project targets **.NET 8** with WPF. Use the .NET SDK (with the `Microsoft.NET.Sdk.WindowsDesktop` workloads installed) and run:

```bash
dotnet build "PSSG Editor/PSSG Editor.csproj" -c Release
```

This produces a Windows executable under `PSSG Editor/bin/Release/net8.0-windows/win-x64/`.

## Running

After a successful build you can start the editor with:

```bash
dotnet run --project "PSSG Editor/PSSG Editor.csproj"
```

or by launching the produced `PF PSSG Editor.exe` from the build directory. Once launched, use **File → Open** to load one of the sample `.pssg` files.

