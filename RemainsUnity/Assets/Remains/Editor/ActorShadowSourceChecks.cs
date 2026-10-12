using System;
using System.Collections.Generic;
using System.Reflection;
using Remains.Domain;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class ActorShadowSourceChecks
    {
        public static void RunHubBatch()
        {
            GameObject room=null;
            try
            {
                Run();
                room=AuthoredRoomVisual.Create("hub",RoomLayout.Load("hub"));
                var view=room.GetComponent<FactoryLevelView>();
                Vector2 actor=new(33,1.7f);view.Focus(actor);
                string left=null,middle=null,right=null;
                foreach(string id in view.LampIds)
                {
                    if(id.EndsWith(":vJGnXZBoX5BOIT4p",StringComparison.Ordinal)) left=id;
                    if(id.EndsWith(":otIYrGj5RimnIyMV",StringComparison.Ordinal)) middle=id;
                    if(id.EndsWith(":T7bpyfTNhj0wX4EY",StringComparison.Ordinal)) right=id;
                }
                Require(left!=null && middle!=null && right!=null,"В авторском хабе не найдены ID трёх ламп ванной: "+string.Join(",",view.LampIds));
                view.SyncLamps(new[]{middle},1,new[]{middle});
                var selector=(ActorShadowSources)typeof(FactoryLevelView).GetField("shadowSources",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(view);
                var lamps=(IReadOnlyList<ActorShadowSource>)typeof(ActorShadowSources).GetField("lamps",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(selector);
                foreach(var lamp in lamps)
                {
                    if(lamp.Id!=left && lamp.Id!=right && lamp.Id!=middle) continue;
                    string trace="";
                    if(lamp.EmissionPoints!=null) foreach(var sample in lamp.EmissionPoints)
                        trace+=$" [{sample.x:F3},{sample.y:F3}; площадь={sample.z:F3}; пропускание={view.ActorShadowTransmission(actor,(Vector2)sample):F4}]";
                    Debug.Log($"Лампа ванной: id={lamp.Id}, центр={lamp.Position}, точек={lamp.EmissionPoints?.Count??0}, мощность={lamp.Strength:F4};{trace}");
                }
                var selected=new List<ActorShadowSource>();
                for(float x=32.7f;x<=33.31f;x+=.05f)
                {
                    actor.x=x;view.SelectActorShadowSources(actor,selected);
                    string trace="";foreach(var lamp in selected) trace+=$" {lamp.Id}:{lamp.Strength:F4}@{lamp.Position}";
                    Debug.Log($"Тени ванной: актёр={actor};{trace}");
                    Require(selected.Exists(s=>s.Id==left) && selected.Exists(s=>s.Id==right),
                        $"В ванной хаба между двумя исправными лампами исчезает источник: актёр={actor};{trace}");
                }
                actor.x=33;view.SelectActorShadowSources(actor,selected);
                float before=selected.Find(s=>s.Id==left).Strength;
                view.SyncLamps(new[]{middle,right},2,new[]{middle,right});view.SelectActorShadowSources(actor,selected);
                Require(!selected.Exists(s=>s.Id==right) && selected.Exists(s=>s.Id==left) && selected.Find(s=>s.Id==left).Strength>before,
                    "Разбитая правая лампа ванной сохраняется в тенях или не меняет вклад левой.");
                Debug.Log("Ванная настоящего хаба: PASS — две лампы, движение через 5 см и уничтожение правой лампы.");
                EditorApplication.Exit(0);
            }
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
            finally {if(room!=null) UnityEngine.Object.DestroyImmediate(room);}
        }
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
            Array.Clear(grid,0,grid.Length);dark.Clear();
            // One half of a long sprite lamp is visible, while its other half (and a legacy centre)
            // is behind an opaque edge. The real emitter must remain a source of shadow.
            for(int y=0;y<16;y++) grid[24,y]=1;
            var footprint=new[]{new Vector3(1.0625f,1.0625f,1),new Vector3(1.0625f,3.0625f,3)};
            var partialActor=new Vector2(5.0625f,2.0625f);
            selector.Configure(grid,Vector2.zero,.125f,Matrix4x4.identity,
                new[]{new ActorShadowSource("finite",footprint[0],.5f,4,emissionPoints:footprint)},1);
            float closed=selector.Visibility(partialActor,footprint[0],doors),open=selector.Visibility(partialActor,footprint[1],doors);
            Require(closed==0 && open>0,
                $"Проверочная геометрия не разделяет закрытую и видимую половины лампы: closed={closed}, open={open}.");
            selector.Select(partialActor,dark,doors,3,output);
            Require(output.Count==1 && Vector2.Distance(output[0].Position,footprint[1])<.00001f &&
                output[0].Strength>0 && output[0].Strength<1,
                "Частично открытый силуэт лампы исчезает или проецируется от закрытой точки.");
            Array.Clear(grid,0,grid.Length);
            selector.Configure(grid,Vector2.zero,.125f,Matrix4x4.identity,
                new[]{new ActorShadowSource("left",new Vector2(1,2),.5f,4,emissionPoints:footprint),
                    new ActorShadowSource("right",new Vector2(5,2),.5f,4)},1);
            selector.Select(new Vector2(3,2),dark,doors,3,output);
            Require(output.Count==2,"Две видимые лампы дают только один источник тени.");
            float leftBefore=output.Find(s=>s.Id=="left").Strength;
            dark.Add("right");selector.Select(new Vector2(3,2),dark,doors,3,output);
            Require(output.Count==1 && output[0].Id=="left" && output[0].Strength>leftBefore,
                "Уничтожение второй лампы не меняет нормированный вклад первой.");
            selector.Clear();selector.Select(new Vector2(5,2),dark,doors,3,output);
            Require(output.Count==0,"Освобождённое окно сохраняет источники тени.");
            Debug.Log("Источники тени персонажа: PASS — порядок, погашенная лампа, дверь, стена, 5% балки, нормирование и освобождение окна.");
        }
        static void Require(bool condition,string message) {if(!condition) throw new InvalidOperationException(message);}
    }
}
