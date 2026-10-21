using System;
using System.Collections.Generic;
using Remains.Domain;
using Remains.Serialization;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class PropImpactIndexChecks
    {
        public static void RunBatch()
        {try {Run();EditorApplication.Exit(0);} catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}}
        public static void Run()
        {
            Anatomy.Load();Remains.Content.ActorTypes.Load();
            CheckSerializedHistory();
            var world=new WorldState();
            var player=new ActorState {id="player",player=true,connected=true,level="l",x=0};world.actors.Add(player);
            var moving=new ActorState {id="moving",level="l",x=3.8f};world.actors.Add(moving);
            for(int i=0;i<5000;i++) world.actors.Add(new ActorState {id="sleep"+i,level="other",x=i*8});
            var index=new PropImpactTargetIndex();index.Refresh(world);
            var hits=new List<ActorState>();
            moving.x=8.2f; // No Changed/publication event between simulation and impact query.
            index.SynchronizeActive();index.Query("l",new Rect(8,0,.5f,1),hits);
            Require(hits.Contains(moving),"Пересечение нескольких bucket до события публикации потеряло текущую цель");
            Require(index.LastSynchronized==2 && index.LastVisited<=2,"Синхронизация/запрос перебирают спящих актёров другого уровня");
            moving.x=-4.2f;index.SynchronizeActive();index.Query("l",new Rect(-4.5f,0,.5f,1),hits);
            Require(hits.Contains(moving),"Движение влево до события оставило старый bucket");
            int synchronized=index.LastSynchronized;
            var propOnly=new HashSet<string> {"props:unrelated"};index.Refresh(world,propOnly);
            Require(index.LastVisited==0,"Несвязанное событие пересобирает actor index");
            // Replica publication replaces the root and only declared actor objects. The 5000 sleepers
            // must stay untouched; a reconciliation-only actor replacement has the same addressing contract.
            var next=new WorldState {worldId=world.worldId,actors=new List<ActorState>(world.actors)};
            index.Refresh(next,new HashSet<string> {"props:unrelated","actorsExtra:moving"});
            Require(index.LastVisited==0,"Новый корень или похожий префикс вызвал полный actor rebuild");
            var replacement=StateJson.Copy(moving);replacement.x=12.2f;next.actors[1]=replacement;
            index.Refresh(next,new HashSet<string> {"actors:moving"});
            Require(index.LastVisited==1,"Замена одного actor обошла весь мир");
            index.Query("l",new Rect(12,0,.5f,1),hits);
            Require(hits.Contains(replacement) && !hits.Contains(moving),"Адресная замена оставила старую ссылку цели");
            next.actors.RemoveAt(1);index.Refresh(next,new HashSet<string> {"actors:moving"});
            index.Query("l",new Rect(12,0,.5f,1),hits);
            Require(!hits.Contains(replacement),"Адресное удаление оставило цель в bucket");
            next.actors.Insert(1,moving);index.Refresh(next,null);
            Require(index.LastVisited>=next.actors.Count,"Null parts не пересобрал состав актёров");
            var disconnected=StateJson.Copy(player);disconnected.connected=false;next.actors[0]=disconnected;
            index.Refresh(next,new HashSet<string> {"actors:player"});
            Require(!index.HasLevel("l"),"Отключение последнего игрока оставило активный уровень");
            var connected=StateJson.Copy(player);next.actors[0]=connected;
            index.Refresh(next,new HashSet<string> {"actors:player"});
            Require(index.HasLevel("l"),"Возвращение игрока не восстановило активный уровень");
            var other=new WorldState {actors=new List<ActorState> {player,moving}};
            index.Refresh(other,new HashSet<string>());
            Require(index.LastVisited>=other.actors.Count,"Другой worldId ошибочно принят за разреженное продолжение");
            index.Refresh(world); // Restore the original fixture for repeated moving queries below.
            var area=new Rect(-4.5f,0,.5f,1);
            for(int i=0;i<1000;i++) {index.SynchronizeActive();index.Query("l",area,hits);Require(hits.Contains(moving),"Повтор запроса потерял actor");}
            Debug.Log($"PropImpactIndexChecks: PASS — 5002 actors, active synchronized={synchronized}, near candidates={index.LastVisited}; in-place crossing before Changed, inactive level excluded, unrelated event visits=0.");
        }
        private static void CheckSerializedHistory()
        {
            var prop=new PropState {id="size-box",definition="woodbox",level="inactive",mass=50,width=1,height=1,anchorHeight=0,
                impacts=new PropImpactJournal {sampleGeneration=4,sampleSequence=60}};
            PropImpactSample Sample(uint sequence)=>new() {world="size-world",prop=prop.id,owner="owner",level=prop.level,generation=4,sequence=sequence,
                id=PropImpactRules.SampleId("size-world",prop.id,4,sequence),at=sequence/30d,x=1,y=1,vx=6,vy=0,width=1,height=1,anchorHeight=0,mass=50};
            var source=Sample(1);
            var contact=new PropImpactContact {id=source.id+":target:0",target="target",lifecycle=0,generationBefore=4,generationAfter=5,source=source,
                until=2,targetVx=1,targetVy=0,propVx=2,propVy=0,damage=10};
            PropImpactRules.Validate(contact);prop.impacts.contacts.Add(contact);prop.impacts.samples.Add(source);
            string one=StateJson.ToJson(prop);int oneBytes=Encoding.UTF8.GetByteCount(one);
            for(uint sequence=2;sequence<=60;sequence++) prop.impacts.samples.Add(Sample(sequence));
            string sixty=StateJson.ToJson(prop);int sixtyBytes=Encoding.UTF8.GetByteCount(sixty);
            Require(one==sixty && !sixty.Contains("\"samples\""),"Транзитные образцы увеличивают persisted prop payload");
            var restored=StateJson.FromJson<PropState>(sixty);
            Require(restored.impacts.samples.Count==0 && restored.impacts.sampleGeneration==4 && restored.impacts.sampleSequence==60 &&
                restored.impacts.contacts.Count==1 && StateJson.ToJson(restored.impacts.contacts[0])==StateJson.ToJson(contact),
                "Сжатие образцов потеряло watermark или принятый pending contact");
            Debug.Log($"PropImpactIndexChecks payload UTF8: one transient sample={oneBytes} bytes, sixty={sixtyBytes} bytes; exact equal; pending contacts=1, durable watermark=4/60, restored transient=0. Это размер сериализации, не измерение bandwidth/CPU полного завода.");
        }
        private static void Require(bool valid,string reason) {if(!valid) throw new InvalidOperationException(reason);}
    }
}
