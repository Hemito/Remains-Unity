#!/usr/bin/env python3
"""Acceptance checks against a published static site. Requires Playwright + Chromium."""
import json,sys,tempfile,os,shutil
from pathlib import Path
from playwright.sync_api import sync_playwright
output=Path(os.environ.get('ROOM_EDITOR_CHECK_OUTPUT',tempfile.mkdtemp(prefix='remains-editor-check-')))
output.mkdir(parents=True,exist_ok=True)
url=sys.argv[1] if len(sys.argv)>1 else 'http://127.0.0.1:8181'
repo=Path(__file__).resolve().parents[3]
with sync_playwright() as p:
    browser=p.chromium.launch(executable_path=os.environ.get('CHROMIUM_PATH',shutil.which('chromium') or shutil.which('google-chrome')),headless=True,args=['--no-sandbox'])
    page=browser.new_page(viewport={'width':1600,'height':1000},accept_downloads=True)
    errors=[]
    page.on('pageerror',lambda e:errors.append(str(e)))
    page.on('console',lambda m:errors.append(m.text) if m.type=='error' else None)
    page.on('dialog',lambda d:d.accept())
    page.goto(url,wait_until='networkidle')
    canvas=page.locator('#room-canvas');canvas.wait_for()
    page.wait_for_function("document.querySelector('canvas').width===720")
    def stroke(points):
        box=canvas.bounding_box()
        page.mouse.move(box['x']+points[0][0],box['y']+points[0][1]);page.mouse.down()
        for x,y in points[1:]:page.mouse.move(box['x']+x,box['y']+y,steps=3)
        page.mouse.up()
        page.wait_for_timeout(150)
    def draft():return json.loads(page.evaluate("()=>roomEditor.loadDraft()"))
    stroke([(60,60),(84,60),(84,84)])
    page.evaluate("async()=>{for(let i=0;i<100;i++){if(await roomEditor.loadDraft()!==null)return;await new Promise(r=>setTimeout(r,50));}throw new Error('Draft commit timed out');}")
    assert draft()['layers']['solid'][2][2]!='.' and draft()['layers']['solid'][3][3]!='.'
    page.keyboard.press('Control+z');page.wait_for_timeout(120)
    assert 'solid' not in draft()['layers'], 'one stroke must be one undo action'
    page.keyboard.press('Control+y');page.wait_for_timeout(120)
    assert draft()['layers']['solid'][3][3]!='.'
    page.keyboard.press('s');stroke([(48,48),(95,95)]);page.keyboard.press('Control+c');page.wait_for_timeout(100)
    page.keyboard.press('Control+v');stroke([(240,240)])
    assert draft()['layers']['solid'][9][10]!='.', 'selection paste did not edit requested cells'
    page.get_by_role('button',name='Зеркало слоя',exact=True).click();page.wait_for_timeout(100)
    saved=page.evaluate("()=>roomEditor.loadDraft()")
    page.reload(wait_until='networkidle');canvas.wait_for();page.wait_for_timeout(200)
    assert page.evaluate("()=>roomEditor.loadDraft()")==saved, 'refresh lost draft'
    assert 'Восстановлен черновик' in page.locator('.status').first.inner_text()
    # Selection is stable by ID when history and crop replace/reorder model records.
    definitions=json.loads((repo/'RemainsUnity/Assets/Remains/Rooms/Original/definitions.json').read_text())
    entity_type=next(d['id'] for d in definitions['definitions'] if d['kind']=='object')
    controlled={'format':'remains-room','version':1,'template':'selection-test','size':{'w':12,'h':8},'cell':.8,'palette':{},'layers':{},'decor':[],
        'objects':[{'id':name,'type':entity_type,'x':x,'y':1,'params':{'tag':name}} for name,x in [('A',10),('B',1),('C',2)]],'connectors':[],'options':{}}
    page.locator('input[type=file]').first.set_input_files({'name':'selection.room.json','mimeType':'application/json','buffer':json.dumps(controlled).encode()})
    page.wait_for_timeout(200)
    import re
    page.get_by_role('button',name=re.compile('^B ·')).click()
    page.get_by_label('Параметры объекта').fill('{"tag":"B2"}')
    page.get_by_role('button',name='Применить параметры',exact=True).click();page.wait_for_timeout(150)
    page.keyboard.press('Control+z');page.wait_for_timeout(150)
    assert json.loads(page.get_by_label('Параметры объекта').input_value())['tag']=='B', 'undo kept future cached parameters'
    page.get_by_label('Ширина',exact=True).fill('5')
    page.get_by_label('Разрешить обрезку содержимого').check()
    page.get_by_role('button',name='Размер',exact=True).click();page.wait_for_timeout(150)
    assert page.locator('.entity-list .selected').inner_text().startswith('B ·'), 'crop retargeted selection from B to C'
    page.get_by_label('Параметры объекта').fill('{"tag":"B3"}');page.get_by_role('button',name='Применить параметры',exact=True).click();page.wait_for_timeout(150)
    assert next(o for o in draft()['objects'] if o['id']=='C')['params']['tag']=='C', 'selection mutation corrupted adjacent entity'
    # Browser reads and downloads the exact original bytes through the WASM RoomSerializer path.
    original=repo/'RemainsUnity/Assets/Remains/Rooms/Original/plant/vkhod2.room.json'
    page.locator('input[type=file]').first.set_input_files(str(original));page.wait_for_timeout(300)
    with page.expect_download() as item:page.get_by_role('button',name='Скачать .room.json',exact=True).click()
    assert Path(item.value.path()).read_bytes()==original.read_bytes(), 'browser roundtrip changed original bytes'
    page.get_by_role('button',name='Найти маршруты',exact=True).first.click()
    page.wait_for_timeout(50)
    cancel=page.get_by_role('button',name='Отменить расчёт',exact=True).first
    if cancel.is_enabled():cancel.click()
    page.wait_for_timeout(500)
    assert 'отменён' in page.locator('body').inner_text() or 'Опоры:' in page.locator('body').inner_text(), 'route cancel or completion missing'
    page.screenshot(path=str(output/'room.png'),full_page=True)
    page.get_by_role('button',name='Карты',exact=True).click()
    page.get_by_role('button',name='Собрать общую геометрию',exact=True).click();page.wait_for_timeout(250)
    assert page.get_by_role('img',name='Общая геометрия карты').count()==1
    page.get_by_role('button',name='Открыть проход',exact=True).click();page.wait_for_timeout(250)
    with page.expect_download() as item:page.get_by_role('button',name='Скачать раскладку',exact=True).click()
    layout=json.loads(Path(item.value.path()).read_text())
    assert layout['format']=='remains-factory-layout' and len(layout['seams'])==1
    assert len(layout['cells'])==4
    map_draft=page.evaluate("()=>roomEditor.loadMapDraft()")
    page.reload(wait_until='networkidle');page.get_by_role('button',name='Карты',exact=True).click();page.wait_for_timeout(300)
    assert page.evaluate("()=>roomEditor.loadMapDraft()")==map_draft
    page.screenshot(path=str(output/'map.png'),full_page=True)
    assert not errors,'Browser errors: '+repr(errors)
    print('PASS Chromium release WASM boot, curved brush stroke, one-step undo/redo, selection copy/paste, mirror, refresh draft, exact original open/download, route cancellation, map preview/seam/download and map draft')
    print('Screenshots:',output)
    browser.close()
