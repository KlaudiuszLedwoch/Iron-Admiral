import sys,csv,io,collections
B,O,N=sys.argv[1:4]; files=sys.argv[4:]
def load(p):
    txt=open(p,encoding='utf-8-sig',errors='replace').read()
    rows=list(csv.reader(io.StringIO(txt)))
    hdr=None; data=collections.OrderedDict(); order=[]
    for r in rows:
        if not r: continue
        if r[0].startswith('@'): hdr=r; continue
        if r[0].startswith('#') : continue
        k=r[0]
        if k in data: k=k+'#dup'+str(len(order))
        data[k]=r; order.append(k)
    return hdr,data
def num(a,b):
    try: return abs(float(a.replace(',',''))-float(b.replace(',','')))<1e-9
    except: return False
for f in files:
    hb,db=load(f'{B}/{f}'); ho,do=load(f'{O}/{f}'); hn,dn=load(f'{N}/{f}')
    cols=[c for c in hb if c]  # named columns of base
    def idx(h): return {c:i for i,c in enumerate(h) if c}
    ib,io_,in_=idx(hb),idx(ho),idx(hn)
    print(f'===== {f}  rows base/ours/new = {len(db)}/{len(do)}/{len(dn)}')
    print('  cols only in new:',[c for c in hn if c and c not in ib],' cols dropped in new:',[c for c in hb if c and c not in in_])
    added=[k for k in dn if k not in db]; removed=[k for k in db if k not in dn]
    print(f'  NEW rows added {len(added)}: {added[:40]}')
    print(f'  NEW rows removed {len(removed)}: {removed[:40]}')
    oa=[k for k in do if k not in db]; orm=[k for k in db if k not in do]
    print(f'  OURS rows added {len(oa)}: {oa[:20]}; OURS removed {len(orm)}: {orm[:20]}')
    ours_only=new_only=same=0; conf=[]; newonly=collections.Counter(); oursonly=collections.Counter(); trivial=0
    for k,rb in db.items():
        ro=do.get(k); rn=dn.get(k)
        if ro is None or rn is None: continue
        for c in cols:
            if c not in in_ or c not in io_: continue
            g=lambda r,i: r[i] if i<len(r) else ''
            vb,vo,vn=g(rb,ib[c]),g(ro,io_[c]),g(rn,in_[c])
            if vo==vb and vn==vb: continue
            if vn!=vb and num(vn,vb): 
                if vo==vb: trivial+=1; continue
                vn=vb  # treat as unchanged
            if vo!=vb and vn==vb: ours_only+=1; oursonly[c]+=1
            elif vo==vb and vn!=vb: new_only+=1; newonly[c]+=1
            elif vo==vn or num(vo,vn): same+=1
            else: conf.append((k,c,vb,vo,vn))
    print(f'  cells: ours-only {ours_only}, new-only {new_only} (+{trivial} trivial reformat), both-same {same}, CONFLICT {len(conf)}')
    print('  ours-only by column:',dict(oursonly.most_common(20)))
    print('  new-only by column:',dict(newonly.most_common(30)))
    for c in conf: print('   CONFLICT',c)
