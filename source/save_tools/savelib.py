import msgpack, lz4.block
def load(p):
    raw=open(p,'rb').read()
    ext=msgpack.unpackb(raw, raw=True, strict_map_key=False)
    assert isinstance(ext,msgpack.ExtType) and ext.code==99
    u=msgpack.Unpacker(raw=True); u.feed(ext.data); n=u.unpack(); off=u.tell()
    data=lz4.block.decompress(ext.data[off:], uncompressed_size=n)
    return raw,data
def pack(data):
    comp=msgpack.packb(len(data))+lz4.block.compress(data,store_size=False)
    return msgpack.packb(msgpack.ExtType(99,comp))
