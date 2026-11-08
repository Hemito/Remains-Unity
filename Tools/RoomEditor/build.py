#!/usr/bin/env python3
"""Prepare public assets, run native checks, publish a static site; never deploy it."""
from pathlib import Path
import argparse,os,subprocess,sys
base=Path(__file__).resolve().parent
p=argparse.ArgumentParser(description=__doc__)
p.add_argument('--dotnet',default='dotnet')
p.add_argument('--output',type=Path,default=base/'artifacts')
p.add_argument('--skip-checks',action='store_true')
a=p.parse_args()
env=dict(os.environ)
env.setdefault('DOTNET_CLI_TELEMETRY_OPTOUT','1')
subprocess.run([sys.executable,str(base/'prepare-assets.py')],check=True)
if not a.skip_checks:
    subprocess.run([a.dotnet,'run','--project',str(base/'Checks/Checks.csproj'),'--',str(base.parent.parent/'RemainsUnity/Assets/Remains/Rooms/Original')],env=env,cwd=base,check=True)
subprocess.run([a.dotnet,'publish',str(base/'Web/Web.csproj'),'-c','Release','-o',str(a.output.resolve())],env=env,cwd=base,check=True)
print('Static site:',a.output.resolve()/'wwwroot')
