"""Package exact source/runtime allowlists and test the actual extracted EXEs."""
import hashlib,importlib.util,json,os,subprocess,sys,uuid,zipfile
from pathlib import Path
from record_publish import record
ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'releases';OUT.mkdir(exist_ok=True)

def digest(path):return hashlib.sha256(path.read_bytes()).hexdigest()
def write_json(path,value):path.write_text(json.dumps(value,indent=2)+'\n',encoding='utf-8')
def archive(path,entries):
 names=set()
 with zipfile.ZipFile(path,'w',zipfile.ZIP_DEFLATED,strict_timestamps=False) as z:
  for source,name in entries:
   if name in names or '..' in name.split('/') or name.startswith('/') or '\\' in name or source.is_symlink() or not source.resolve().is_relative_to(ROOT):raise RuntimeError('Unsafe or duplicate ZIP entry: '+name)
   names.add(name);z.write(source,name)
 with zipfile.ZipFile(path) as z:
  if z.testzip():raise RuntimeError('ZIP CRC failure')
 path.with_suffix(path.suffix+'.sha256').write_text(digest(path)+'  '+path.name+'\n',encoding='utf-8')
 return {'name':path.name,'size':path.stat().st_size,'sha256':digest(path),'entries':len(entries)}
def execute(exe,args):
 result=subprocess.run([str(exe),*map(str,args)],cwd=ROOT,timeout=30,capture_output=True,text=True,encoding='utf-8',errors='strict')
 if result.returncode:raise RuntimeError('Extracted executable failed: '+exe.name+'\n'+result.stdout+'\n'+result.stderr)
 return result.stdout

def privacy(path):
 spec=importlib.util.spec_from_file_location('rea_privacy',ROOT/'scripts/privacy_scan.py');module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
 markers=[str(ROOT),ROOT.as_posix()]
 parts=ROOT.parts
 for i,p in enumerate(parts[:-1]):
  if p.lower()=='users':
   prefix=Path(*parts[:i+2]);markers += [str(prefix),prefix.as_posix()]
 module.MARKERS.extend(x.lower() for x in markers)
 module.scan(path.read_bytes(),path.name)
 if module.HITS:raise RuntimeError('Private build-path markers found in release entries: '+repr(module.HITS))
 return {'passed':True,'files_and_nested_entries':module.COUNT,'PE_entries':module.PES,'marker_hits':[],'scope':'ASCII and UTF16LE current workspace and local user-prefix markers; recursive ZIP payloads'}

