#!/usr/bin/env python3
"""Shader-equation/pixel acceptance for C# command rasterization, WebGL2 + CPU fallback."""
import base64,io,json,math,os,shutil,sys
from PIL import Image
from playwright.sync_api import sync_playwright
url=sys.argv[1] if len(sys.argv)>1 else 'http://127.0.0.1:8181'
native=json.load(open(sys.argv[2],encoding='utf-8')) if len(sys.argv)>2 else None
if native is not None:assert native['passed'] and native['colorSpace']=='Linear' and native['width']==32 and all(len(f['pixels'])==1024 for f in native['frames'])
def png(width,height,pixels):
    img=Image.new('RGBA',(width,height));img.putdata(pixels);out=io.BytesIO();img.save(out,format='PNG');return 'data:image/png;base64,'+base64.b64encode(out.getvalue()).decode()
def solid(r,g,b,a=255):return png(1,1,[(r,g,b,a)])
def command(**values):
    c=dict(kind='sprite',image=None,texture=None,x=0,y=0,width=1,height=1,originX=0,originY=0,scaleX=1,scaleY=1,alpha=1,eraseAffected=False,blend='normal');c.update(values);return c
def linear(v):return v/12.92 if v<=.04045 else ((v+.055)/1.055)**2.4
def srgb(v):return 12.92*v if v<=.0031308 else 1.055*max(0,v)**(1/2.4)-.055
def check(actual,expected,label,tolerance=3):
    assert all(abs(a-e)<=tolerance for a,e in zip(actual[:3],expected)),(label,actual,expected)
with sync_playwright() as p:
    browser=p.chromium.launch(executable_path=os.environ.get('CHROMIUM_PATH',shutil.which('chromium') or shutil.which('google-chrome')),headless=True,args=['--no-sandbox'])
    page=browser.new_page();errors=[];page.on('pageerror',lambda e:errors.append(str(e)))
    page.goto(url,wait_until='networkidle');page.locator('canvas').wait_for()
    def render(commands,points,*,erases=(),density=8,pan=0,cpu=False):
        plan=dict(commands=commands,erases=list(erases),texelsPerMeter=density,farBackground=None)
        return page.evaluate('''async data=>{
            let canvas,stats;
            for(let i=0;i<150;i++){
                canvas=remainsRoomRenderer.render(data.plan,{width:32,height:32,zoom:8,cell:1,panX:data.pan,panRow:0},()=>{},data.cpu);
                stats=remainsRoomRenderer.stats();if(stats.missing===0)break;await new Promise(r=>setTimeout(r,20));
            }
            const copy=document.createElement('canvas');copy.width=32;copy.height=32;const ctx=copy.getContext('2d',{willReadFrequently:true});ctx.drawImage(canvas,0,0);
            return {stats,pixels:data.points.map(p=>Array.from(ctx.getImageData(p[0],p[1],1,1).data))};
        }''',dict(plan=plan,points=points,pan=pan,cpu=cpu))
    results={}
    for cpu in [False,True]:
        mode='CPU' if cpu else 'WebGL2';results[mode]={}
        pattern=png(3,1,[(255,0,0,255),(0,255,0,255),(0,0,255,255)])
        fills=[command(kind='fill',texture=pattern,x=x,width=1,height=4) for x in range(4)]
        sample=render(fills,[(1,10),(8,10),(11,10)],cpu=cpu)
        if not cpu:assert sample['stats']['backend']=='webgl2',sample['stats']
        for name,pixel in zip(['phase-x1','phase-x8','phase-x11'],sample['pixels']):results[mode][name]=pixel
        check(sample['pixels'][0],[0,255,0],mode+' bitmap texel');check(sample['pixels'][1],[0,0,255],mode+' continuous cell phase');check(sample['pixels'][2],[0,0,255],mode+' repeated phase')
        panned=render(fills,[(0,10)],pan=1,cpu=cpu);check(panned['pixels'][0],[0,0,255],mode+' room-origin pan phase')
        mask=png(2,2,[(255,255,255,255),(255,255,255,0),(255,255,255,0),(255,255,255,0)])
        base=command(kind='fill',texture=solid(0,0,255),width=4,height=4)
        masked=command(kind='material',image=mask,texture=solid(255,0,0),x=1,y=1)
        sample=render([base,masked],[(8,8),(15,15)],cpu=cpu);check(sample['pixels'][0],[255,0,0],mode+' mask registration');check(sample['pixels'][1],[0,0,255],mode+' transparent mask')
        flipped=dict(masked,x=2,scaleX=-1);sample=render([base,flipped],[(8,8),(15,8)],cpu=cpu);check(sample['pixels'][0],[0,0,255],mode+' flipped mask transparent');check(sample['pixels'][1],[255,0,0],mode+' flipped mask opaque')
        background=command(kind='fill',texture=solid(255,0,0),width=4,height=4,eraseAffected=True)
        erases=[command(image=solid(255,255,255,a),x=.5,y=2.5,width=1,height=.5) for a in [128,200]]
        front=command(kind='fill',texture=solid(0,255,0),x=1,y=2.5,width=.25,height=.25)
        sample=render([base,background,front],[(6,22),(6,6),(8,20)],erases=erases,cpu=cpu)
        expected=[round(srgb(1-200/255)*255),0,round(srgb(200/255)*255)]
        check(sample['pixels'][0],expected,mode+' MAX erase alpha and lower-Y orientation');check(sample['pixels'][1],[255,0,0],mode+' erase stays in its world rectangle');check(sample['pixels'][2],[0,255,0],mode+' foreground unaffected by erase')
        for blend in ['normal','multiply','overlay']:
            a=.5;dst=[80,120,160];src=[200,60,30]
            c=command(image=solid(*src),width=4,height=4,alpha=a,blend=blend)
            sample=render([command(kind='fill',texture=solid(*dst),width=4,height=4),c],[(12,12)],cpu=cpu)
            expected=[]
            for d,s in zip(dst,src):
                d,s=linear(d/255),linear(s/255)
                value=d*(1-a)+s*a if blend=='normal' else d*(1-a+s*a) if blend=='multiply' else 2*d*(.5*(1-a)+s*a)
                expected.append(round(min(1,srgb(value))*255))
            check(sample['pixels'][0],expected,mode+' linear '+blend)
            results[mode]['blend-'+str(['normal','multiply','overlay'].index(blend))]=sample['pixels'][0]
        print('PASS',mode,'continuous texture phase/pan, real alpha masks/flips, MAX background erase/orientation, foreground isolation, linear normal/multiply/2x-overlay')
    if native is not None:
        for mode,samples in results.items():
            for sample in native['samples']:
                expected=[round(srgb(sample['color'][c])*255) for c in ['r','g','b']]
                check(samples[sample['name']],expected,mode+' actual Unity '+sample['name'],2)
            print('PASS',mode,'matches actual Unity',native['version'],native['backend'],'RenderTexture readback within2 sRGB byte values')
    assert not errors,errors
    browser.close()
