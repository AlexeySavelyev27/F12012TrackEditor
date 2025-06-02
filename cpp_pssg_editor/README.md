# PSSG Editor C++

This folder contains a minimal C++ port of the `pssg_editor.py` tool.
The project uses Qt for the GUI and can be opened with Microsoft Visual Studio
(2019 or newer) with the **Qt Visual Studio Tools** extension installed.

The implementation is simplified compared to the Python original but mirrors the
core logic: parsing PSSG files, displaying nodes in a tree view, showing and
editing attributes and binary data, and saving the file back.

## Building
1. Install [Qt](https://www.qt.io/download) and the Qt VS Tools extension.
2. Open `PSSGEditor.sln` in Visual Studio.
3. Build and run.

The solution contains one Qt Widgets application project: `PSSGEditor`.
