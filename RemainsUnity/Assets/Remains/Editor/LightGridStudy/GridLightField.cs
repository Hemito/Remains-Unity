using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remains.Editor
{
    public readonly struct GridLightSeed
    {
        public readonly int Index;
        public readonly Color Radiance;
        // Radiance is the source colour/strength, NOT the weight of one shape sample.
        // Several cells of one emitting form get the same source value; max transport does not add them.
        public GridLightSeed(int index,Color radiance) {Index=index;Radiance=radiance;}
    }

    // Editor-only comparison. This is neither radiative transfer nor a bounce solver.
    // Four cardinal neighbours give Manhattan distance and cannot cut diagonally across a sealed corner.
    // One scalar max-path field per RGB8-normalized chromaticity. Decode constant hue * scalar,
    // then componentwise max across groups: coloured lights mix without a cold white turning pure blue.
    // This is an explicit coloured extension of bounded block light, not Minecraft's exact colour model.
    public sealed class GridLightField
    {
        public const int DisplayLevels=15,Sublevels=16,Maximum=DisplayLevels*Sublevels;
        public const float RadianceAtMaximum=1f,FalloffMetres=12f,OpaqueThreshold=0.99f;
        private readonly float[] absorption;
        private sealed class Group
        {
            public readonly Color Hue;
            public readonly byte[] Values;
            public Group(int key,int count) {Hue=HueFromKey(key);Values=new byte[count];}
        }
        private readonly struct QuantizedSeed
        {
            public readonly int Index,Level;
            public QuantizedSeed(int index,int level) {Index=index;Level=level;}
        }
        private readonly Dictionary<int,Group> groups=new();
        private byte[] values=Array.Empty<byte>();
        private readonly int[] next,previous,heads;
        private int queued;
        private bool solved;
        private RectInt region;
        public int Width {get;}
        public int Height {get;}
        public float CellSize {get;}
        public int StepLoss {get;}
        public float EffectiveMaximumRange=>Maximum*CellSize/StepLoss;
        public int InfluenceRadiusCells=>Maximum/StepLoss+1;
        public Color[] Pixels {get;}
        // Array payloads only, excluding CLR headers, caller-owned seeds/absorption and GPU upload.
        public int GroupCount=>groups.Count;
        public long AllocatedBytes=>(long)Pixels.Length*(16+GroupCount+4+4)+heads.Length*4L;
        public long ReferencedAbsorptionBytes=>(long)absorption.Length*4;
        public long PoppedCells {get;private set;}
        public long NeighbourChecks {get;private set;}
        public long ImprovedCells {get;private set;}
        public int PeakQueuedCells {get;private set;}
        public int SeedCount {get;private set;}
        public long RecomputedCells {get;private set;}
        public RectInt RecomputedRegion=>region;

        // absorption is per step of THIS grid: from a finer grid use
        // coarseAlpha=1-(1-fineAlpha)^(coarseCell/fineCell), preserving any opaque fine texel.
        // Partial opacity therefore attenuates by physical distance, as DirectLight.compute does.
        // The input is retained, not copied; caller must not mutate it while Solve runs.
        public GridLightField(int width,int height,float cellSize,float[] absorption)
        {
            if(width<=0 || height<=0 || (long)width*height>int.MaxValue) throw new ArgumentOutOfRangeException(nameof(width),"Некорректный размер световой сетки.");
            if(!Finite(cellSize) || cellSize<=0) throw new ArgumentOutOfRangeException(nameof(cellSize));
            if(absorption==null || absorption.Length!=width*height) throw new ArgumentException("Поглощение не совпадает с размером световой сетки.",nameof(absorption));
            for(int i=0;i<absorption.Length;i++)
                if(!Finite(absorption[i]) || absorption[i]<0 || absorption[i]>1) throw new ArgumentException($"Некорректное поглощение клетки {i}: {absorption[i]}.",nameof(absorption));
            Width=width;Height=height;CellSize=cellSize;this.absorption=absorption;
            // Round up rather than increase reach through a fractional/zero-cost grid edge.
            // At .2m the loss is exactly 4/240, maximum range 12m, fifteen displayed levels.
            // The small tolerance removes float representation noise (.2f would otherwise ceil 4.00000006 to 5).
            StepLoss=(int)Math.Max(1,Math.Min(Maximum,Math.Ceiling(Maximum*(double)cellSize/FalloffMetres-0.000001)));
            Pixels=new Color[absorption.Length];
            next=new int[absorption.Length];previous=new int[absorption.Length];heads=new int[Maximum+1];
        }

        public void Solve(IReadOnlyList<GridLightSeed> seeds)
        {
            ValidateSeeds(seeds);
            // Reset the active palette, but retain planes whose colours are still present. Repeated full
            // references must not allocate one whole-level byte array per unchanged colour every time.
            var present=new HashSet<int>();
            foreach(var seed in seeds) if(IntensityLevel(seed.Radiance)>0) present.Add(ColorKey(seed.Radiance));
            var removed=new List<int>();
            foreach(int key in groups.Keys) if(!present.Contains(key)) removed.Add(key);
            foreach(int key in removed) groups.Remove(key);
            values=Array.Empty<byte>();
            Rebuild(seeds,new RectInt(0,0,Width,Height));solved=true;
        }

        // RGB8 chromaticity introduces <=.5/255 absolute error per normalized channel.
        // Clamp the COMMON peak, not RGB channels separately: e.g. (3,1,0) keeps hue (1,1/3,0).
        public static Color NormalizedColor(Color radiance)=>HueFromKey(ColorKey(radiance));
        public int LevelForColor(Color radiance,int index)
        {
            if(index<0 || index>=Pixels.Length) throw new ArgumentOutOfRangeException(nameof(index));
            return groups.TryGetValue(ColorKey(radiance),out var group)?group.Values[index]:0;
        }
        private static int ColorKey(Color radiance)
        {
            if(!Valid(radiance.r) || !Valid(radiance.g) || !Valid(radiance.b)) throw new ArgumentException("Сетка света: некорректный цвет источника.");
            float peak=Math.Max(radiance.r,Math.Max(radiance.g,radiance.b));
            if(peak<=0) return 0;
            int r=Mathf.Clamp(Mathf.RoundToInt(radiance.r/peak*255),0,255);
            int g=Mathf.Clamp(Mathf.RoundToInt(radiance.g/peak*255),0,255);
            int b=Mathf.Clamp(Mathf.RoundToInt(radiance.b/peak*255),0,255);
            return (r<<16)|(g<<8)|b;
        }
        private static Color HueFromKey(int key)=>new Color(((key>>16)&255)/255f,((key>>8)&255)/255f,(key&255)/255f,1);
        private static int IntensityLevel(Color radiance)
        {
            float peak=Math.Max(radiance.r,Math.Max(radiance.g,radiance.b));
            return (int)Math.Floor(Math.Min(1,peak/RadianceAtMaximum)*Maximum+0.5);
        }

        // Caller first patches the retained absorption and provides current sources. It may query its spatial
        // source index and provide only seeds inside AffectedRegion(changedCells); exterior light is retained.
        // changedCells must
        // enclose every changed absorption cell and old AND new positions of added/removed/changed seeds.
        // Nothing outside this rectangle may have changed; this contract avoids scanning the whole level.
        public RectInt Update(IReadOnlyList<GridLightSeed> allCurrentSeeds,RectInt changedCells)
        {
            if(!solved) throw new InvalidOperationException("Сетка света: перед локальным обновлением нужен исходный Solve.");
            ValidateSeeds(allCurrentSeeds);
            var affected=AffectedRegion(changedCells);
            if(affected.width==0 || affected.height==0)
            {
                ResetStatistics(allCurrentSeeds.Count,affected);return region;
            }
            // In EACH chromaticity group every edge loses at least StepLoss, all sources are <=Maximum,
            // and absorption never amplifies. Groups never transfer/relay light into one another.
            // Consequently ANY positive path has <Maximum/StepLoss edges. A changed cell/source cannot
            // affect the outside of this larger rectangle. Those old exterior values are exact boundary
            // conditions. Clearing the interior removes stale light; reseeding all sources + exterior
            // neighbours computes the same max-path solution as a full rebuild (also for decreases).
            Rebuild(allCurrentSeeds,affected);return region;
        }

        public RectInt AffectedRegion(RectInt changedCells)
        {
            if(changedCells.width<0 || changedCells.height<0) throw new ArgumentException("Сетка света: отрицательный размер изменённой области.");
            int x0=(int)Math.Max(0,Math.Min(Width,(long)changedCells.x));
            int y0=(int)Math.Max(0,Math.Min(Height,(long)changedCells.y));
            int x1=(int)Math.Max(0,Math.Min(Width,(long)changedCells.x+changedCells.width));
            int y1=(int)Math.Max(0,Math.Min(Height,(long)changedCells.y+changedCells.height));
            if(changedCells.width==0 || changedCells.height==0 || x1<=x0 || y1<=y0)
                return new RectInt(x0,y0,0,0);
            int radius=InfluenceRadiusCells;
            return Rectangle(Math.Max(0,x0-radius),Math.Max(0,y0-radius),Math.Min(Width,x1+radius),Math.Min(Height,y1+radius));
        }

        private void ValidateSeeds(IReadOnlyList<GridLightSeed> seeds)
        {
            if(seeds==null) throw new ArgumentNullException(nameof(seeds));
            for(int i=0;i<seeds.Count;i++)
            {
                var seed=seeds[i];
                if(seed.Index<0 || seed.Index>=Pixels.Length) throw new ArgumentException($"Источник {i}: индекс {seed.Index} вне световой сетки.");
                if(absorption[seed.Index]>=OpaqueThreshold) throw new ArgumentException($"Источник {i}: клетка {seed.Index} непрозрачна. Сначала разместите излучение на открытой поверхности формы.");
                if(!Valid(seed.Radiance.r) || !Valid(seed.Radiance.g) || !Valid(seed.Radiance.b)) throw new ArgumentException($"Источник {i}: некорректная яркость {seed.Radiance}.");
            }
        }
        private void ResetStatistics(int count,RectInt area)
        {
            SeedCount=count;PoppedCells=NeighbourChecks=ImprovedCells=0;PeakQueuedCells=0;queued=0;
            region=area;RecomputedCells=(long)area.width*area.height;
        }
        private void Rebuild(IReadOnlyList<GridLightSeed> seeds,RectInt area)
        {
            for(int y=area.yMin;y<area.yMax;y++) for(int x=area.xMin;x<area.xMax;x++)
            {
                int i=y*Width+x;
                if(!Valid(absorption[i]) || absorption[i]>1) throw new ArgumentException($"Сетка света: некорректное поглощение клетки {i}: {absorption[i]}.");
            }
            ResetStatistics(seeds.Count,area);
            var groupedSeeds=new Dictionary<int,List<QuantizedSeed>>();
            foreach(var seed in seeds)
            {
                int x=seed.Index%Width,y=seed.Index/Width;
                if(x<area.xMin || x>=area.xMax || y<area.yMin || y>=area.yMax) continue;
                int level=IntensityLevel(seed.Radiance);
                if(level==0) continue;
                int key=ColorKey(seed.Radiance);
                if(!groups.ContainsKey(key)) groups.Add(key,new Group(key,Pixels.Length));
                if(!groupedSeeds.TryGetValue(key,out var list)) groupedSeeds.Add(key,list=new List<QuantizedSeed>());
                list.Add(new QuantizedSeed(seed.Index,level));
            }
            for(int y=area.yMin;y<area.yMax;y++) for(int x=area.xMin;x<area.xMax;x++) Pixels[y*Width+x]=new Color(0,0,0,1);
            foreach(var pair in groups)
            {
                var group=pair.Value;values=group.Values;Array.Fill(heads,-1);queued=0;
                for(int y=area.yMin;y<area.yMax;y++)
                {
                    int row=y*Width+area.xMin;Array.Clear(values,row,area.width);
                    for(int i=row;i<row+area.width;i++) previous[i]=-2;
                }
                // Install EVERY source before propagation. Descending buckets give order-independent max paths.
                if(groupedSeeds.TryGetValue(pair.Key,out var placed)) foreach(var seed in placed)
                    if(seed.Level>values[seed.Index]) Put(seed.Index,seed.Level);
                // Unchanged cells immediately outside the region can illuminate its inside boundary.
                for(int y=area.yMin;y<area.yMax;y++)
                {
                    if(area.xMin>0) Enter(y*Width+area.xMin,y*Width+area.xMin-1);
                    if(area.xMax<Width) Enter(y*Width+area.xMax-1,y*Width+area.xMax);
                }
                for(int x=area.xMin;x<area.xMax;x++)
                {
                    if(area.yMin>0) Enter(area.yMin*Width+x,(area.yMin-1)*Width+x);
                    if(area.yMax<Height) Enter((area.yMax-1)*Width+x,area.yMax*Width+x);
                }
                for(int level=Maximum;level>StepLoss;level--)
                    while(heads[level]>=0)
                    {
                        int index=heads[level];Remove(index,level);PoppedCells++;
                        int x=index%Width,y=index/Width,remainder=level-StepLoss;
                        if(x>area.xMin) Relax(index-1,remainder);
                        if(x+1<area.xMax) Relax(index+1,remainder);
                        if(y>area.yMin) Relax(index-Width,remainder);
                        if(y+1<area.yMax) Relax(index+Width,remainder);
                    }
                for(int y=area.yMin;y<area.yMax;y++) for(int x=area.xMin;x<area.xMax;x++)
                {
                    int i=y*Width+x;var c=Pixels[i];float value=values[i]/(float)Maximum;
                    c.r=Math.Max(c.r,group.Hue.r*value);c.g=Math.Max(c.g,group.Hue.g*value);c.b=Math.Max(c.b,group.Hue.b*value);
                    Pixels[i]=c;
                }
            }
        }

        private void Enter(int inside,int outside)
        {
            int remainder=values[outside]-StepLoss;
            if(remainder>0) Relax(inside,remainder);
        }

        private void Relax(int index,int remainder)
        {
            NeighbourChecks++;
            if(absorption[index]>=OpaqueThreshold || remainder<=values[index]) return;
            // Every edge traverses one cell. The absorption supplied for that cell has already
            // been rescaled to CellSize; no extra exponent or source-weight multiplication here.
            int candidate=(int)Math.Floor(remainder*(1.0-absorption[index]));
            if(candidate>values[index]) {Put(index,candidate);ImprovedCells++;}
        }
        private void Put(int index,int level)
        {
            if(previous[index]!=-2) Remove(index,values[index]);
            values[index]=(byte)level;int first=heads[level];next[index]=first;previous[index]=-1;
            if(first>=0) previous[first]=index;
            heads[level]=index;queued++;PeakQueuedCells=Math.Max(PeakQueuedCells,queued);
        }
        private void Remove(int index,int level)
        {
            int before=previous[index],after=next[index];
            if(before>=0) next[before]=after;else heads[level]=after;
            if(after>=0) previous[after]=before;
            previous[index]=-2;next[index]=-1;queued--;
        }
        private static bool Finite(float value)=>!float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Valid(float value)=>Finite(value) && value>=0;
        private static RectInt Rectangle(int x0,int y0,int x1,int y1)=>new RectInt(x0,y0,x1-x0,y1-y0);

        public static string RunSelfChecks()
        {
            const int w=9,h=7;var a=new float[w*h];
            for(int y=0;y<h;y++) a[y*w+4]=1;
            var field=new GridLightField(w,h,.2f,a);int left=3*w+2,right=3*w+6;
            field.Solve(new[]{new GridLightSeed(left,Color.white)});
            Require(field.Pixels[right].r==0,"Герметичная стена пропускает свет.");
            field.Solve(Array.Empty<GridLightSeed>());
            foreach(var c in field.Pixels) Require(c.r==0 && c.g==0 && c.b==0,"Без источников остался свет.");
            Array.Clear(a,0,a.Length);
            var strong=new GridLightSeed(left,new Color(5,.25f,0));
            var weak=new GridLightSeed(right,new Color(.2f,0,.5f));
            field.Solve(new[]{strong,weak});var together=(Color[])field.Pixels.Clone();
            foreach(var c in together) Require(c.r<=1 && c.g<=1 && c.b<=1,"Перекрытие превысило предел яркости.");
            Require(together[left].b>0,"Слабый синий канал потерян рядом с сильным красным.");
            field.Solve(new[]{weak,strong});
            for(int i=0;i<together.Length;i++) Require(field.Pixels[i].Equals(together[i]),"Результат зависит от порядка источников.");
            field.Solve(new[]{weak});Require(field.Pixels[left].r<together[left].r && field.Pixels[left].r>0,"Удаление сильного источника потеряло слабый или оставило старый свет.");
            a[right]=.5f;field.Solve(new[]{new GridLightSeed(right-1,Color.white)});
            Require(Math.Abs(field.Pixels[right].r-118f/240)<.00001,"Неверно учтено частичное поглощение шага.");
            CheckHuePreservation();
            CheckLocalChanges();
            return "PASS: герметичная стена, темнота, ограниченное RGB-перекрытие, сохранение оттенка, слабый цвет, удаление источника, порядок, частичное поглощение; локальные события побитно равны полному пересчёту.";
        }

        private static void CheckHuePreservation()
        {
            var cold=new Color(.42f,.468f,.57f);var hue=NormalizedColor(cold);
            var alpha=new float[80];alpha[10]=.25f;
            var line=new GridLightField(80,1,.2f,alpha);line.Solve(new[]{new GridLightSeed(1,cold)});
            foreach(int x in new[]{1,5,11,20,25})
            {
                var actual=line.Pixels[x];Require(actual.b>0,"Проба холодного белого оказалась тёмной.");
                Require(Math.Abs(actual.r/actual.b-hue.r)<.00001 && Math.Abs(actual.g/actual.b-hue.g)<.00001,"Холодный белый поменял оттенок при затухании/поглощении.");
                Require(line.LevelForColor(cold,x)>0,"Не читается скалярный уровень цветовой группы.");
            }
            var retainedPlane=line.groups[ColorKey(cold)].Values;
            line.Solve(new[]{new GridLightSeed(1,cold)});
            Require(ReferenceEquals(retainedPlane,line.groups[ColorKey(cold)].Values),"Повторный Solve заново выделил поле неизменившегося цвета.");
            const int w=80,h=30;var barrier=new float[w*h];
            for(int y=0;y<=20;y++) barrier[y*w+10]=1;
            var around=new GridLightField(w,h,.2f,barrier);around.Solve(new[]{new GridLightSeed(15*w+2,cold)});
            var escaped=around.Pixels[15*w+12];Require(escaped.b>0 && Math.Abs(escaped.r/escaped.b-hue.r)<.00001,"Свет, обошедший стену, поменял оттенок.");
            line.Solve(new[]{new GridLightSeed(1,new Color(3,1,0))});
            Require(line.Pixels[1].r==1 && Math.Abs(line.Pixels[1].g-1f/3)<.00001 && line.Pixels[1].b==0,"Ограничение общей яркости исказило (3,1,0).");
            line.Solve(new[]{new GridLightSeed(1,Color.green)});
            Require(line.Pixels[5].g>0 && line.Pixels[5].r==0 && line.Pixels[5].b==0,"Намеренно зелёный источник потерял цвет.");
            Require(line.GroupCount==1,"Полный Solve не сбросил прежнюю цветовую палитру.");
        }

        private static void CheckLocalChanges()
        {
            const int w=340,h=170;var a=new float[w*h];
            for(int y=0;y<h;y++) a[y*w+170]=1;
            var strong=new GridLightSeed(80*w+150,new Color(1,.2f,.1f));
            var border=new GridLightSeed(3*w+2,new Color(.3f,.9f,.8f));
            var seeds=new List<GridLightSeed>{strong,new GridLightSeed(80*w+143,new Color(.15f,.25f,.8f)),new GridLightSeed(20*w+320,Color.white),border};
            var local=new GridLightField(w,h,.2f,a);local.Solve(seeds);
            seeds.RemoveAt(0);Check(Cell(strong.Index),"выключение сильного рядом со слабым");
            seeds.Insert(0,strong);Check(Cell(strong.Index),"включение сильного рядом со слабым");
            int door=80*w+170;
            a[door]=0;Check(Cell(door),"открытие герметичной двери");
            a[door]=.5f;Check(Cell(door),"частично прозрачная дверь");
            a[door]=1;Check(Cell(door),"закрытие двери");
            seeds.RemoveAt(seeds.Count-1);Check(Cell(border.Index),"выключение у края карты");
            seeds.Add(border);Check(Cell(border.Index),"включение у края карты");
            // A changed interior cell creates a region boundary through existing illumination.
            // Exterior seeds are omitted deliberately: only preserved boundary values can carry their light in.
            int patch=70*w+85;a[patch]=.3f;Check(Cell(patch),"вход света с неизменённой внешней границы");
            Require(local.RecomputedCells<(long)w*h,"Локальный патч пересчитал весь тестовый уровень.");
            var random=new System.Random(104);
            for(int step=0;step<32;step++)
            {
                if((step&1)==0)
                {
                    int slot=random.Next(seeds.Count),old=seeds[slot].Index,index;
                    do {index=random.Next(a.Length);} while(a[index]>=OpaqueThreshold);
                    seeds[slot]=new GridLightSeed(index,new Color((float)random.NextDouble(),(float)random.NextDouble(),(float)random.NextDouble()));
                    var oldCell=Cell(old);var newCell=Cell(index);
                    Check(Rectangle(Math.Min(oldCell.x,newCell.x),Math.Min(oldCell.y,newCell.y),Math.Max(oldCell.x,newCell.x)+1,Math.Max(oldCell.y,newCell.y)+1),"перемещение/цвет источника #"+step);
                }
                else
                {
                    int index;do {index=random.Next(a.Length);} while(seeds.Exists(s=>s.Index==index));
                    a[index]=step%3==0?1:step%3==1?.35f:0;
                    Check(Cell(index),"перемежающееся изменение поглощения #"+step);
                }
            }
            var beforeEmpty=(Color[])local.Pixels.Clone();
            var empty=local.Update(Array.Empty<GridLightSeed>(),new RectInt(4,4,0,0));
            Require(empty.width==0 && local.RecomputedCells==0,"Пустое событие выполнило пересчёт.");
            for(int i=0;i<a.Length;i++) Require(local.Pixels[i].Equals(beforeEmpty[i]),"Пустое событие изменило поле.");

            RectInt Cell(int index)=>new RectInt(index%w,index/w,1,1);
            void Check(RectInt changed,string name)
            {
                var before=(Color[])local.Pixels.Clone();var area=local.AffectedRegion(changed);
                var nearby=new List<GridLightSeed>();
                foreach(var seed in seeds) if(area.Contains(new Vector2Int(seed.Index%w,seed.Index/w))) nearby.Add(seed);
                Require(local.Update(nearby,changed).Equals(area),name+": неверно возвращена область.");
                Require(local.RecomputedCells==(long)area.width*area.height,name+": неверно посчитаны клетки.");
                var full=new GridLightField(w,h,.2f,a);full.Solve(seeds);
                var keys=new HashSet<int>(local.groups.Keys);keys.UnionWith(full.groups.Keys);
                foreach(int key in keys)
                {
                    local.groups.TryGetValue(key,out var localGroup);full.groups.TryGetValue(key,out var fullGroup);
                    for(int i=0;i<a.Length;i++)
                        if((localGroup?.Values[i]??0)!=(fullGroup?.Values[i]??0)) throw new InvalidOperationException(name+$": расходятся скалярные значения группы {key}, клетки #{i}.");
                }
                for(int i=0;i<a.Length;i++)
                {
                    if(!local.Pixels[i].Equals(full.Pixels[i])) throw new InvalidOperationException(name+$": расходятся цвета #{i}.");
                    if(!area.Contains(new Vector2Int(i%w,i/w)) && !local.Pixels[i].Equals(before[i])) throw new InvalidOperationException(name+$": изменилась внешняя клетка #{i}.");
                }
            }
        }
        private static void Require(bool condition,string message) {if(!condition) throw new InvalidOperationException("Сетка света: "+message);}
    }
}
