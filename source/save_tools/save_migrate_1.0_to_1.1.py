import sys, msgpack
sys.path.insert(0,'/tmp/claude-0/scripts'); import savelib
src,dst=sys.argv[1],sys.argv[2]
raw,data=savelib.load(src)
top=msgpack.unpackb(data,raw=True,strict_map_key=False)
MAP={b'gun_main':[b'gun_xlarge',b'gun_verylarge',b'gun_large'], b'gun_sec':[b'gun_medium',b'gun_small']}
P=lambda o: msgpack.packb(o, use_bin_type=False)
def arrhdr(n): return P([])[:0] or (bytes([0x90|n]) if n<16 else (b'\xdc'+n.to_bytes(2,'big') if n<65536 else b'\xdd'+n.to_bytes(4,'big')))
u=msgpack.Unpacker(raw=True); u.feed(data)
assert u.read_array_header()==35
def elems():  # offsets of elements of the array at current position
    n=u.read_array_header(); out=[]
    for _ in range(n):
        a=u.tell(); u.skip(); out.append((a,u.tell()))
    return out
tops=[]
for i in range(35):
    a=u.tell(); u.skip(); tops.append((a,u.tell()))
edits=[]   # (start,end,newbytes)
# players: top[6]
u=msgpack.Unpacker(raw=True); u.feed(data[tops[6][0]:tops[6][1]]); base=tops[6][0]
nplayers=u.read_array_header()
for k in range(nplayers):
    st=u.tell(); ln=u.read_array_header(); offs=[]
    for j in range(ln):
        a=u.tell(); u.skip(); offs.append((a,u.tell()))
    rec=top[6][k]
    # field 38: list of [techType, mod]
    if len(rec)>38 and isinstance(rec[38],list) and any(isinstance(p,list) and p and p[0] in MAP for p in rec[38]):
        a,b=offs[38]; sub=data[base+a:base+b]
        uu=msgpack.Unpacker(raw=True); uu.feed(sub); n=uu.read_array_header(); parts=[]
        for _ in range(n):
            x=uu.tell(); uu.skip(); parts.append(sub[x:uu.tell()])
        newparts=[]
        for pb,pv in zip(parts,rec[38]):
            if isinstance(pv,list) and len(pv)==2 and pv[0] in MAP:
                # keep original float encoding: bytes after the string
                sl=len(P(pv[0])); hdr=pb[:1]; fbytes=pb[1+sl:]
                assert hdr==b"\x92" and pb[1:1+sl]==P(pv[0]), (k, pb[:30], pv)
                for nm in MAP[pv[0]]:
                    if not any(isinstance(q,list) and q and q[0]==nm for q in rec[38]):
                        newparts.append(b'\x92'+P(nm)+fbytes)
            else: newparts.append(pb)
        edits.append((base+a,base+b,arrhdr(len(newparts))+b''.join(newparts)))
    # field 39: list of techType names (priorities)
    if len(rec)>39 and isinstance(rec[39],list) and any(x in MAP for x in rec[39]):
        a,b=offs[39]; lst=[]
        for x in rec[39]:
            if x in MAP:
                for nm in MAP[x]:
                    if nm not in lst and nm not in rec[39]: lst.append(nm)
            elif x not in lst: lst.append(x)
        edits.append((base+a,base+b,P(lst)))
# ships: top[13] removed hull rename (field 10)
OLDH,NEWH=b'ca_1_italian_exp_japan_3mast',b'ca_1_italian_small_japan_3mast'
u=msgpack.Unpacker(raw=True); u.feed(data[tops[13][0]:tops[13][1]]); base=tops[13][0]
n=u.read_array_header()
for k in range(n):
    ln=u.read_array_header(); offs=[]
    for j in range(ln):
        a=u.tell(); u.skip(); offs.append((a,u.tell()))
    rec=top[13][k]
    for j,v in enumerate(rec):
        if v==OLDH:
            a,b=offs[j]; assert data[base+a:base+b]==P(OLDH); edits.append((base+a,base+b,P(NEWH)))
edits.sort()
out=bytearray(); pos=0
for a,b,nb in edits:
    assert a>=pos; out+=data[pos:a]+nb; pos=b
out+=data[pos:]; out=bytes(out)
t2=msgpack.unpackb(out,raw=True,strict_map_key=False)
# verify: only players' fields 38/39 and the hull string changed
assert all(t2[i]==top[i] for i in range(35) if i not in (6,13))
for k in range(len(top[6])):
    for j in range(len(top[6][k])):
        if j not in (38,39): assert t2[6][k][j]==top[6][k][j]
diff13=[(k,j) for k in range(len(top[13])) for j in range(len(top[13][k])) if t2[13][k][j]!=top[13][k][j]]
assert all(t2[13][k][j]==NEWH for k,j in diff13), diff13
def strs(o,acc):
    if isinstance(o,bytes): acc.add(o)
    elif isinstance(o,list): [strs(x,acc) for x in o]
    return acc
left=strs(t2,set()) & {b'gun_main',b'gun_sec',OLDH}
open(dst,'wb').write(savelib.pack(out))
r2,d2=savelib.load(dst); assert d2==out
print('edits',len(edits),'ship hull renames',len(diff13),'remaining old refs',left)
for k in range(len(top[6])):
    if top[6][k][38]!=t2[6][k][38]: print(' player',k,top[6][k][0][:20] if isinstance(top[6][k][0],bytes) else top[6][k][0], [p for p in t2[6][k][38] if p[0].startswith(b'gun_')])
    if top[6][k][39]!=t2[6][k][39]: print('  prio',t2[6][k][39])
