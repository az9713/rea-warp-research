"""Read-only archive/PE privacy scan for known local build-path markers."""
import gzip,hashlib,io,json,sys,tarfile,zipfile
from pathlib import Path
MARKERS=[]
HITS=[];COUNT=0;PES=0

def scan(data,name,depth=0):
 global COUNT,PES
 COUNT+=1
 if len(data)>300_000_000:raise RuntimeError('Scan entry too large')
 found=False
 for marker in MARKERS:
  if marker.encode() in data.lower() or marker.encode('utf-16le') in data.lower():found=True
 if found:HITS.append(name)
 if data[:2]==b'MZ':PES+=1
 if depth>=5:return
 if data[:4]==b'PK\x03\x04':
  with zipfile.ZipFile(io.BytesIO(data)) as z:
   if sum(i.file_size for i in z.infolist())>1_500_000_000:raise RuntimeError('Archive limit')
   for i in z.infolist():
    if not i.is_dir():scan(z.read(i),name+'!'+i.filename,depth+1)
 elif data[:2]==b'\x1f\x8b':
  plain=gzip.decompress(data)
  try:
   with tarfile.open(fileobj=io.BytesIO(plain)) as t:
    for i in t:
     if i.isfile():scan(t.extractfile(i).read(),name+'!'+i.name,depth+1)
  except tarfile.ReadError:scan(plain,name+'!gzip',depth+1)

def main():
 import argparse
 parser=argparse.ArgumentParser()
 parser.add_argument('--marker',action='append',required=True)
 parser.add_argument('paths',nargs='+')
 args=parser.parse_args()
 MARKERS.extend(x.lower() for x in args.marker)
 paths=[Path(x) for x in args.paths]
 for path in paths:scan(path.read_bytes(),path.name)
 print(json.dumps({'files_and_nested_entries':COUNT,'PE_entries':PES,'marker_hits':HITS,'passed':not HITS},indent=2))
 return 0 if not HITS else 2
if __name__=='__main__':raise SystemExit(main())