def main():
 tests=json.loads((ROOT/'artifacts/core-tests.json').read_text())
 gui=json.loads((ROOT/'artifacts/gui-tests.json').read_text())
 browser=json.loads((ROOT/'artifacts/browser-tests.json').read_text())
 if tests['passed']!=tests['total'] or tests['total']<35 or not gui['passed'] or not browser['passed']:raise RuntimeError('Required checks incomplete')
 for name,expected in tests['tested_source_files'].items():
  if digest(ROOT/name)!=expected:raise RuntimeError('Core test source evidence is stale: '+name)
 for name,expected in browser['tested_files'].items():
  if digest(ROOT/name)!=expected:raise RuntimeError('Browser test evidence is stale: '+name)
 publish=record()
 runtime_hashes={x['name']:x['sha256'] for x in publish['files']}
 for name,expected in {**tests['tested_fixture_files'],**gui['runtime_files']}.items():
  if runtime_hashes.get(name)!=expected:raise RuntimeError('Native GUI/fixture test evidence is stale: '+name)
 entries=[]
 for item in publish['files']:
  source=ROOT/'.runtime/publish'/item['name']
  if digest(source)!=item['sha256']:raise RuntimeError('Stale publish manifest')
  entries.append((source,item['name']))
 for name in ['README.md','NOTICE.md','LICENSE']:entries.append((ROOT/name,name))
 entries.append((ROOT/'.runtime/publish/demo/input.json','demo/input.json'))
 for name in ['plan-2.json','receipt-2.json','plan-3.json','receipt-3.json']:entries.append((ROOT/'docs/demo'/name,'example/'+name))
 binary=OUT/'ResearchNotebook-0.1.0-win-x64.zip'
 binary_info=archive(binary,entries)
 smoke=ROOT/'.runtime'/('release-smoke-'+uuid.uuid4().hex);smoke.mkdir()
 with zipfile.ZipFile(binary) as z:z.extractall(smoke)
 for item in publish['files']:
  if digest(smoke/item['name'])!=item['sha256']:raise RuntimeError('Extracted bytes differ')
 evidence=smoke/'new-evidence'
 execute(smoke/'ResearchNotebook.Cli.exe',['demo',smoke,evidence])
 for factor in [2,3]:execute(smoke/'ResearchNotebook.Cli.exe',['verify',evidence/('plan-'+str(factor)+'.json'),evidence/('receipt-'+str(factor)+'.json')])
 # CLI destination refusal must occur before a real rerun changes its output.
 output=smoke/'demo/result.json';before=digest(output)
 result=subprocess.run([str(smoke/'ResearchNotebook.Cli.exe'),'run',str(smoke),str(evidence/'plan-2.json'),str(evidence/'receipt-2.json')],cwd=ROOT,timeout=15,capture_output=True)
 if result.returncode!=2 or digest(output)!=before:raise RuntimeError('Existing receipt did not refuse before execution')
 # Preview reports a meaningful nonzero exit when declared input bytes drift.
 (smoke/'demo/input.json').write_text('{"Values":[1,2,3]}',encoding='utf-8')
 result=subprocess.run([str(smoke/'ResearchNotebook.Cli.exe'),'preview',str(smoke),str(evidence/'plan-2.json')],cwd=ROOT,timeout=15,capture_output=True)
 if result.returncode!=1:raise RuntimeError('Changed-input preview did not return refusal')
 # Restore exact synthetic input for the GUI click test.
 (smoke/'demo/input.json').write_bytes((ROOT/'.runtime/publish/demo/input.json').read_bytes())
 shot=smoke/'native.png';execute(smoke/'ResearchNotebook.exe',['--ui-self-test',shot])
 if not json.loads(shot.with_suffix('.png.json').read_text())['passed']:raise RuntimeError('Extracted GUI self-test report incomplete')
 smoke_report={'passed':True,'checks':['Exact extracted runtime file hashes match publication allowlist','Self-contained extracted CLI actually ran both fixture factors','Extracted CLI verified both full plan/receipt envelopes','Existing evidence destination refused before command launch','Changed-input CLI preview returned nonzero refusal','Extracted native GUI clicked Prepare/Preview/Run twice and confirmed changed output'],'native_GUI_checks':len(gui['checks']),'installed_Warp_used':False}
 write_json(ROOT/'artifacts/release-smoke.json',smoke_report)
 privacy_report=privacy(binary);write_json(ROOT/'artifacts/privacy-scan.json',privacy_report)
 tested={p.relative_to(ROOT).as_posix():digest(p) for base in ['src','tests'] for p in (ROOT/base).rglob('*') if p.is_file()}
 for name in ['Directory.Build.props','global.json','NuGet.Config']:tested[name]=digest(ROOT/name)
 verification={'passed':True,'core_checks':tests['total'],'native_GUI_checks':len(gui['checks']),'browser_checks':len(browser['checks']),'release_smoke':smoke_report,'binary_privacy_scan':privacy_report,'compiled_source_files':tested,'route':'Original independent C# native app and CLI; not a modified Warp client','limits':['Synthetic owned fixture only','No installed Warp/PTY/SSH/cloud account or remote execution','Parent exit only is confirmed; descendant termination remains best effort','Selected files/executable are hashed; complete runtime/dependency/OS state is not sealed','No OS sandbox; explicit trusted user commands run with normal OS permissions','Hashes prove internal consistency; authorship requires independent trust']}
 write_json(ROOT/'artifacts/verification.json',verification);write_json(ROOT/'docs/evidence.json',verification)
 roots=['.gitignore','LICENSE','NOTICE.md','README.md','Directory.Build.props','NuGet.Config','global.json','package.json','package-lock.json','public-files.json','licenses','src','tests','scripts','docs','investigation/COMPARISON.md','investigation/public-source/pin.json','artifacts/core-tests.json','artifacts/gui-tests.json','artifacts/browser-tests.json','artifacts/release-smoke.json','artifacts/privacy-scan.json','artifacts/verification.json','artifacts/publish-manifest.json']
 public=[]
 for name in roots:
  path=ROOT/name
  if name=='public-files.json':continue
  if path.is_dir():public.extend(p.relative_to(ROOT).as_posix() for p in path.rglob('*') if p.is_file() and not p.is_symlink())
  elif path.is_file():public.append(name)
  else:raise RuntimeError('Missing public file: '+name)
 public=sorted(set(public+['public-files.json']))
 write_json(ROOT/'public-files.json',public)
 source_entries=[(ROOT/name,'ResearchNotebook-source/'+name) for name in public]
 source=OUT/'ResearchNotebook-0.1.0-source.zip';source_info=archive(source,source_entries)
 source_privacy=privacy(source)
 summary={'binary':binary_info,'source':source_info,'source_privacy':source_privacy,'binary_privacy':privacy_report,'source_files':len(public)}
 write_json(ROOT/'artifacts/package-result.private.json',summary)
 print(json.dumps(summary,indent=2))
if __name__=='__main__':main()
