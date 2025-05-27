# F12012TrackEditor
A tool for editing existing and creating new tracks for F1 2012
See [docs/file-format-map.md](docs/file-format-map.md) for a description of the
included Melbourne track files.

## Utilities

`bxml2xml.py` converts Codemasters BXML files to regular XML.  It can also
detect and pass through normal XML files. Usage:

```bash
python3 bxml2xml.py <input.bxml> [output.xml]
```

The tool handles the simple BXML variant starting with `00 42 58 4D` and also
fully supports RBXML files with the header `1A 22 52 72`.  RBXML files are
parsed using their string, node and attribute tables to reconstruct the complete
XML tree.  If the input already contains plain XML (with or without a UTF-8
BOM) it is returned unchanged.

`pssg_info.py` reads the header of a PSSG archive and shows basic
information about the file:

```bash
python3 pssg_info.py melbourne/objects.pssg
```
