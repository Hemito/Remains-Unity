#!/usr/bin/env python3
"""Optional persistence acceptance at the editor's 4M-cell/seven-layer limit."""
from pathlib import Path
import hashlib,json,os,shutil,sys,tempfile,time
from playwright.sync_api import sync_playwright
url=sys.argv[1] if len(sys.argv)>1 else 'http://127.0.0.1:8181'
with tempfile.TemporaryDirectory(prefix='remains-large-room-') as temporary:
    file=Path(temporary)/'large.room.json'
    room={'format':'remains-room','version':1,'template':'large-max','size':{'w':2000,'h':2000},'cell':.8,'palette':{},
        'layers':{n:['.'*2000]*2000 for n in ['solid','back','ladder','platform','stairs','height','water']},'decor':[],'objects':[],'connectors':[],'options':{}}
    file.write_text(json.dumps(room,separators=(',',':')),encoding='utf-8')
    with sync_playwright() as p:
        browser=p.chromium.launch(executable_path=os.environ.get('CHROMIUM_PATH',shutil.which('chromium') or shutil.which('google-chrome')),headless=True,args=['--no-sandbox'])
        page=browser.new_page(viewport={'width':1600,'height':1000},accept_downloads=True)
        errors=[];page.on('pageerror',lambda e:errors.append(str(e)))
        page.goto(url,wait_until='networkidle');page.locator('canvas').wait_for()
        started=time.monotonic();page.locator('input[type=file]').first.set_input_files(str(file))
        page.wait_for_function("document.querySelector('input[aria-label=Название]')?.value==='large-max'",timeout=120000)
        # Await the actual commit. Returning a Promise from a polling predicate can
        # be mistaken for a truthy value; keep this loop inside awaited evaluate.
        page.evaluate("async()=>{for(let i=0;i<120;i++){const text=await roomEditor.loadDraft();if(text?.includes('large-max'))return;await new Promise(r=>setTimeout(r,1000));}throw new Error('Draft commit timed out');}")
        assert not page.get_by_role('alert').all_inner_texts()
        assert page.locator('canvas').evaluate('(c)=>c.width<=4800&&c.height<=4800')
        with page.expect_download(timeout=120000) as item:page.get_by_role('button',name='Скачать .room.json',exact=True).click(timeout=120000)
        assert hashlib.sha256(Path(item.value.path()).read_bytes()).digest()==hashlib.sha256(file.read_bytes()).digest()
        page.reload(wait_until='networkidle')
        page.wait_for_function("document.querySelector('input[aria-label=Название]')?.value==='large-max'",timeout=120000)
        assert not errors,errors
        print(f'PASS {file.stat().st_size} bytes / 4M cells / seven layers: open, committed IndexedDB, exact download, bounded canvas and reload ({time.monotonic()-started:.1f}s)')
        browser.close()
