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
        [Serializable] private sealed class BlueReport
        {
            public string explanation="Каждый канал теряет одинаковое абсолютное число уровней. У холодного белого R/G заканчиваются раньше B; оставшийся свет становится насыщенно-синим. Это дефект цветового затухания пробы, не цвет тени.";
            public Vector2 world;public Color field;
            public string sourceId,sourceKind;public Color sourceRadiance,seedRadiance;
            public int blueSteps;public Color exampleInput=new(.4f,.5f,.7f,1),exampleAfterLoss=new(0,0,.2f,1);
        }
        public static void WriteBlueReport(GridLightField field,float[] absorption,LevelLightScene scene,
            IReadOnlyList<LevelLightSource> sources,Dictionary<string,GridLightSeed[]> seeds,Rect visible,string path)
        {
            int selected=-1;float score=0;
            for(int y=0;y<field.Height;y++) for(int x=0;x<field.Width;x++)
            {
                var p=scene.WorldRect.position+new Vector2((x+.5f)*field.CellSize,(y+.5f)*field.CellSize);
                if(!visible.Contains(p)) continue;
                int i=y*field.Width+x;var c=field.Pixels[i];float s=c.b>.04f?(c.b-Mathf.Max(c.r,c.g))/c.b:0;
                if(s==score && selected>=0 && c.b>field.Pixels[selected].b) {selected=i;continue;}
                if(s>score) {score=s;selected=i;}
            }
            if(selected<0) throw new InvalidOperationException("В контрольном зале не найдено синее пятно для диагностики.");
            int at=selected,steps=0;string found=null;Color seedColor=default;
            for(;steps<=GridLightField.Maximum/field.StepLoss+1;steps++)
            {
                int q=Q(field.Pixels[at].b);
                foreach(var pair in seeds)
                {
                    foreach(var seed in pair.Value) if(seed.Index==at && Q(Mathf.Clamp01(seed.Radiance.b))==q)
                    {found=pair.Key;seedColor=seed.Radiance;break;}
                    if(found!=null) break;
                }
                if(found!=null) break;
                int x=at%field.Width;int[] neighbours={x>0?at-1:-1,x+1<field.Width?at+1:-1,at>=field.Width?at-field.Width:-1,at<field.Pixels.Length-field.Width?at+field.Width:-1};
                int next=-1;
                foreach(int n in neighbours)
                {
                    if(n<0) continue;int neighbour=Q(field.Pixels[n].b);
                    if(neighbour>q && (int)Math.Floor((neighbour-field.StepLoss)*(1.0-absorption[at]))==q) {next=n;break;}
                }
                if(next<0) throw new InvalidOperationException("Не восстановлен путь синего канала клетки "+at);
                at=next;
            }
            if(found==null) throw new InvalidOperationException("Путь синего света не дошёл до источника.");
            var source=sources.Single(s=>s.Id==found);
            var report=new BlueReport{world=scene.WorldRect.position+new Vector2((selected%field.Width+.5f)*field.CellSize,(selected/field.Width+.5f)*field.CellSize),
                field=field.Pixels[selected],sourceId=found,sourceKind=source.Kind.ToString(),sourceRadiance=source.Radiance,seedRadiance=seedColor,blueSteps=steps};
            File.WriteAllText(path,JsonUtility.ToJson(report,true));
            Debug.Log("Диагностика синего света: "+JsonUtility.ToJson(report,true));
        }
        private static int Q(float c)=>Mathf.RoundToInt(c*GridLightField.Maximum);
    }
}
