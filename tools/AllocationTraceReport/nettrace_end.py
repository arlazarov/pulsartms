"""End a nettrace stream the process could not close.

A process killed before EventPipe closes its session leaves a stream
without its end marker, and TraceEvent refuses it. This keeps every
complete object, drops a partial last one, and writes the end marker.

    python3 nettrace_end.py api.nettrace ended.nettrace
"""
import struct, sys
src, dst = sys.argv[1], sys.argv[2]
d = open(src, 'rb').read()
assert d[:8] == b'Nettrace'
n = struct.unpack_from('<i', d, 8)[0]
p = 12 + n
assert d[12:p] == b'!FastSerialization.1'
last = None
def obj(p):
    # BeginObject, type descriptor, payload, EndObject
    assert d[p] == 5; p += 1
    assert d[p] == 5 and d[p+1] == 1; p += 2
    p += 8  # version, minimum reader version
    ln = struct.unpack_from('<i', d, p)[0]; p += 4
    name = d[p:p+ln].decode(); p += ln
    assert d[p] == 6; p += 1
    if name == 'Trace':
        p += 48
    else:
        size = struct.unpack_from('<i', d, p)[0]; p += 4
        p += (4 - p % 4) % 4
        p += size
    if p >= len(d) or d[p] != 6:
        raise EOFError
    return p + 1, name
count = 0
try:
    while p < len(d) and d[p] == 5:
        p, name = obj(p)
        last = p; count += 1
except (EOFError, struct.error, IndexError, AssertionError):
    pass
open(dst, 'wb').write(d[:last] + b'\x01')
print(f'kept {count} objects, {last} of {len(d)} bytes')
