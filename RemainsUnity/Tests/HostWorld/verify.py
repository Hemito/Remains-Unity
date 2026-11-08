#!/usr/bin/env python3
"""Run existing production graphical probes as two isolated native Player processes in both host modes."""
import argparse, datetime, hashlib, json, os, pathlib, subprocess, time

p=argparse.ArgumentParser()
p.add_argument('--player',required=True,type=pathlib.Path)
p.add_argument('--output',required=True,type=pathlib.Path)
p.add_argument('--modes',nargs='+',choices=['on','off','default'],default=['on','off'])
p.add_argument('--scenarios',nargs='+',default=['appearance','door','melee','grenade','revival','telekinesis','telekinesis-container','telekinesis-pause','grenade-telekinesis','grenade-telekinesis-expire','projectile-handoff','npc','container','pause','prop-body','shortcuts'])
p.add_argument('--port',type=int,default=19440)
p.add_argument('--expect-default',choices=['on','off'])
p.add_argument('--timeout',type=int,default=180)
p.add_argument('--windowed',action='store_true',help='Use a normal graphical window (Linux batchmode can expose startup 640x480)')
p.add_argument('--wait-host-admission',action='store_true',help='Delay guest until local host admission; use only with probes whose startup budget permits sequential loading')
a=p.parse_args();a.player=a.player.resolve();a.output=a.output.resolve();a.output.mkdir(parents=True,exist_ok=True)
if not a.player.is_file():p.error('Player does not exist')
if os.name=='posix' and not os.environ.get('DISPLAY') and not os.environ.get('WAYLAND_DISPLAY'):p.error('Graphical Player requires DISPLAY or WAYLAND_DISPLAY')
assembly=a.player.parent/(a.player.stem+'_Data')/'Managed/Assembly-CSharp.dll'
metadata={'utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'player':str(a.player),'assembly_sha256':hashlib.sha256(assembly.read_bytes()).hexdigest(),'runs':[]}
(a.output/'summary.json').write_text(json.dumps(metadata,ensure_ascii=False,indent=2))
for mode in a.modes:
 for scenario in a.scenarios:
  directory=a.output/(scenario+'-'+mode)
  directory.mkdir(exist_ok=False)
  port=a.port+len(metadata['runs']);processes=[];streams=[];reports=[]
  try:
   for index in range(2):
    role='host' if index==0 else 'client';character='session-host' if index==0 else 'session-client-1'
    report=directory/(character+'-report.json');reports.append(report)
    probe=scenario+'-'+role
    if scenario in ['contact-audio-props','contact-audio-targets','prop-body','door','shortcuts']:
     probe=scenario.replace('contact-audio-','contact-audio-frames-')+'-'+role if scenario.startswith('contact-audio-') else scenario+'-frames-'+role
    width,height=(1920,1080) if scenario.startswith('contact-audio-') or scenario=='prop-body' else (1280,720)
    command=[str(a.player),*(['-force-glcore'] if os.name=='posix' else []),'-job-worker-count','2','-screen-width',str(width),'-screen-height',str(height),'-screen-fullscreen','0','-logFile',str(directory/(character+'.log')),'--role',role,'--character',character,'--save-dir',str(directory/(character+'-save')),'--smoke',probe,'--probe-peers','2','--probe-fps','60','--report',str(report),'--port',str(port)]
    if not a.windowed:command.insert(1,'-batchmode')
    if mode!='default':command+=['--host-self-ticks',mode]
    (directory/(character+'-command.json')).write_text(json.dumps(command,ensure_ascii=False,indent=2))
    stream=(directory/(character+'-stdout.log')).open('w');streams.append(stream)
    processes.append(subprocess.Popen(command,stdout=stream,stderr=subprocess.STDOUT))
    if index==0 and not a.wait_host_admission:time.sleep(2)
    if index==0 and a.wait_host_admission:
     # A cold Linux fixture may take longer than KCP's connect timeout. Start the guest after the host's own
     # admission/geometry preparation, rather than racing an arbitrary two-second delay.
     ready_deadline=time.monotonic()+90;host_log_path=directory/(character+'.log')
     while time.monotonic()<ready_deadline and processes[0].poll() is None:
      host_text=host_log_path.read_text(errors='replace') if host_log_path.exists() else ''
      if 'Подключён хост:' in host_text or 'Подключён выбранный персонаж:' in host_text:break
      time.sleep(.25)
     else:raise RuntimeError('Host did not complete its local admission before starting the guest')
   deadline=time.monotonic()+a.timeout
   while any(process.poll() is None for process in processes) and time.monotonic()<deadline:time.sleep(.5)
   for process in processes:
    if process.poll() is None:process.terminate()
   for process in processes:
    try:process.wait(timeout=10)
    except subprocess.TimeoutExpired:process.kill();process.wait()
   results=[]
   for role,report,process in zip(['host','client'],reports,processes):
    data=json.loads(report.read_text()) if report.exists() else {'success':False,'detail':'Missing report'}
    data.update(role=role,exit_code=process.returncode)
    results.append(data)
   host_log=(directory/'session-host.log').read_text(encoding='utf-8',errors='replace')
   observed='off' if 'Режим хоста: один мир' in host_log else 'on' if 'Режим хоста: самоотправка' in host_log else 'unknown'
   passed=all(r.get('success') and r['exit_code']==0 for r in results) and (mode!='default' or not a.expect_default or observed==a.expect_default)
   result={'mode':mode,'observed_mode':observed,'scenario':scenario,'success':passed,'directory':str(directory),'reports':results}
  except Exception as error:
   result={'mode':mode,'scenario':scenario,'success':False,'error':str(error),'directory':str(directory)}
  finally:
   for process in processes:
    if process.poll() is None:process.kill();process.wait()
   for stream in streams:stream.close()
  metadata['runs'].append(result)
  (a.output/'summary.json').write_text(json.dumps(metadata,ensure_ascii=False,indent=2))
  print(json.dumps({'mode':mode,'scenario':scenario,'success':result['success'],'directory':str(directory)}),flush=True)
raise SystemExit(0 if all(r['success'] for r in metadata['runs']) else 1)
