"""构建仅依赖 Windows .NET Framework 的本机 MCP 标准输入输出入口。"""
from pathlib import Path
import argparse
import os
import subprocess

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--output', type=Path, required=True)
args = parser.parse_args()
root = Path(__file__).parent
compiler = Path(os.environ.get('WINDIR', 'C:/Windows'))/'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
args.output.mkdir(parents=True, exist_ok=True)
target = args.output/'InfantGod.Mcp.exe'
subprocess.run([str(compiler), '/nologo', '/optimize+', '/target:exe', '/platform:x64', '/out:'+str(target),
               '/r:System.dll', '/r:System.Core.dll', '/r:System.Web.Extensions.dll', str(root/'Program.cs')], check=True)
(args.output/'InfantGod.Mcp.exe.config').write_text('<?xml version="1.0" encoding="utf-8"?><configuration><startup><supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.8"/></startup></configuration>', encoding='utf-8')
print('已生成本机 MCP 入口：'+str(target))
