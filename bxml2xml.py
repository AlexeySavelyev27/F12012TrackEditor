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

def parse_rbxml(data: bytes) -> str:
    """Parse the RBXML ("1A 22 52 72") binary XML format."""

    off = 8

    # string section container
    if data[off:off + 4] != b"\x17\x22Rr":
        raise ValueError("Invalid RBXML: missing string section")
    str_section_size = int.from_bytes(data[off + 4:off + 8], "little")
    off += 8

    # string data block
    if data[off:off + 4] != b"\x1d\x22Rr":
        raise ValueError("Invalid RBXML: missing string data block")
    str_data_size = int.from_bytes(data[off + 4:off + 8], "little")
    off += 8
    str_data = data[off:off + str_data_size]
    off += str_data_size

    # string index table
    if data[off:off + 4] != b"\x1e\x22Rr":
        raise ValueError("Invalid RBXML: missing string index table")
    index_size = int.from_bytes(data[off + 4:off + 8], "little")
    off += 8
    index_table = data[off:off + index_size]
    off += index_size

    # build string list
    indices = [int.from_bytes(index_table[i:i + 4], "little")
               for i in range(0, index_size, 4)]
    indices.append(len(str_data))
    strings = [
        str_data[indices[i]:indices[i + 1]].split(b"\x00", 1)[0].decode("ascii")
        for i in range(len(indices) - 1)
    ]

    # node table
    if data[off:off + 4] != b"\x1b\x22Rr":
        raise ValueError("Invalid RBXML: missing node table")
    node_size = int.from_bytes(data[off + 4:off + 8], "little")
    off += 8
    node_data = data[off:off + node_size]
    off += node_size
    nodes = [
        tuple(int.from_bytes(node_data[i + j:i + j + 4], "little")
              for j in range(0, 24, 4))
        for i in range(0, node_size, 24)
    ]

    # attribute table
    if off < len(data):
        if data[off:off + 4] != b"\x1c\x22Rr":
            raise ValueError("Invalid RBXML: missing attribute table")
        attr_size = int.from_bytes(data[off + 4:off + 8], "little")
        off += 8
        attr_data = data[off:off + attr_size]
        attr_records = [
            tuple(int.from_bytes(attr_data[i + j:i + j + 4], "little")
                  for j in range(0, 16, 4))
            for i in range(0, attr_size, 16)
        ]
    else:
        attr_records = []

    def build_xml(idx: int, depth: int = 0) -> str:
        tag_idx, _unused, attr_count, attr_offset, child_count, first_child = nodes[idx]
        tag = strings[tag_idx]

        attrs = {}
        if attr_count:
            pair_index = attr_offset // 2
            pair_count = (attr_count + 1) // 2
            for i in range(pair_index, pair_index + pair_count):
                n1, v1, n2, v2 = attr_records[i]
                if len(attrs) < attr_count:
                    attrs[strings[n1]] = strings[v1]
                if len(attrs) < attr_count:
                    attrs[strings[n2]] = strings[v2]

        indent = "  " * depth
        attr_str = "".join(f" {k}=\"{v}\"" for k, v in attrs.items())

        if child_count == 0:
            return f"{indent}<{tag}{attr_str} />"

        children = [build_xml(first_child + i, depth + 1) for i in range(child_count)]
        inner = "\n".join(children)
        return f"{indent}<{tag}{attr_str}>\n{inner}\n{indent}</{tag}>"

    return build_xml(0)


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
        return parse_rbxml(data)
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
