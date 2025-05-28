import struct
import sys


def read_node(f, indent=0, limit=None):
    start = f.tell()
    if limit is not None and start >= limit:
        return

    try:
        name_len_data = f.read(4)
        if len(name_len_data) < 4:
            return
        name_len = struct.unpack('>I', name_len_data)[0]
        name = f.read(name_len).decode('ascii', errors='replace')
        flags, child_count = struct.unpack('>II', f.read(8))
        print('  ' * indent + f"{name} (flags={flags}, children={child_count})")

        for _ in range(child_count):
            read_node(f, indent + 1, limit)
    except Exception as e:
        print('Error reading node at', hex(start), e)


def main():
    if len(sys.argv) != 2:
        print("Usage: python3 pssg_extractor.py <file.pssg>")
        return

    path = sys.argv[1]
    with open(path, 'rb') as f:
        sig = f.read(4)
        if sig != b'PSSG':
            print("Invalid signature:", sig)
            return

        # Read three big-endian 32-bit integers
        size, str_table_off, root_off = struct.unpack('>III', f.read(12))
        print(f"File size: {size}")
        print(f"String table offset: {str_table_off}")
        print(f"Root node offset: {root_off}")

        try:
            f.seek(str_table_off)
            strings = []
            for _ in range(5):
                chunk = []
                while True:
                    ch = f.read(1)
                    if not ch or ch == b'\x00':
                        break
                    chunk.append(ch)
                if not chunk and not ch:
                    break
                strings.append(b''.join(chunk).decode('utf-8', errors='replace'))
                if not ch:
                    break
            if strings:
                print("Example strings:")
                for s in strings:
                    print("  " + s)
        except Exception as e:
            print("Could not read string table:", e)

        print("\nNode hierarchy:")
        f.seek(root_off)
        read_node(f, 0, str_table_off)


if __name__ == '__main__':
    main()
