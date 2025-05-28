import struct
import sys


def main():
    if len(sys.argv) != 2:
        print("Usage: python3 pssg_info.py <file.pssg>")
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

        # Try to read a few strings from the string table
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


if __name__ == '__main__':
    main()
