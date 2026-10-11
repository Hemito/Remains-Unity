using System;
using System.Collections.Generic;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Editor
{
    public static class ActorShadowSourceChecks
    {
        public static void Run()
        {
            var selector=new ActorShadowSources();
            var grid=new float[48,32];
            var doors=new List<(Rect,double)>();
            var dark=new HashSet<string>(StringComparer.Ordinal);
            var output=new List<ActorShadowSource>();
            var lamps=new[]{new ActorShadowSource("b",new Vector2(1,2),.2f,4),
                new ActorShadowSource("a",new Vector2(1,2),.2f,4)};
            selector.Configure(grid,Vector2.zero,.125f,Matrix4x4.identity,lamps,0);
            selector.Select(new Vector2(5,2),dark,doors,3,output);
            Require(output.Count==2 && output[0].Id=="a" && Mathf.Abs(output[0].Strength-.5f)<.00001f,
                "Равные источники не имеют устойчивого порядка и нормированного вклада.");
            dark.Add("a");selector.Select(new Vector2(5,2),dark,doors,3,output);
            Require(output.Count==1 && output[0].Id=="b","Выключенная лампа участвует в выборе источников тени.");
            doors.Add((new Rect(2,0,.2f,4),1));selector.Select(new Vector2(5,2),dark,doors,3,output);
            Require(output.Count==0,"Закрытая дверь не перекрывает источник тени.");
            doors.Clear();
            for(int y=0;y<32;y++) grid[24,y]=1;
            selector.Select(new Vector2(5,2),dark,doors,3,output);
            Require(output.Count==0,"Сплошная стена не перекрывает источник тени.");
            Array.Clear(grid,0,grid.Length);
            float beam=1-Mathf.Pow(.05f,.25f);
            for(int y=0;y<32;y++) for(int x=22;x<26;x++) grid[x,y]=beam;
            float through=selector.Visibility(new Vector2(5,2),new Vector2(1,2),doors);
            Require(Mathf.Abs(through-.05f)<.00001f,$"Четыре текселя балки пропускают {through}, ожидалось 0,05.");
            selector.Configure(grid,Vector2.zero,.125f,Matrix4x4.identity,lamps,1);
            selector.Select(new Vector2(5,2),dark,doors,3,output);
            Require(output.Count==1 && Mathf.Abs(output[0].Strength-(.05f/1.05f))<.00001f,
                "Источник не нормирован на общий свет с учётом фонового вклада.");
            selector.Clear();selector.Select(new Vector2(5,2),dark,doors,3,output);
            Require(output.Count==0,"Освобождённое окно сохраняет источники тени.");
            Debug.Log("Источники тени персонажа: PASS — порядок, погашенная лампа, дверь, стена, 5% балки, нормирование и освобождение окна.");
        }
        static void Require(bool condition,string message) {if(!condition) throw new InvalidOperationException(message);}
    }
}
