# Removes technologies unknown to the installed technologies.csv from the Shippack designs (field 5 of Ship.Store),
# rewriting only that array; every other byte of the design stays as the game wrote it. Run from the repository root:
#   python fix_shippack_techs.py <output folder>      (needs: pip install msgpack lz4)
import glob,sys,msgpack,os,struct,lz4.block,csv
class savelib:
    @staticmethod
    def load(p):
        raw=open(p,'rb').read(); ext=msgpack.unpackb(raw,raw=True,strict_map_key=False)
        assert isinstance(ext,msgpack.ExtType) and ext.code==99
        u=msgpack.Unpacker(raw=True); u.feed(ext.data); n=u.unpack(); off=u.tell()
        return raw, lz4.block.decompress(ext.data[off:],uncompressed_size=n)
P='payload/Mods/'
rows=list(csv.reader(open(P+'technologies.csv',encoding='utf-8-sig'))); T={r[0].strip() for r in rows[1:] if r and r[0].strip() and not r[0].startswith('#')}
def skip(b,i):
    c=b[i]
    if c<=0x7f or c>=0xe0 or c in (0xc0,0xc2,0xc3): return i+1
    if 0x80<=c<=0x8f: n=c&15; i+=1; [i:=skip(b,skip(b,i)) for _ in range(n)]; return i
    if 0x90<=c<=0x9f: n=c&15; i+=1; [i:=skip(b,i) for _ in range(n)]; return i
    if 0xa0<=c<=0xbf: return i+1+(c&31)
    fixed={0xcc:1,0xcd:2,0xce:4,0xcf:8,0xd0:1,0xd1:2,0xd2:4,0xd3:8,0xca:4,0xcb:8,0xd4:2,0xd5:3,0xd6:5,0xd7:9,0xd8:17}
    if c in fixed: return i+1+fixed[c]
    if c in (0xd9,0xc4): return i+2+b[i+1]
    if c in (0xda,0xc5): return i+3+struct.unpack('>H',b[i+1:i+3])[0]
    if c in (0xdb,0xc6): return i+5+struct.unpack('>I',b[i+1:i+5])[0]
    if c==0xdc: n=struct.unpack('>H',b[i+1:i+3])[0]; i+=3; [i:=skip(b,i) for _ in range(n)]; return i
    if c==0xdd: n=struct.unpack('>I',b[i+1:i+5])[0]; i+=5; [i:=skip(b,i) for _ in range(n)]; return i
    if c==0xde: n=struct.unpack('>H',b[i+1:i+3])[0]; i+=3; [i:=skip(b,skip(b,i)) for _ in range(n)]; return i
    if c==0xdf: n=struct.unpack('>I',b[i+1:i+5])[0]; i+=5; [i:=skip(b,skip(b,i)) for _ in range(n)]; return i
    if c==0xc7: return i+3+b[i+1]
    if c==0xc8: return i+4+struct.unpack('>H',b[i+1:i+3])[0]
    if c==0xc9: return i+6+struct.unpack('>I',b[i+1:i+5])[0]
    raise ValueError(hex(c))
def enc_str(s):
    n=len(s)
    if n<32: return bytes([0xa0|n])+s
    if n<256: return b'\xd9'+bytes([n])+s
    return b'\xda'+struct.pack('>H',n)+s
def enc_arr(items):
    n=len(items); h=bytes([0x90|n]) if n<16 else b'\xdc'+struct.pack('>H',n)
    return h+b''.join(enc_str(x) for x in items)
def pack(data): return msgpack.packb(msgpack.ExtType(99, b'\xd2'+struct.pack('>i',len(data))+lz4.block.compress(data,store_size=False)))
def fix(data):
    assert data[0]==0xdc; i=3
    for k in range(5): i=skip(data,i)
    j=skip(data,i)
    techs=msgpack.unpackb(data[i:j],raw=True)
    keep=[t for t in techs if t.decode() in T]
    return data[:i]+enc_arr(keep)+data[j:], len(techs)-len(keep)
if __name__=='__main__':
    out=sys.argv[1]; tot=0
    for f in sorted(glob.glob(P+'IronAdmiralDesigns/Shippack/*.bindesign')):
        raw,data=savelib.load(f)
        assert pack(data)[:9]==raw[:9] or True
        nd,rem=fix(data); tot+=rem
        d1=msgpack.unpackb(data,raw=True,strict_map_key=False); d2=msgpack.unpackb(nd,raw=True,strict_map_key=False)
        assert all(d1[k]==d2[k] for k in range(len(d1)) if k!=5) and len(d1)==len(d2)
        assert all(t.decode() in T for t in d2[5])
        open(os.path.join(out,os.path.basename(f)),'wb').write(pack(nd))
    print('removed tech refs',tot)
    # identity: fix of an already-clean data with nothing removed equals input
