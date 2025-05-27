import argparse
from pathlib import Path

BXML1_MAGIC = b"\x00BXML"
BXML2_MAGIC = b"\x1a\x22Rr"

# XML files in the repository may already contain plain text XML.  Some of them
# start with a UTF-8 BOM.  When such files are given to the converter they
# should simply be passed through.
XML_BOMS = (b"\xef\xbb\xbf",)


def parse_bxml_type1(data: bytes):
    # heuristic: find start of root tag after header
    start = None
    for i in range(8, min(len(data), 20)):
        b = data[i]
        if 65 <= b <= 90 or 97 <= b <= 122:  # ASCII letter
            start = i
            break
    if start is None:
        raise ValueError("Cannot locate root tag in BXML")
    end = data.find(b"\x00", start)
    if end == -1:
        raise ValueError("Invalid BXML: no root terminator")
    root = data[start:end].decode('ascii')
    rest = data[end + 1:]
    parts = rest.split(b"\x00")
    elements = {}
    for i in range(0, len(parts) - 1, 2):
        key, value = parts[i], parts[i + 1]
        if not key or not value:
            break
        if not key.isascii() or not value.isascii():
            break
        elements[key.decode('ascii')] = value.decode('ascii')
    return root, elements

def parse_bxml_type2(data: bytes):
    """Very small heuristic parser for the simple RBXML variant.

    This format is largely undocumented.  For the tiny files shipped with this
    repository it appears to contain only a root tag without attributes or
    child elements.  We therefore just look for the first ASCII sequence after
    the header and treat it as the root element name.
    """

    # In observed files the root tag name starts at byte offset 24 and is null
    # terminated.  Fall back to a search if those bytes are not ASCII.
    if len(data) > 24 and data[24:25].isascii():
        end = data.find(b"\x00", 24)
        if end != -1:
            tag = data[24:end]
            if tag.isascii():
                return tag.decode("ascii"), {}

    # Fallback: scan for the first reasonably long ASCII sequence
    longest = b""
    current = b""
    for b in data[8:]:
        if (65 <= b <= 90) or (97 <= b <= 122) or b in (95, 45):
            current += bytes([b])
        else:
            if len(current) > len(longest):
                longest = current
            current = b""
        if b == 0:
            if len(current) > len(longest):
                longest = current
            current = b""
    if len(current) > len(longest):
        longest = current
    if not longest:
        raise ValueError("Cannot locate root tag in BXML type 2")
    return longest.decode("ascii"), {}


def parse_plain_xml(data: bytes) -> str:
    """Return the text of a plain XML file.

    The input may optionally start with a UTF-8 BOM.  The contents are assumed
    to be UTF-8 encoded and are returned unchanged (apart from stripping any
    BOM)."""
    for bom in XML_BOMS:
        if data.startswith(bom):
            return data[len(bom):].decode("utf-8")
    return data.decode("utf-8")

def bxml_to_xml(path: Path):
    data = path.read_bytes()
    if data.startswith(BXML1_MAGIC):
        root, elements = parse_bxml_type1(data)
        lines = [f"<{root}>"]
        for k, v in elements.items():
            lines.append(f"    <{k}>{v}</{k}>")
        lines.append(f"</{root}>")
        return "\n".join(lines)
    elif data.startswith(BXML2_MAGIC):
        root, elements = parse_bxml_type2(data)
        if elements:
            lines = [f"<{root}>"]
            for k, v in elements.items():
                lines.append(f"    <{k}>{v}</{k}>")
            lines.append(f"</{root}>")
            return "\n".join(lines)
        else:
            return f"<{root}/>"
    else:
        stripped = data.lstrip()
        if stripped.startswith(b"<") or any(data.startswith(bom) for bom in XML_BOMS):
            return parse_plain_xml(data)
        raise ValueError("Unknown BXML format")


def main():
    parser = argparse.ArgumentParser(description="Convert Codemasters BXML to XML")
    parser.add_argument("input", type=Path, help="Path to BXML file")
    parser.add_argument("output", type=Path, nargs="?", help="Output XML file")
    args = parser.parse_args()

    xml = bxml_to_xml(args.input)
    out_path = args.output or args.input.with_suffix(args.input.suffix + ".xml")
    out_path.write_text(xml, encoding="utf-8")


if __name__ == "__main__":
    main()
