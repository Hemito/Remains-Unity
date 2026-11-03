using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Debug=UnityEngine.Debug;
using Object=UnityEngine.Object;

namespace Remains.Editor
{
    // Stage 2. Offline views and Unity timings share PolarShadowField; browser only plays recorded frames.
    public static class PolarMotionStudy
    {
        private const int Frames=48,Fps=24,Cycles=9,Batch=8;
        private static readonly float[] Steps={.02f,.03f,.05f};
        private static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/light-polar-20261103"));
        [Serializable] private sealed class Track
        {
            public string id,title,description;
            public Vector2[] positions;
            public bool flash;
        }
        [Serializable] private sealed class Timing
        {
            public string operation;
            public double[] samplesMs;
            public double medianMs,p90Ms;
        }
        [Serializable] private sealed class Report
        {
            public string device,api,startedUtc,finishedUtc,method,validation;
            public int frames=Frames,fps=Fps,width=PolarShadowField.Width,height=PolarShadowField.Height,pngs,gc0,gc1,gc2;
            public float fill=.08f,fixedWidth=.05f;
            public double maxAngleError,maxFilterError,maxAAError,maxExportError;
            public Track[] tracks;
            public Timing[] timings;
        }
        private sealed class Operation
        {
            internal string Name;
            internal Action<int> Before,Run;
            internal RenderTexture Fence;
            internal readonly List<double> Times=new();
        }
        public static void RunBatch()
        {
            Texture2D image=null;
            try
            {
                if(Application.isPlaying) throw new InvalidOperationException("Проверка движущегося света запускается вне Play Mode.");
                Directory.CreateDirectory(Output);
                var scenes=PolarShadowCase.Create();
                var report=new Report{device=SystemInfo.graphicsDeviceName,api=SystemInfo.graphicsDeviceType.ToString(),startedUtc=DateTime.UtcNow.ToString("O"),
                    method="Этап 2: те же точные непрозрачные контуры и GPU-реализация, не игровой уровень. Три цикла по48 исходных положений при24кадр/с: небольшое движение возле диагональной тени, пересечение углового шва, движение в2см от стены. Траектории общие для всех методов, записаны в отчёте. Сравнение2/3/5см: legacy (две соседние проверки, ширина зависит от шага) и box-фильтр угловой видимости с одинаковой полной шириной5см на радиусе15м. Последний интегрирует кусочно-постоянные angular bins, НЕ интерполирует расстояния. Дополнительных дробных границ нет. Сглаженный эталон:16 точных лучей видимости на пиксель (4×4), геометрия приёмника/радиальная яркость по центру пикселя. Экспорт sRGB8 проверяется против линейного поля. Статическое заполнение0,08 зафиксировано по исходному положению и одинаково во всех вариантах; движущийся источник/вспышка не перестраивают/не добавляют заполнение. Вспышка в фиксированной позиции: один кадр из24, два импульса за цикл, выключение обнуляет прямой вклад и маркер, не трогая заполнение. Browser показывает записанные Unity PNG, его скорость НЕ является замером игры. Никакой временной интерполяции кадров/накопления теней. Замеры только diagonal:2прогрева,9циклов по8 операций; порядок разворачивается через цикл. Wall-clock включает отправку и завершение GPU чтением одного пикселя, делится на8 — пакетная пропускная способность, не GPU timestamps/FPS/задержка одного кадра. PNG/CPU-эталоны вне серии; память заранее выделена. Build-only меняет позицию и строит строку; cached-frame строит строку вне замера и повторяет отрисовку; moving-total меняет позицию, строит строку и рисует. Во всех измеряемых polar вариантах ширина5см. Геометрия не меняется. Нагрузку ПК специально не меняли. Нет нескольких ламп, alpha-материалов, игровых тел/теней, полных заводских контуров или слоёв поглощения."};
                image=new Texture2D(PolarShadowField.Width,PolarShadowField.Height,TextureFormat.RGBA32,false,true);
                var tracks=new List<Track>();
                foreach(var scene in scenes.Where(s=>s.Id!="thin-far"))
                {
                    var track=MakeTrack(scene);tracks.Add(track);
                    using var field=new PolarShadowField(scene);
                    for(int frame=0;frame<Frames;frame++)
                    {
                        field.SetSource(track.positions[frame]);field.SetGain(1);
                        field.RenderReferenceAA();
                        if(frame%12==0) report.maxAAError=Math.Max(report.maxAAError,ValidateAA(field));
                        field.Compose();Save(field,image,track.id,"aa",frame,report);
                        foreach(float step in Steps)
                        {
                            field.Build(step);
                            if(frame%12==0 || frame==Frames-1) report.maxAngleError=Math.Max(report.maxAngleError,PolarShadowStudy.ValidateAngles(field));
                            foreach(bool legacy in new[]{true,false})
                            {
                                field.RenderPolar(legacy?-1:.05f);
                                if(!legacy && frame%12==0) report.maxFilterError=Math.Max(report.maxFilterError,ValidateFilter(field));
                                field.Compose();
                                if(frame==0 && step==.05f && !legacy) report.maxExportError=Math.Max(report.maxExportError,ValidateExport(field,image));
                                Save(field,image,track.id,Variant(step,legacy),frame,report);
                            }
                        }
                        if(frame%12==11) Debug.Log($"Полярное движение: {scene.Id}, кадры {frame+1}/{Frames}.");
                    }
                }
                using(var field=new PolarShadowField(scenes[0]))
                {
                    var track=MakeTrack(scenes[0]);
                    report.timings=Measure(field,track.positions,report);
                    tracks.Add(Flash(field,image,report));
                }
                report.tracks=tracks.ToArray();report.finishedUtc=DateTime.UtcNow.ToString("O");
                report.validation="PASS: CPU double-пересечения для всех углов на контрольных движущихся позициях; независимая CPU-интеграция box-фильтра по1024пикселям для каждого шага в контрольных кадрах; CPU4×4-эталон по512пикселям в контрольных кадрах; экспорт сRGB8 с допуском2/255. После вспышки выключенный кадр побитно равен исходному (нет следа/накопления). Ошибки движения/субъективное мерцание не объявлены отсутствующими — оценка по синхронным кадрам. Базовое заполнение не пересчитывается во время движения или вспышки.";
                File.WriteAllText(Path.Combine(Output,"motion-report.json"),JsonUtility.ToJson(report,true));RefreshPage();
                Debug.Log("Полярное движение: PASS; "+JsonUtility.ToJson(report,true));EditorApplication.Exit(0);
            }
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
            finally {if(image!=null) Object.DestroyImmediate(image);}
        }
        public static void RefreshPageBatch()
        {
            try {RefreshPage();Debug.Log("Полярное движение: страница обновлена без повторных измерений.");EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void VerifyBatch()
        {
            Texture2D image=null;
            try
            {
                image=new Texture2D(PolarShadowField.Width,PolarShadowField.Height,TextureFormat.RGBA32,false,true);
                using var field=new PolarShadowField(PolarShadowCase.Create()[0]);
                var verification=new Report();Flash(field,image,verification);field.SetGain(1);
                foreach(float step in Steps) {field.Build(step);PolarShadowStudy.ValidateAngles(field);field.RenderPolar(.05f);ValidateFilter(field);}
                field.RenderReferenceAA();ValidateAA(field);
                field.Build(.05f);field.RenderPolar(true);field.Compose();
                field.SaveImage(Path.Combine(Output,"motion-legacy-check.png"),image);
                var direct=PolarShadowField.Read(field.Light);
                var previous=new Texture2D(2,2,TextureFormat.RGBA32,false,true);
                try
                {
                    if(!previous.LoadImage(File.ReadAllBytes(Path.Combine(Output,"diagonal-polar05-smooth.png"))))
                        throw new InvalidOperationException("Не прочитан предыдущий кадр полярной пробы.");
                    var before=previous.GetPixels32();var after=image.GetPixels32();int maximum=0,checkedPixels=0;
                    if(before.Length!=after.Length) throw new InvalidOperationException("Изменился размер прежнего полярного кадра.");
                    for(int i=0;i<before.Length;i++)
                    {
                        // Fill reconstruction intentionally changed. Compare only fully lit pixels
                        // where direct light dominates the maximum possible .08 grid contribution.
                        if(direct[i].a<.999f || direct[i].r<=.1f)continue;
                        checkedPixels++;
                        maximum=Math.Max(maximum,Math.Abs(before[i].r-after[i].r));maximum=Math.Max(maximum,Math.Abs(before[i].g-after[i].g));
                        maximum=Math.Max(maximum,Math.Abs(before[i].b-after[i].b));
                    }
                    if(checkedPixels<10000 || maximum>3) throw new InvalidOperationException("Изменился прежний прямой свет: пикселей="+checkedPixels+", максимум "+maximum+"/255.");
                    Debug.Log("Полярное движение: прежний прямой свет сохранён, проверено "+checkedPixels+" пикселей, отличие sRGB-экспорта "+maximum+"/255; заполнение исправлено отдельно.");
                }
                finally {Object.DestroyImmediate(previous);}
                RefreshPage();Debug.Log("Полярное движение: финальная компиляция, фильтр, AA и побитное исчезновение вспышки PASS. Замеры не повторялись.");EditorApplication.Exit(0);
            }
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
            finally {if(image!=null) Object.DestroyImmediate(image);}
        }
        private static void RefreshPage()=>File.Copy(Path.Combine(Application.dataPath,"Remains/Editor/LightGridStudy/PolarMotionPreview.html.txt"),Path.Combine(Output,"motion.html"),true);
        internal static void RefreshFlashImages()
        {
            var image=new Texture2D(PolarShadowField.Width,PolarShadowField.Height,TextureFormat.RGBA32,false,true);
            try
            {
                string old=Path.Combine(Output,"motion-flash-p05-fixed-00.png"),before=Path.Combine(Output,"halo-before.png");
                if(File.Exists(old) && !File.Exists(before))File.Copy(old,before);
                using var field=new PolarShadowField(PolarShadowCase.Create()[0]);Flash(field,image,new Report());
                File.Copy(old,Path.Combine(Output,"halo-after.png"),true);
            }
            finally{Object.DestroyImmediate(image);}
        }
        private static Track MakeTrack(PolarShadowCase scene)
        {
            var track=new Track{id=scene.Id,title=scene.Title,positions=new Vector2[Frames],description=scene.Id=="diagonal"?
                "Лампа движется на ±10 см по горизонтали и ±3 см по вертикали. Смотрите дальний край тени при увеличении4×.":scene.Id=="seam"?
                "Лампа движется вверх/вниз на ±8 см: тень проходит через угловой стык0/360°.":
                "Лампа движется вдоль стены на ±15 см; расстояние до её поверхности постоянно2см."};
            for(int frame=0;frame<Frames;frame++)
            {
                float a=frame*Mathf.PI*2/Frames;
                track.positions[frame]=scene.Source+(scene.Id=="diagonal"?new Vector2(.1f*Mathf.Sin(a),.03f*Mathf.Sin(2*a)):
                    new Vector2(0,(scene.Id=="seam"?.08f:.15f)*Mathf.Sin(a)));
            }
            return track;
        }
        private static string Variant(float step,bool legacy)=>"p"+Mathf.RoundToInt(step*100).ToString("00")+(legacy?"-legacy":"-fixed");
        private static void Save(PolarShadowField field,Texture2D image,string track,string variant,int frame,Report report)
        {field.SaveImage(Path.Combine(Output,$"motion-{track}-{variant}-{frame:00}.png"),image);report.pngs++;}
        private static double ValidateAA(PolarShadowField field)
        {
            var pixels=PolarShadowField.Read(field.Light);double maximum=0;
            for(int k=0;k<512;k++)
            {
                int index=(int)((long)k*15485863%pixels.Length),x=index%PolarShadowField.Width,y=index/PolarShadowField.Width;
                var center=new Vector2((x+.5f)*32/PolarShadowField.Width,(y+.5f)*18/PolarShadowField.Height);
                if(field.Case.Contains(center)) continue;
                double expected=0;bool tie=false;
                for(int sy=0;sy<4;sy++) for(int sx=0;sx<4;sx++)
                {
                    double px=(x+(sx+.5)/4)*32/PolarShadowField.Width,py=(y+(sy+.5)/4)*18/PolarShadowField.Height;
                    double dx=px-field.Source.x,dy=py-field.Source.y,d=Math.Sqrt(dx*dx+dy*dy);
                    double nearest=d<1e-7?15:field.Case.Nearest(field.Source,dx/d,dy/d);
                    if(Math.Abs(d-nearest)<.002) tie=true;
                    expected+=d<=nearest+1e-5?1:0;
                }
                if(tie) continue;
                double error=Math.Abs(pixels[index].a-expected/16);maximum=Math.Max(maximum,error);
                if(error>.0001) throw new InvalidOperationException($"Сглаженный эталон {field.Case.Id}, пиксель{index}: ошибка{error}.");
            }
            return maximum;
        }
        private static double ValidateFilter(PolarShadowField field)
        {
            var angles=PolarShadowField.Read(field.Angles);var light=PolarShadowField.Read(field.Light);double maximum=0;
            for(int k=0;k<1024;k++)
            {
                int index=(int)((long)k*15485863%light.Length),x=index%PolarShadowField.Width,y=index/PolarShadowField.Width;
                if(light[index].a<0) continue;
                double dx=(x+.5)*32/PolarShadowField.Width-field.Source.x,dy=(y+.5)*18/PolarShadowField.Height-field.Source.y,d=Math.Sqrt(dx*dx+dy*dy);
                double angle=(Math.Atan2(dy,dx)/(Math.PI*2)+1)%1*field.AngleCount,width=.05*field.AngleCount/(Math.PI*2*15);
                double lo=angle-width/2,hi=angle+width/2,expected=0;
                for(int bin=(int)Math.Floor(lo);bin<=Math.Floor(hi);bin++)
                    if(d<=angles[(bin%field.AngleCount+field.AngleCount)%field.AngleCount].r+1e-5)
                        expected+=Math.Max(0,Math.Min(hi,bin+1)-Math.Max(lo,bin))/width;
                double error=Math.Abs(light[index].a-expected);maximum=Math.Max(maximum,error);
                if(error>.005) throw new InvalidOperationException($"Box-фильтр {field.Case.Id},N={field.AngleCount},пиксель{index}: ошибка{error}.");
            }
            return maximum;
        }
        private static double ValidateExport(PolarShadowField field,Texture2D image)
        {
            string path=Path.Combine(Output,"motion-export-check.png");field.SaveImage(path,image);
            var linear=PolarShadowField.Read(field.Image);var bytes=image.GetPixels32();double maximum=0;
            for(int i=0;i<linear.Length;i+=997) for(int c=0;c<3;c++)
            {
                float v=Mathf.Clamp01(linear[i][c]),expected=v<=.0031308f?12.92f*v:1.055f*Mathf.Pow(v,1/2.4f)-.055f;
                double actual=(c==0?bytes[i].r:c==1?bytes[i].g:bytes[i].b)/255.0,error=Math.Abs(actual-expected);
                maximum=Math.Max(maximum,error);if(error>2.0/255) throw new InvalidOperationException("Ошибка экспорта полярного кадра: "+error);
            }
            return maximum;
        }
        private static Track Flash(PolarShadowField field,Texture2D image,Report report)
        {
            field.SetSource(field.Case.Source);field.Build(.05f);field.RenderPolar(.05f);field.SetGain(0);field.Compose();
            var before=PolarShadowField.Read(field.Image);
            field.SetGain(1);field.Compose();Save(field,image,"flash","p05-fixed",1,report);
            field.SetGain(0);field.Compose();Save(field,image,"flash","p05-fixed",0,report);
            var after=PolarShadowField.Read(field.Image);
            for(int i=0;i<before.Length;i++) for(int channel=0;channel<4;channel++)
                if(BitConverter.SingleToInt32Bits(before[i][channel])!=BitConverter.SingleToInt32Bits(after[i][channel]))
                    throw new InvalidOperationException("Остаток вспышки в пикселе "+i+", канал"+channel);
            field.RenderReferenceAA();field.SetGain(1);field.Compose();Save(field,image,"flash","aa",1,report);
            field.SetGain(0);field.Compose();Save(field,image,"flash","aa",0,report);
            return new Track{id="flash",title="Короткая вспышка",description="Вспышка на один кадр (около42мс), два раза за цикл. Нет нового заполнения/отражений. Для неё показан только выбранный вариант5см с фиксированным сглаживанием5см.",positions=Enumerable.Repeat(field.Case.Source,Frames).ToArray(),flash=true};
        }
        private static Timing[] Measure(PolarShadowField field,Vector2[] positions,Report report)
        {
            var ops=new List<Operation>();
            foreach(float step in Steps)
            {
                float s=step;string name=Mathf.RoundToInt(s*100).ToString("00");
                ops.Add(new Operation{Name="build-"+name,Run=i=>{field.SetSource(positions[i%Frames]);field.Build(s);},Fence=field.Angles});
                ops.Add(new Operation{Name="cached-frame-"+name,Before=i=>{field.SetSource(positions[i%Frames]);field.Build(s);field.Wait(field.Angles);},
                    Run=i=>{field.RenderPolar(.05f);field.Compose();},Fence=field.Image});
                ops.Add(new Operation{Name="moving-total-"+name,Run=i=>{field.SetSource(positions[i%Frames]);field.Build(s);field.RenderPolar(.05f);field.Compose();},Fence=field.Image});
            }
            ops.Add(new Operation{Name="flash-on-total-05",Run=i=>{field.SetSource(field.Case.Source);field.SetGain(1);field.Build(.05f);field.RenderPolar(.05f);field.Compose();},Fence=field.Image});
            ops.Add(new Operation{Name="flash-off",Run=i=>{field.SetGain(0);field.Compose();},Fence=field.Image});
            field.SetGain(1);field.Build(.05f);field.RenderPolar(.05f);field.Compose();field.Wait(field.Image);
            int gc0=GC.CollectionCount(0),gc1=GC.CollectionCount(1),gc2=GC.CollectionCount(2);var clock=new Stopwatch();
            for(int cycle=-2;cycle<Cycles;cycle++)
            {
                var order=(cycle&1)==0?ops:ops.AsEnumerable().Reverse();
                foreach(var op in order)
                {
                    field.SetGain(1);op.Before?.Invoke((cycle+2)*Batch);field.Wait(op.Fence);
                    clock.Restart();for(int repeat=0;repeat<Batch;repeat++) op.Run((cycle+2)*Batch+repeat);field.Wait(op.Fence);clock.Stop();
                    if(cycle>=0) op.Times.Add(clock.Elapsed.TotalMilliseconds/Batch);
                }
            }
            report.gc0=GC.CollectionCount(0)-gc0;report.gc1=GC.CollectionCount(1)-gc1;report.gc2=GC.CollectionCount(2)-gc2;field.SetGain(1);
            return ops.Select(op=>new Timing{operation=op.Name,samplesMs=op.Times.ToArray(),medianMs=Percentile(op.Times,.5),p90Ms=Percentile(op.Times,.9)}).ToArray();
        }
        private static double Percentile(List<double> values,double q)=>values.OrderBy(x=>x).ElementAt((int)Math.Ceiling(q*values.Count)-1);
    }
}
