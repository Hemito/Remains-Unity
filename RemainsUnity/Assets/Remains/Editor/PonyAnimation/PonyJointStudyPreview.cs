using System;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Remains.Content;
using UnityEngine;

namespace Remains.Editor
{
    public static class PonyJointStudyPreview
    {
        private const int Width=560,Height=520;
        private static readonly (string Id,string Label,bool Loop)[] Settings={
            ("walk","Ходьба",true),("crouchLower","Приседание",false),
            ("crouchRise","Поднятие",false),("climb","Лестница",true)};
        private static readonly string[] Outfits={"","armor.antirad"};
        public static void Write(GodotRigPilotData rig,PonyJointStudy.Connection[] connections,string output)
        {
            PonyAnimationData.Load();
            var world=MeasureWorld();var clips=new JArray();
            using(var preview=new PonyAnimationPreview())
            foreach(var setting in Settings)
            {
                var definition=PonyAnimationData.Library.clips.Single(c=>c.id==setting.Item1);var frames=new JArray();
                int start=setting.Item1 is "walk" or "climb"?1:0;
                for(int f=start;f<definition.frames.Length;f++)
                {
                    var source=PonyRigMotionCompiler.Sample(rig,setting.Item1,f,definition.frames.Length);
                    if(source==null)throw new InvalidOperationException("Нет исходного движения для кадра "+setting.Item1+"/"+f);
                    var markers=new JArray();
                    foreach(var connection in connections)
                    {
                        int p=rig.Indices[GodotRigPilotData.SocketBones[connection.Parent]],c=rig.Indices[GodotRigPilotData.SocketBones[connection.Child]];
                        var parent=source.Delta[p].MultiplyPoint3x4(connection.Shared.ParentAnchor);var child=source.Delta[c].MultiplyPoint3x4(connection.Shared.ChildAnchor);
                        markers.Add(new JArray(parent.x,parent.y,child.x,child.y));
                    }
                    var pictures=new JArray();
                    foreach(string outfit in Outfits)
                    {
                        var art=PonyAnimationArtwork.Prepare(outfit,setting.Item1,f);
                        var matrices=art.Frame.Nodes.Select(n=>n.transform.Matrix).ToArray();preview.Render(art,matrices,world,Width,Height);
                        string file=setting.Item1+"-"+f.ToString("D2")+(outfit.Length==0?"-bare":"-antirad")+".png";
                        string path=Path.Combine(output,file);preview.WritePng(path);pictures.Add("data:image/png;base64,"+Convert.ToBase64String(File.ReadAllBytes(path)));
                    }
                    frames.Add(new JObject { ["sourceFrame"]=f,["images"]=pictures,["markers"]=markers });
                }
                clips.Add(new JObject { ["id"]=setting.Item1,["label"]=setting.Item2,["loop"]=setting.Item3,["frames"]=frames });
            }
            var pairs=new JArray(connections.Select(c=>new JObject { ["label"]=c.Label,["classification"]=c.Classification,
                ["trainingRms"]=c.Shared.Rms,["holdoutMax"]=c.SharedHoldout.Max,["identifiable"]=c.Shared.Identifiable,
                ["rank"]=c.Shared.Rank,["unknowns"]=c.Shared.Unknowns }));
            var data=new JObject { ["clips"]=clips,["pairs"]=pairs,["world"]=new JArray(world.x,world.y,world.width,world.height),["width"]=Width,["height"]=Height };
            var html=new StringBuilder(Template);html.Replace("@DATA@",data.ToString(Formatting.None));
            string target=Path.Combine(output,"cutout-joints-v1.html"),temporary=target+".tmp";
            File.WriteAllText(temporary,html.ToString(),new UTF8Encoding(false));
            if(File.Exists(target))File.Replace(temporary,target,null);else File.Move(temporary,target);
        }
        private static Rect MeasureWorld()
        {
            Bounds bounds=default;bool any=false;
            foreach(var setting in Settings)
            {
                var clip=PonyAnimationData.Library.clips.Single(c=>c.id==setting.Id);
                int start=setting.Id is "walk" or "climb"?1:0;
                for(int f=start;f<clip.frames.Length;f++)foreach(string outfit in Outfits)
                {
                    var current=PonyAnimationArtwork.Prepare(outfit,setting.Id,f).Bounds;
                    if(!any){bounds=current;any=true;}else bounds.Encapsulate(current);
                }
            }
            if(!any || bounds.size.x<=0 || bounds.size.y<=0)
                throw new InvalidOperationException("Нет геометрии для рамки проверки креплений.");
            // One fixed camera for all frames and outfits: no clipping and no apparent zoom flicker.
            float width=bounds.size.x*1.12f,height=bounds.size.y*1.12f,aspect=(float)Width/Height;
            if(width/height<aspect)width=height*aspect;else height=width/aspect;
            return new Rect(bounds.center.x-width/2,bounds.center.y-height/2,width,height);
        }
        private const string Template=@"<!doctype html><html lang='ru'><meta charset='utf-8'>
<meta name='viewport' content='width=device-width,initial-scale=1'><title>Крепления составного пони — проверка v1</title>
<style>body{background:#252a32;color:#eee;font:17px/1.45 system-ui;margin:24px}main{max-width:1180px;margin:auto}.pair{display:grid;grid-template-columns:1fr 1fr;gap:16px}canvas{width:100%;height:auto;aspect-ratio:560/520;background:#2b3038}button,select,input{font:inherit;margin:6px}#status,#description{min-height:1.5em}table{border-collapse:collapse;width:100%;font-size:14px}td,th{padding:8px;border-bottom:1px solid #555;text-align:left}code{color:#a7d4ff}</style>
<main><h1>Крепления составного пони — проверка v1</h1>
<p><strong>Версия JOINTS-V1.</strong> Здесь существующие игровые анимации и смена рисунков частей. Резиновой деформации SpriteSkin и придуманного сгиба нет.</p>
<p>Зелёный — постоянная точка согласуется с округлением исходника. Жёлтый — приближение. Красный — одна неподвижная точка не описывает движение. Серый — движение не позволяет определить уникальный центр.</p>
<div><select id='clip' aria-label='Анимация' disabled></select><button id='play' disabled>Загрузка…</button><input id='time' type='range' min='0' value='0' disabled><span id='frame'></span></div>
<div><select id='joint' aria-label='Соединение' disabled></select><label><input id='markers' type='checkbox' checked>Показать расчётные точки</label></div>
<p id='status' role='status'>Подготовка кадров…</p><p id='description'></p>
<div class='pair'><canvas id='left' width='560' height='520'></canvas><canvas id='right' width='560' height='520'></canvas></div>
<p>Круг — точка родительской детали, крест — точка дочерней. Линия между ними показывает ошибку постоянного крепления. Это проверка по движениям, а не доказательство анатомии.</p>
<h2>Результаты проверки на других анимациях</h2><table><thead><tr><th>Соединение</th><th>Вывод</th><th>Максимальное расхождение в предпросмотре</th></tr></thead><tbody id='results'></tbody></table>
<p>Подробные численные данные: <code>joint-report.json</code>. Точки подбираются на ходьбе и приседании, затем проверяются на лестнице, краях и укрытиях. Обводка и исходные варианты деталей сохранены.</p></main>
<script>(()=>{'use strict';const data=@DATA@;
const clipSelect=document.getElementById('clip'),jointSelect=document.getElementById('joint'),playButton=document.getElementById('play'),slider=document.getElementById('time'),markerToggle=document.getElementById('markers'),status=document.getElementById('status'),description=document.getElementById('description'),frameLabel=document.getElementById('frame');
const canvases=[document.getElementById('left'),document.getElementById('right')],contexts=canvases.map(c=>c.getContext('2d'));
const titles={'stable-within-source-rounding':'Устойчивое крепление','approximate-pivot':'Приближённая точка','variable-attachment':'Постоянная точка не подтверждается','not-identifiable':'Недостаточно независимого движения','rigid-follow-no-unique-pivot':'Жёсткое следование: центр не уникален'};
const colors={'stable-within-source-rounding':'#61ee9b','approximate-pivot':'#ffd05a','variable-attachment':'#ff7979','not-identifiable':'#b7bec9','rigid-follow-no-unique-pivot':'#b7bec9'};
let clip=0,frame=0,joint=data.pairs.findIndex(p=>p.label.includes('Локоть ближний')),playing=true,ready=false,last=0,accumulator=0;if(joint<0)joint=0;
data.clips.forEach((c,i)=>clipSelect.add(new Option(c.label,String(i))));data.pairs.forEach((p,i)=>jointSelect.add(new Option(p.label,String(i))));jointSelect.value=String(joint);
const pixelScale=data.width/data.world[2];const rows=document.getElementById('results');data.pairs.forEach(p=>{const tr=document.createElement('tr');[p.label,titles[p.classification],(p.holdoutMax*pixelScale).toFixed(2)+' px'].forEach(t=>{const td=document.createElement('td');td.textContent=t;tr.append(td)});rows.append(tr)});
function point(x,y){return [(x-data.world[0])/data.world[2]*data.width,data.height-(y-data.world[1])/data.world[3]*data.height]}
function draw(){const f=data.clips[clip].frames[frame],pair=data.pairs[joint],m=f.markers[joint],a=point(m[0],m[1]),b=point(m[2],m[3]);
 contexts.forEach((ctx,i)=>{ctx.clearRect(0,0,data.width,data.height);ctx.drawImage(f.decoded[i],0,0);if(markerToggle.checked&&pair.identifiable){ctx.save();ctx.lineWidth=2;ctx.strokeStyle=colors[pair.classification];ctx.beginPath();ctx.moveTo(...a);ctx.lineTo(...b);ctx.stroke();ctx.beginPath();ctx.arc(a[0],a[1],6,0,Math.PI*2);ctx.stroke();ctx.beginPath();ctx.moveTo(b[0]-5,b[1]-5);ctx.lineTo(b[0]+5,b[1]+5);ctx.moveTo(b[0]+5,b[1]-5);ctx.lineTo(b[0]-5,b[1]+5);ctx.stroke();ctx.restore()}});
 slider.max=String(data.clips[clip].frames.length-1);slider.value=String(frame);frameLabel.textContent='Кадр '+(frame+1)+' / '+data.clips[clip].frames.length;
 description.textContent=pair.label+': '+titles[pair.classification]+(pair.identifiable?'. Расхождение здесь: '+Math.hypot(a[0]-b[0],a[1]-b[1]).toFixed(2)+' px.':'. Расчётная точка не показана: движение не определяет её однозначно.');
 canvases.forEach(c=>{c.dataset.frame=String(frame);c.dataset.clip=String(clip)});}
function load(src){const image=new Image();image.src=src;return image.decode().then(()=>image)}
Promise.all(data.clips.flatMap(c=>c.frames.map(f=>Promise.all(f.images.map(load)).then(images=>{f.decoded=images})))).then(()=>{
 ready=true;draw();[clipSelect,jointSelect,playButton,slider].forEach(c=>c.disabled=false);playButton.textContent='Пауза';status.textContent='Кадры готовы. JOINTS-V1 — составные детали, исходные движения.';document.documentElement.dataset.previewReady='true';
 playButton.onclick=()=>{playing=!playing;playButton.textContent=playing?'Пауза':'Продолжить';last=performance.now();accumulator=0};
 slider.oninput=()=>{playing=false;playButton.textContent='Продолжить';frame=+slider.value;draw()};clipSelect.onchange=()=>{clip=+clipSelect.value;frame=0;accumulator=0;last=performance.now();draw()};jointSelect.onchange=()=>{joint=+jointSelect.value;draw()};markerToggle.onchange=draw;
 function tick(now){if(!last)last=now;if(playing){accumulator+=Math.max(0,Math.min(250,now-last));const n=Math.floor(accumulator/(1000/30));if(n>0){accumulator-=n*1000/30;const c=data.clips[clip];if(frame+n>=c.frames.length&&!c.loop){frame=c.frames.length-1;playing=false;playButton.textContent='Продолжить'}else frame=(frame+n)%c.frames.length;draw()}}last=now;requestAnimationFrame(tick)}requestAnimationFrame(tick);
}).catch(error=>{status.textContent='Не удалось загрузить предпросмотр: '+error.message;status.style.color='#ff9999';document.documentElement.dataset.previewReady='false'});
})();</script></html>";
    }
}
