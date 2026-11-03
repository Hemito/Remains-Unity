using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Editor
{
    // Trace the actual quantized result back to a source; do not guess a blue patch's origin from its image.
    public static class GridLightDiagnostics
    {
        [Serializable] private sealed class SourceColor {public string id,kind;public Color color;}
        [Serializable] private sealed class ColorInventory {public SourceColor[] sources;}
        public static void WriteColorInventory(IReadOnlyList<LevelLightSource> sources,string path)
        {
            var inventory=new ColorInventory{sources=sources.Select(s=>new SourceColor{id=s.Id,kind=s.Kind.ToString(),color=s.Radiance}).ToArray()};
            File.WriteAllText(path,JsonUtility.ToJson(inventory,true));
        }
        [Serializable] private sealed class ColorReport
        {
            public string explanation="У каждого оттенка затухает одна общая яркость. Цвет не теряет R/G на расстоянии; между разными группами остаётся максимум RGB.";
            public Vector2 world;public Color field,windowContribution,groupHue,previousField;
            public string sourceId,sourceKind;public Color sourceRadiance,seedRadiance;
            public int steps,level;public float hueRatioError;
        }
        public static void WriteColorReport(GridLightField field,float[] absorption,LevelLightScene scene,
            IReadOnlyList<LevelLightSource> sources,Dictionary<string,GridLightSeed[]> seeds,string path)
        {
            // The actual blue-only probe traced in the preceding seed104 study, not a hand-picked new sample.
            var world=new Vector2(46.5f,365.7f);
            const string expected="window:factory:0:1,1:uHvgVXC2iZg1OtSd";
            var input=sources.Single(s=>s.Id==expected);
            Color hue=GridLightField.NormalizedColor(input.Radiance);
            int x0=Mathf.FloorToInt((world.x-scene.WorldRect.x)/field.CellSize),y0=Mathf.FloorToInt((world.y-scene.WorldRect.y)/field.CellSize);
            int selected=y0*field.Width+x0;
            int original=field.LevelForColor(input.Radiance,selected);
            if(original==0)
                throw new InvalidOperationException("Группа проверяемого окна не освещает прежнюю контрольную точку.");
            int at=selected,steps=0;string found=null;Color seedColor=default;
            for(;steps<=GridLightField.Maximum/field.StepLoss+1;steps++)
            {
                int q=field.LevelForColor(input.Radiance,at);
                foreach(var pair in seeds)
                {
                    foreach(var seed in pair.Value) if(seed.Index==at && GridLightField.NormalizedColor(seed.Radiance).Equals(hue) && Q(Mathf.Clamp01(seed.Radiance.maxColorComponent))==q)
                    {found=pair.Key;seedColor=seed.Radiance;break;}
                    if(found!=null) break;
                }
                if(found!=null) break;
                int x=at%field.Width;int[] neighbours={x>0?at-1:-1,x+1<field.Width?at+1:-1,at>=field.Width?at-field.Width:-1,at<field.Pixels.Length-field.Width?at+field.Width:-1};
                int next=-1;
                foreach(int n in neighbours)
                {
                    if(n<0) continue;int neighbour=field.LevelForColor(input.Radiance,n);
                    if(neighbour>q && (int)Math.Floor((neighbour-field.StepLoss)*(1.0-absorption[at]))==q) {next=n;break;}
                }
                if(next<0) throw new InvalidOperationException("Не восстановлен путь синего канала клетки "+at);
                at=next;
            }
            if(found==null) throw new InvalidOperationException("Путь цветовой группы не дошёл до источника.");
            var source=sources.Single(s=>s.Id==found);
            Color contribution=hue*(original/(float)GridLightField.Maximum);contribution.a=1;
            float error=Mathf.Max(Mathf.Abs(contribution.r/contribution.b-hue.r/hue.b),Mathf.Abs(contribution.g/contribution.b-hue.g/hue.b));
            if(error>1e-5 || contribution.r<=0 || contribution.g<=0) throw new InvalidOperationException("Цвет окна искажён затуханием в контрольной точке.");
            var report=new ColorReport{world=world,field=field.Pixels[selected],windowContribution=contribution,groupHue=hue,
                previousField=new Color(0,0,.1625f,1),sourceId=found,sourceKind=source.Kind.ToString(),sourceRadiance=source.Radiance,seedRadiance=seedColor,steps=steps,level=original,hueRatioError=error};
            File.WriteAllText(path,JsonUtility.ToJson(report,true));
            Debug.Log("Проверка сохранения цвета: "+JsonUtility.ToJson(report,true));
        }
        private static int Q(float c)=>Mathf.RoundToInt(c*GridLightField.Maximum);
    }
}
