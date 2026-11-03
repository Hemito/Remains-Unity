using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug=UnityEngine.Debug;
using Object=UnityEngine.Object;

namespace Remains.Editor
{
    // Stage 1: controlled contours, one opaque point light. Does not build a factory or modify runtime assets.
    public static class PolarShadowStudy
    {
        private const int Warmup=2,Cycles=9,Batch=8;
        private static readonly float[] Steps={.02f,.03f,.05f};
        private static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/light-polar-20261103"));
        [Serializable] private sealed class Error
        {
            public string scene,variant;
            public int angles,openPixels,wrongHardPixels;
            public double meanVisibilityError,maxVisibilityError;
        }
        [Serializable] private sealed class Timing
        {
            public string operation;
            public double[] samplesMs;
            public double medianMs,p90Ms;
        }
        [Serializable] private sealed class Report
        {
            public string startedUtc,finishedUtc,device,api,method,validation;
            public int width=PolarShadowField.Width,height=PolarShadowField.Height,segmentsInTimedCase;
            public float fill=.08f,radius=PolarShadowCase.Radius;
            public long reservedAngleBytes=PolarShadowField.MaximumAngles*4L,fullFieldBytes=PolarShadowField.Width*PolarShadowField.Height*16L;
            public int warmup=Warmup,cycles=Cycles,batch=Batch,gc0,gc1,gc2,filledShadowChecks;
            public double maxAngleDistanceError;
            public Error[] errors;
            public Timing[] timings;
        }
        private sealed class Operation
        {
            internal string Name;
            internal Action Run,Before;
            internal RenderTexture Fence;
            internal readonly List<double> Times=new();
        }
        public static void RefreshPageBatch()
        {
            try
            {
                var report=JsonUtility.FromJson<Report>(File.ReadAllText(Path.Combine(Output,"report.json")));
                if(report?.timings==null || report.timings.Length==0) throw new InvalidOperationException("Нет готовых измерений полярных теней.");
                WritePage(PolarShadowCase.Create(),report);
                Debug.Log("Полярные тени: страница обновлена без повторных измерений.");EditorApplication.Exit(0);
            }
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void RunBatch()
        {
            try
            {
                if(Application.isPlaying) throw new InvalidOperationException("Проба полярных теней запускается вне Play Mode.");
                Directory.CreateDirectory(Output);
                var report=new Report{startedUtc=DateTime.UtcNow.ToString("O"),device=SystemInfo.graphicsDeviceName,api=SystemInfo.graphicsDeviceType.ToString(),
                    method="Этап 1, Editor batch: синтетическая сцена 32×18 м, один точечный источник радиуса 15 м, непрозрачные непересекающиеся контуры. Одна геометрия для CPU-эталона, GPU-контуров, силуэта и консервативных клеток заполнения. Не raster ray marching: строки построены точным пересечением лучей с отрезками. Эталон direct проверяет те же отрезки в каждом пикселе; контроль 20 см вычисляет ту же формулу в центрах клеток и билинейно увеличивает. Это изолирует дискретизацию, не копия production Direct/Bounce. Во всех кадрах одно поле GridLightField и max(softKnee(direct),0.08*grid), одинаковая радиальная функция и цвет. Полярные варианты: ближайший угол либо линейное смешивание ДВУХ проверок видимости, без интерполяции расстояний и без дополнительных дробных границ. Все карты строятся заранее. Замеры только diagonal, 1536×864, 2 прогрева и 9 циклов по 8 повторов; порядок операций разворачивается через цикл. Wall-clock: отправка команд + завершение GPU через чтение одного пикселя, делённые на 8; это пакетная пропускная способность с ожиданием, не GPU timestamps, не отдельная задержка кадра и не Player FPS. Readback PNG и проверки вне замеров. Текстуры/buffers уже выделены; создание/подготовка заполнения вне замера. Внешняя нагрузка ПК не меняется. Статика: строка кэшируется; frame включает расчёт polar в полном кадре и Compose, для control20 только Compose готового поля; rebuild-control20 измерен отдельно. Тени нескольких точек лампы, частичное поглощение, геометрия реального завода, движения и временная стабильность ещё не проверены. Нет отсечения числа ламп, LOD или скрытых подстановок."};
                var errors=new List<Error>();var scenes=PolarShadowCase.Create();
                foreach(var scene in scenes)
                {
                    using var field=new PolarShadowField(scene);
                    field.RenderExact();var oracle=PolarShadowField.Read(field.Light);ValidateOracle(scene,oracle);
                    field.Compose();report.filledShadowChecks+=field.ValidateFillComposition(oracle);Save(field.Image,scene.Id+"-exact.png");
                    field.RenderExact(true);field.Compose(true);Save(field.Image,scene.Id+"-coarse.png");
                    foreach(float step in Steps)
                    {
                        field.Build(step);report.maxAngleDistanceError=Math.Max(report.maxAngleDistanceError,ValidateAngles(field));
                        foreach(bool filtered in new[]{false,true})
                        {
                            string variant=Variant(step,filtered);field.RenderPolar(filtered);
                            errors.Add(Compare(scene.Id,variant,field.AngleCount,oracle,PolarShadowField.Read(field.Light)));
                            field.Compose();Save(field.Image,scene.Id+"-"+variant+".png");
                        }
                    }
                    Debug.Log("Полярные тени: кадры и эталон готовы, "+scene.Id);
                }
                using(var field=new PolarShadowField(scenes[0]))
                {
                    report.segmentsInTimedCase=scenes[0].Segments.Count;
                    report.timings=Measure(field,report);
                }
                report.errors=errors.ToArray();report.finishedUtc=DateTime.UtcNow.ToString("O");
                report.validation="PASS: GPU-расстояния всех углов сопоставлены CPU double-эталону во всех 4 сценах и 3 шагах; допустимое расхождение 0.005 м. Full-pixel exact проверен по 8192 CPU-лучам в каждой сцене. Ошибки дискретизации polar НЕ объявлены PASS: они опубликованы отдельно для оценки. Все значения конечны; геометрия/свет/fill общие, параметры не подгонялись между вариантами.";
                File.WriteAllText(Path.Combine(Output,"report.json"),JsonUtility.ToJson(report,true));WritePage(scenes,report);
                Debug.Log("Полярные тени, этап 1: PASS; "+Output+"\n"+JsonUtility.ToJson(report,true));EditorApplication.Exit(0);
            }
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        private static string Variant(float step,bool filtered)=>"polar"+Mathf.RoundToInt(step*100).ToString("00")+(filtered?"-smooth":"-hard");
        internal static double ValidateAngles(PolarShadowField field)
        {
            var pixels=PolarShadowField.Read(field.Angles);double maximum=0;
            for(int i=0;i<field.AngleCount;i++)
            {
                double a=(i+.5)*Math.PI*2/field.AngleCount,expected=field.Case.Nearest(field.Source,Math.Cos(a),Math.Sin(a));
                double delta=Math.Abs(pixels[i].r-expected);maximum=Math.Max(maximum,delta);
                if(double.IsNaN(delta) || double.IsInfinity(delta) || delta>.005)
                    throw new InvalidOperationException($"Угловая карта {field.Case.Id}, N={field.AngleCount}, угол={i}: GPU={pixels[i].r}, CPU={expected}, ошибка={delta}.");
            }
            return maximum;
        }
        private static void ValidateOracle(PolarShadowCase scene,Color[] pixels)
        {
            int checkedPixels=0;
            for(int k=0;k<8192;k++)
            {
                int index=(int)((long)k*15485863%pixels.Length),x=index%PolarShadowField.Width,y=index/PolarShadowField.Width;
                var p=new Vector2((x+.5f)/PolarShadowField.Width*32,(y+.5f)/PolarShadowField.Height*18);
                double dx=p.x-scene.Source.x,dy=p.y-scene.Source.y,d=Math.Sqrt(dx*dx+dy*dy);
                double nearest=d<1e-7?15:scene.Nearest(dx/d,dy/d);
                if(Math.Abs(d-nearest)<.002) continue; // exclude numerical ties on geometry/radius, not polar mismatches
                float expected=scene.Contains(p)?-1:(d<=nearest+1e-5?1:0);
                if(Math.Abs(pixels[index].a-expected)>.001)
                    throw new InvalidOperationException($"Пиксельный эталон {scene.Id}, ({x},{y}): GPU={pixels[index].a}, CPU={expected}.");
                checkedPixels++;
            }
            if(checkedPixels<8000) throw new InvalidOperationException("Недостаточно точек CPU-проверки: "+scene.Id);
        }
        private static Error Compare(string scene,string variant,int angles,Color[] exact,Color[] actual)
        {
            var error=new Error{scene=scene,variant=variant,angles=angles};double sum=0;
            for(int i=0;i<exact.Length;i++)
            {
                var p=actual[i];
                if(!float.IsFinite(p.r) || !float.IsFinite(p.g) || !float.IsFinite(p.b) || !float.IsFinite(p.a))
                    throw new InvalidOperationException($"Неконечный свет {scene}/{variant}, пиксель {i}.");
                if(exact[i].a<0) continue;
                double delta=Math.Abs(exact[i].a-p.a);sum+=delta;error.maxVisibilityError=Math.Max(error.maxVisibilityError,delta);
                if(delta>.5) error.wrongHardPixels++;error.openPixels++;
            }
            error.meanVisibilityError=sum/error.openPixels;return error;
        }
        private static Timing[] Measure(PolarShadowField field,Report report)
        {
            var ops=new List<Operation>
            {
                new(){Name="rebuild-control20",Run=()=>field.RenderExact(true),Fence=field.Coarse},
                new(){Name="frame-control20-cached",Run=()=>field.Compose(true),Fence=field.Image},
                new(){Name="frame-exact-oracle",Run=()=>{field.RenderExact();field.Compose();},Fence=field.Image}
            };
            foreach(float step in Steps)
            {
                float value=step;
                ops.Add(new Operation{Name="build-"+Variant(value,false),Run=()=>field.Build(value),Fence=field.Angles});
                foreach(bool filtered in new[]{false,true})
                {
                    bool smooth=filtered;
                    ops.Add(new Operation{Name="frame-"+Variant(value,smooth),Before=()=>{field.Build(value);field.Wait(field.Angles);},
                        Run=()=>{field.RenderPolar(smooth);field.Compose();},Fence=field.Image});
                }
            }
            field.RenderExact(true);field.Wait(field.Coarse);
            int g0=GC.CollectionCount(0),g1=GC.CollectionCount(1),g2=GC.CollectionCount(2);
            var clock=new Stopwatch();
            for(int cycle=-Warmup;cycle<Cycles;cycle++)
            {
                var order=(cycle&1)==0?ops:ops.AsEnumerable().Reverse();
                foreach(var op in order)
                {
                    op.Before?.Invoke();field.Wait(op.Fence);
                    clock.Restart();for(int repeat=0;repeat<Batch;repeat++) op.Run();field.Wait(op.Fence);clock.Stop();
                    if(cycle>=0) op.Times.Add(clock.Elapsed.TotalMilliseconds/Batch);
                }
                Debug.Log($"Полярные тени: цикл замеров {cycle+1}/{Cycles}.");
            }
            report.gc0=GC.CollectionCount(0)-g0;report.gc1=GC.CollectionCount(1)-g1;report.gc2=GC.CollectionCount(2)-g2;
            return ops.Select(op=>new Timing{operation=op.Name,samplesMs=op.Times.ToArray(),medianMs=Percentile(op.Times,.5),p90Ms=Percentile(op.Times,.9)}).ToArray();
        }
        private static double Percentile(List<double> values,double q)=>values.OrderBy(x=>x).ElementAt((int)Math.Ceiling(q*values.Count)-1);
        private static void Save(RenderTexture target,string name)
        {
            var pixels=PolarShadowField.Read(target);var image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false,true);
            try
            {
                for(int i=0;i<pixels.Length;i++) pixels[i]=pixels[i].gamma;
                image.SetPixels(pixels);image.Apply(false,false);File.WriteAllBytes(Path.Combine(Output,name),image.EncodeToPNG());
            }
            finally {Object.DestroyImmediate(image);}
        }
        private static void WritePage(PolarShadowCase[] scenes,Report report)
        {
            string H(string s)=>WebUtility.HtmlEncode(s);string F(double d)=>d.ToString("F3",CultureInfo.InvariantCulture);
            var page=new StringBuilder("<!doctype html><html lang=ru><meta charset=utf-8><meta name=viewport content='width=device-width,initial-scale=1'><title>Полярные тени — этап 1</title><style>body{background:#171b21;color:#eee;font:16px/1.5 system-ui;margin:24px}main{max-width:1700px;margin:auto}button,select{font:inherit;background:#343c49;color:white;border:1px solid #768294;padding:7px;margin:3px;border-radius:5px}button[aria-pressed=true]{background:#235b80}.pair{display:grid;grid-template-columns:1fr 1fr;gap:12px}canvas{width:100%;background:#080808;image-rendering:auto;touch-action:none}h2{margin-bottom:5px}table{border-collapse:collapse}td,th{padding:6px 12px;border-bottom:1px solid #555;text-align:left}small{color:#c3cbd5}#zoom{position:sticky;top:0;background:#171b21;padding:8px}a{color:#8ed5ff}@media(max-width:800px){.pair{grid-template-columns:1fr}}details{margin:18px 0}</style><main><h1>Одномерные карты теней: первая проба</h1><p><a href='motion.html'>Следующий этап: движение лампы и вспышка</a></p><p>Одна точечная лампа, точные непрозрачные контуры. Меняется только расчёт прямого света; сеточное заполнение везде <b>0,08</b>. Это технические сцены, ещё не внешний вид игры.</p><label>Сцена <select id=scene>");
            foreach(var scene in scenes) page.Append($"<option value='{scene.Id}'>{H(scene.Title)}</option>");
            page.Append("</select></label><label>Шаг на радиусе 15 м <select id=step><option value=02>2 см · 4713 углов</option><option value=03 selected>3 см · 3142 угла</option><option value=05>5 см · 1885 углов</option></select></label><label><input type=checkbox id=smooth checked>Сглаживать две проверки видимости</label><p id=description></p><div id=zoom>Масштаб <button data-zoom=1>Вся сцена</button><button data-zoom=2>2×</button><button data-zoom=4>4×</button><span>Щёлкните на кадр для центра увеличения. Масштаб одинаковый в обоих кадрах.</span><br>Слева <select id=left><option value=coarse selected>Контроль: поле 20 см</option><option value=exact>Эталон: точный луч на пиксель</option></select></div><div class=pair><section><h2 id=leftLabel></h2><canvas id=a width=1536 height=864></canvas></section><section><h2 id=rightLabel>Полярная карта</h2><canvas id=b width=1536 height=864></canvas></section></div><p id=status role=status>Загрузка кадров…</p><p><b>На что смотреть:</b> диагональный край, дальняя часть тени, сохранность тонкой преграды. В сцене «Стык» — линия строго вправо от лампы. Переключайте шаг и сглаживание при одном увеличении.</p><h2>Измерения: одна лампа, сцена «Диагональные тени»</h2><p>1536×864. Числа ниже — мс на операцию в пакете, включая отправку и ожидание GPU. Это не FPS игры и не сравнение со старой production-системой. Подготовка карты нужна при изменении лампы/препятствий; отрисовка — каждый кадр. У контроля 20 см поле уже готово.</p><table><tr><th>Операция</th><th>Медиана, мс</th><th>p90, мс</th></tr>");
            foreach(var t in report.timings) page.Append($"<tr><td>{H(Label(t.operation))}</td><td>{F(t.medianMs)}</td><td>{F(t.p90Ms)}</td></tr>");
            page.Append($"</table><p>{H(report.device)} · {H(report.api)}. GC: {report.gc0}/{report.gc1}/{report.gc2}. Резерв одной строки R32F: {report.reservedAngleBytes} байт; поле полного кадра RGBA32F: {report.fullFieldBytes} байт (проверочный формат, не бюджет будущей игры).</p><details><summary>Методика и пределы</summary><p>{H(report.method)}</p><p>{H(report.validation)} Максимальная ошибка расстояний GPU/CPU: {F(report.maxAngleDistanceError)} м.</p><a href=report.json>Полные данные и ошибки относительно эталона</a></details><script>const descriptions=");
            page.Append("{").Append(string.Join(",",scenes.Select(s=>$"'{s.Id}':{Js(s.Description)}"))).Append("};");
            page.Append("const scene=document.querySelector('#scene'),step=document.querySelector('#step'),smooth=document.querySelector('#smooth'),left=document.querySelector('#left'),status=document.querySelector('#status'),canvases=[document.querySelector('#a'),document.querySelector('#b')],cache=new Map;let zoom=1,cx=.5,cy=.5,serial=0,loaded=[];function load(path){if(!cache.has(path))cache.set(path,new Promise((resolve,reject)=>{const i=new Image;i.onload=()=>resolve(i);i.onerror=()=>reject(Error('Не загружен '+path));i.src=path}));return cache.get(path)}function draw(){if(loaded.length!==2)return;canvases.forEach((c,k)=>{const w=c.width/zoom,h=c.height/zoom,x=Math.max(0,Math.min(c.width-w,cx*c.width-w/2)),y=Math.max(0,Math.min(c.height-h,cy*c.height-h/2)),ctx=c.getContext('2d');ctx.clearRect(0,0,c.width,c.height);ctx.drawImage(loaded[k],x,y,w,h,0,0,c.width,c.height)});document.querySelectorAll('[data-zoom]').forEach(b=>b.setAttribute('aria-pressed',Number(b.dataset.zoom)===zoom))}async function update(){const ticket=++serial;loaded=[];document.documentElement.dataset.ready='loading';canvases.forEach(c=>c.getContext('2d').clearRect(0,0,c.width,c.height));status.textContent='Загрузка кадров…';document.querySelector('#description').textContent=descriptions[scene.value];document.querySelector('#leftLabel').textContent=left.selectedOptions[0].textContent;document.querySelector('#rightLabel').textContent='Полярная карта · '+step.selectedOptions[0].textContent+(smooth.checked?' · 2 проверки':' · без сглаживания');try{const files=[scene.value+'-'+left.value+'.png',scene.value+'-polar'+step.value+(smooth.checked?'-smooth':'-hard')+'.png'];const images=await Promise.all(files.map(load));if(ticket!==serial)return;loaded=images;draw();status.textContent='Кадры готовы. Центр и масштаб общие.';document.documentElement.dataset.ready='true'}catch(e){if(ticket===serial){status.textContent=e.message;document.documentElement.dataset.ready='false'}}}for(const e of [scene,step,smooth,left])e.addEventListener('change',update);document.querySelectorAll('[data-zoom]').forEach(b=>b.onclick=()=>{zoom=Number(b.dataset.zoom);draw()});canvases.forEach(c=>c.onclick=e=>{const r=c.getBoundingClientRect(),w=1/zoom,h=1/zoom,x=Math.max(0,Math.min(1-w,cx-w/2)),y=Math.max(0,Math.min(1-h,cy-h/2));cx=x+(e.clientX-r.left)/r.width*w;cy=y+(e.clientY-r.top)/r.height*h;draw()});update();</script></main></html>");
            File.WriteAllText(Path.Combine(Output,"index.html"),page.ToString());
        }
        private static string Js(string text)=>"\""+text.Replace("\\","\\\\").Replace("\"","\\\"").Replace("\n","\\n")+"\"";
        private static string Label(string operation)
        {
            if(operation=="rebuild-control20") return "Подготовить поле 20 см";
            if(operation=="frame-control20-cached") return "Кадр: готовое поле 20 см";
            if(operation=="frame-exact-oracle") return "Кадр: точный эталон";
            string[] parts=operation.Split('-');string step=parts[1].Replace("polar","");
            return (parts[0]=="build"?"Подготовить строку: ":"Кадр: ")+int.Parse(step,CultureInfo.InvariantCulture)+" см"+
                (parts[0]=="build"?"":parts[2]=="smooth"?", 2 проверки":", без сглаживания");
        }
    }
}
