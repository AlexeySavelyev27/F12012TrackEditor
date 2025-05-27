import argparse
from pathlib import Path

BXML1_MAGIC = b"\x00BXML"
BXML2_MAGIC = b"\x1a\x22Rr"


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
        raise NotImplementedError("BXML type 2 not supported yet")
    else:
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
