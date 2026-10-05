"""构建嵌入游戏客户区的 WebView2 宿主。"""
from pathlib import Path
import argparse,os,io,shutil,subprocess,urllib.request,zipfile

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--output',type=Path,required=True)
args=parser.parse_args()
root=Path(__file__).parent
deps=root/'deps';deps.mkdir(exist_ok=True)
version='1.0.4191.47'
files={
 'lib/net462/Microsoft.Web.WebView2.Core.dll':'Microsoft.Web.WebView2.Core.dll',
 'lib/net462/Microsoft.Web.WebView2.WinForms.dll':'Microsoft.Web.WebView2.WinForms.dll',
 'runtimes/win-x64/native/WebView2Loader.dll':'WebView2Loader.dll',
 'LICENSE.txt':'WebView2-LICENSE.txt',
}
if not all((deps/name).exists() for name in files.values()):
    url=f'https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/{version}/microsoft.web.webview2.{version}.nupkg'
    with zipfile.ZipFile(io.BytesIO(urllib.request.urlopen(url,timeout=30).read())) as package:
        for source,target in files.items():(deps/target).write_bytes(package.read(source))
compiler=Path(os.environ.get('WINDIR','C:/Windows'))/'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if not compiler.exists():raise SystemExit('需要 Windows .NET Framework 4.8 编译器。')
args.output.mkdir(parents=True,exist_ok=True)
target=args.output/'Graywill.InfantGod.WebHost.exe'
command=[str(compiler),'/nologo','/optimize+','/target:winexe','/platform:x64','/out:'+str(target),
 '/r:System.dll','/r:System.Core.dll','/r:System.Drawing.dll','/r:System.Windows.Forms.dll','/r:System.Web.Extensions.dll',
 '/r:'+str(deps/'Microsoft.Web.WebView2.Core.dll'),'/r:'+str(deps/'Microsoft.Web.WebView2.WinForms.dll'),str(root/'Program.cs')]
subprocess.run(command,check=True)
for name in files.values():shutil.copy2(deps/name,args.output/name)
(args.output/'Graywill.InfantGod.WebHost.exe.config').write_text('<?xml version="1.0" encoding="utf-8"?><configuration><startup useLegacyV2RuntimeActivationPolicy="true"><supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.8"/></startup></configuration>',encoding='utf8')
print('已生成 WebView2 宿主：'+str(target))
