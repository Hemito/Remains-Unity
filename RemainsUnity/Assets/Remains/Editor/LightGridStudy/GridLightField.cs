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

    // Editor-only comparison, full rebuild. This is neither radiative transfer nor a bounce solver.
    // Four cardinal neighbours give Manhattan distance and cannot cut diagonally across a sealed corner.
    // Channels travel independently: max(R),max(G),max(B), so red does not erase a weaker blue source.
    // This is an explicit coloured extension of bounded block light, not Minecraft's exact colour model.
    public sealed class GridLightField
    {
        public const int DisplayLevels=15,Sublevels=16,Maximum=DisplayLevels*Sublevels;
        public const float RadianceAtMaximum=1f,FalloffMetres=12f,OpaqueThreshold=0.99f;
        private readonly float[] absorption;
        private readonly byte[] values;
        private readonly int[] next,previous,heads;
        private int queued;
        public int Width {get;}
        public int Height {get;}
        public float CellSize {get;}
        public int StepLoss {get;}
        public float EffectiveMaximumRange=>Maximum*CellSize/StepLoss;
        public Color[] Pixels {get;}
        // Array payloads only, excluding CLR headers, caller-owned seeds/absorption and GPU upload.
        public long AllocatedBytes=>(long)Pixels.Length*(16+1+4+4)+heads.Length*4L;
        public long ReferencedAbsorptionBytes=>(long)absorption.Length*4;
        public long PoppedCells {get;private set;}
        public long NeighbourChecks {get;private set;}
        public long ImprovedCells {get;private set;}
        public int PeakQueuedCells {get;private set;}
        public int SeedCount {get;private set;}

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
            Pixels=new Color[absorption.Length];values=new byte[absorption.Length];
            next=new int[absorption.Length];previous=new int[absorption.Length];heads=new int[Maximum+1];
        }

        public void Solve(IReadOnlyList<GridLightSeed> seeds)
        {
            if(seeds==null) throw new ArgumentNullException(nameof(seeds));
            for(int i=0;i<seeds.Count;i++)
            {
                var seed=seeds[i];
                if(seed.Index<0 || seed.Index>=values.Length) throw new ArgumentException($"Источник {i}: индекс {seed.Index} вне световой сетки.");
                if(absorption[seed.Index]>=OpaqueThreshold) throw new ArgumentException($"Источник {i}: клетка {seed.Index} непрозрачна. Сначала разместите излучение на открытой поверхности формы.");
                if(!Valid(seed.Radiance.r) || !Valid(seed.Radiance.g) || !Valid(seed.Radiance.b)) throw new ArgumentException($"Источник {i}: некорректная яркость {seed.Radiance}.");
            }
            SeedCount=seeds.Count;PoppedCells=NeighbourChecks=ImprovedCells=0;PeakQueuedCells=0;
            for(int i=0;i<Pixels.Length;i++) Pixels[i]=new Color(0,0,0,1);
            for(int channel=0;channel<3;channel++)
            {
                Array.Clear(values,0,values.Length);Array.Fill(previous,-2);Array.Fill(heads,-1);queued=0;
                // Install EVERY source before propagation. Descending buckets give order-independent max paths.
                for(int i=0;i<seeds.Count;i++)
                {
                    var seed=seeds[i];float c=channel==0?seed.Radiance.r:channel==1?seed.Radiance.g:seed.Radiance.b;
                    int q=(int)Math.Floor(Math.Min(1,c/RadianceAtMaximum)*Maximum+0.5);
                    if(q>values[seed.Index]) Put(seed.Index,q);
                }
                for(int level=Maximum;level>StepLoss;level--)
                    while(heads[level]>=0)
                    {
                        int index=heads[level];Remove(index,level);PoppedCells++;
                        int x=index%Width,remainder=level-StepLoss;
                        if(x>0) Relax(index-1,remainder);
                        if(x+1<Width) Relax(index+1,remainder);
                        if(index>=Width) Relax(index-Width,remainder);
                        if(index<values.Length-Width) Relax(index+Width,remainder);
                    }
                for(int i=0;i<Pixels.Length;i++)
                {
                    var c=Pixels[i];float value=values[i]/(float)Maximum;
                    if(channel==0)c.r=value;else if(channel==1)c.g=value;else c.b=value;
                    Pixels[i]=c;
                }
            }
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
            return "PASS: герметичная стена, темнота, ограниченное RGB-перекрытие, слабый цвет, удаление источника, порядок, частичное поглощение.";
        }
        private static void Require(bool condition,string message) {if(!condition) throw new InvalidOperationException("Сетка света: "+message);}
    }
}
