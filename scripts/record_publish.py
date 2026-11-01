"""Record an exact runtime allowlist; never recurse into demo/profile/session folders."""
import hashlib,json,os,shutil
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
BASE=ROOT/'.runtime/publish'
LOCALES={'cs','de','es','fr','it','ja','ko','pl','pt-BR','ru','tr','zh-Hans','zh-Hant'}

def record():
 licenses=BASE/'licenses';licenses.mkdir(exist_ok=True)
 nuget=Path(os.environ.get('NUGET_PACKAGES',ROOT/'.runtime/nuget'))
 for package,label in [('microsoft.netcore.app.runtime.win-x64','netcore'),('microsoft.windowsdesktop.app.runtime.win-x64','desktop')]:
  for name in (['LICENSE.TXT','THIRD-PARTY-NOTICES.TXT'] if label=='netcore' else ['LICENSE']):
   source=nuget/package/'9.0.22'/name
   if not source.is_file():raise RuntimeError('Missing publisher notice: '+package+'/'+name)
   shutil.copyfile(source,licenses/(label+'-'+('LICENSE.TXT' if name=='LICENSE' else name)))
 for name in ['wpf-THIRD-PARTY-NOTICES.TXT','winforms-THIRD-PARTY-NOTICES.TXT','provenance.json']:
  shutil.copyfile(ROOT/'licenses'/name,licenses/name)
 files=[]
 for path in BASE.iterdir():
  if path.is_file():
   if path.suffix.lower() not in {'.exe','.dll','.json','.config','.txt'}:raise RuntimeError('Unexpected runtime root file: '+path.name)
   files.append(path)
  elif path.name in LOCALES or path.name=='licenses':
   for child in path.rglob('*'):
    if child.is_file():
     if child.suffix.lower() not in {'.dll','.txt','.json'}:raise RuntimeError('Unexpected localized/runtime-license file')
     files.append(child)
  elif path.name not in {'.rea-notebook','demo'}:raise RuntimeError('Unexpected runtime folder: '+path.name)
 items=[]
 for path in sorted(files):
  if path.is_symlink() or not path.resolve().is_relative_to(BASE):raise RuntimeError('Unsafe runtime file')
  data=path.read_bytes();items.append({'name':path.relative_to(BASE).as_posix(),'size':len(data),'sha256':hashlib.sha256(data).hexdigest()})
 if not all(any(x['name']==name for x in items) for name in ['ResearchNotebook.exe','ResearchNotebook.Cli.exe','NotebookFixture.exe','Notebook.Core.dll']):raise RuntimeError('Incomplete native package')
 result={'self_contained':True,'platform':'Windows x64','runtime':'Microsoft.NETCore.App and Microsoft.WindowsDesktop.App 9.0.22','files':items,'excluded_owned_runtime_folders':['.rea-notebook','demo'],'publisher_licenses_included':True}
 (ROOT/'artifacts').mkdir(exist_ok=True)
 (ROOT/'artifacts/publish-manifest.json').write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
 print('Exact runtime allowlist:',len(items),'files')
 return result
if __name__=='__main__':record()
