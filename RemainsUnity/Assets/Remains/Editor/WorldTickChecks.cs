using System;
using Remains.Domain;
using Remains.Persistence;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Transport v2, step 1: a receiver that starts from the host's full world and applies only ticks ends with exactly
    // the host's world text, through random changes, replacements, additions, removals and reorders.
    public static class WorldTickChecks
    {
        public static void RunBatch()
        {
            try {Run();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void Run()
        {
            var random=new System.Random(104);
            var host=new WorldState {worldId="tick-check"};
            for(int i=0;i<40;i++) host.containers.Add(new ContainerState {id="c"+i,x=i});
            for(int i=0;i<20;i++) host.props.Add(new PropState {id="p"+i,x=i});
            for(int i=0;i<10;i++) host.actors.Add(new ActorState {id="a"+i,health=100,x=i});
            for(int i=0;i<10;i++) host.doors.Add(new DoorState {id="d"+i});
            for(int i=0;i<6;i++) host.projectiles.Add(new ProjectileState {launch=new AttackEvent {id="f"+i},flightSpeed=10});
            WorldChanges.Track(host);
            var client=StateJson.FromJson<WorldState>(StateJson.ToJson(host));
            var composer=new WorldTickComposer();
            var clientTexts=new WorldTickTexts(client);
            var live=StateJson.FromJson<WorldState>(StateJson.ToJson(host));   // the client's live world (detached copies)
            Require(composer.Next(host,part=>WorldChanges.IsMarked(host,part))==null,"Первый вызов должен только запомнить мир.");
            Require(composer.Next(host,part=>WorldChanges.IsMarked(host,part))==null,"Тик без изменений не пуст.");
            int ticks=0,next=1000;
            for(int round=0;round<300;round++)
            {
                switch(random.Next(9))
                {
                    case 8: {var f=host.projectiles[random.Next(host.projectiles.Count)];if(!f.finished) {f.traveled+=1;if(f.traveled>=3) f.finished=true;}break;}   // flight progress and landing, no mark
                    case 7: {var a=host.actors[random.Next(host.actors.Count)];a.owner="o"+round;a.revision++;break;}   // revision only, no mark
                    case 0: {var c=host.containers[random.Next(host.containers.Count)];c.x+=1;WorldChanges.Mark(host,c);break;}
                    case 1: {int i=random.Next(host.actors.Count);var a=host.actors[i].Copy();a.health-=1;host.actors[i]=a;break;}   // replaced object, no mark
                    case 2: host.props.Add(new PropState {id="p"+(next++),x=round});break;
                    case 3: if(host.props.Count>1) host.props.RemoveAt(random.Next(host.props.Count));break;
                    case 4: {int i=random.Next(host.doors.Count),j=random.Next(host.doors.Count);(host.doors[i],host.doors[j])=(host.doors[j],host.doors[i]);break;}
                    case 5: host.revision++;break;
                    case 6: {var p=host.props[random.Next(host.props.Count)];p.y+=2;WorldChanges.Mark(host,p);break;}
                }
                if(random.Next(3)!=0) continue;   // several changes per tick
                var tick=composer.Next(host,part=>WorldChanges.IsMarked(host,part));
                WorldChanges.Clear(host);
                if(tick==null) continue;
                Require(tick.seq==ticks+1,$"Номер тика {tick.seq}, ожидается {ticks+1}.");
                ticks++;
                // Through the wire format, as the transport will send it.
                var wire=StateJson.FromJson<WorldTick>(StateJson.ToJson(tick));
                var parsed=WorldTickApplier.Parse(client,wire);
                WorldTickApplier.Apply(client,parsed,null);clientTexts.Apply(wire);
                var nextLive=WorldTickLists.ShallowCopy(live);WorldTickApplier.Apply(nextLive,parsed,Remains.Serialization.ValidatedStateCopy.Copy);live=nextLive;
                string expected=StateJson.ToJson(host),actual=StateJson.ToJson(client);
                Require(expected==actual,$"Мир по тикам разошёлся с миром хоста после тика {tick.seq}.");
                Require(StateJson.ToJson(live)==expected,$"Живой мир по тикам разошёлся с миром хоста после тика {tick.seq}.");
                foreach(var actor in live.actors) Require(!client.actors.Exists(a=>ReferenceEquals(a,actor)),"Живой мир делит объект актёра с каноническим.");
                // A snapshot composed from the composer's texts is exactly the world's text (after a slow tick).
                Require(composer.Composition(WorldTickLists.Template(host)).Json==expected,$"Снимок из тиков разошёлся с миром хоста после тика {tick.seq}.");
                // The receiver's copy from its tick texts is byte-identical (its SHA acknowledges the host's recovery copy).
                Require(clientTexts.Composition(WorldTickLists.Template(host)).Json==expected,$"Копия получателя из текстов тиков разошлась с хостом после тика {tick.seq}.");
            }
            Require(ticks>40,"Слишком мало тиков для проверки: "+ticks);

            // A malformed tick leaves the world untouched.
            string before=StateJson.ToJson(client);
            var broken=new WorldTick {world=client.worldId,seq=ticks+1};
            broken.lists.Add(new WorldTickList {name="containers",keys=new[]{"containers:c0","containers:c1"},texts=new[]{StateJson.ToJson(client.containers[0]),"{не json"}});
            bool rejected=false;
            try {WorldTickApplier.Apply(client,broken);} catch(Exception) {rejected=true;}
            Require(rejected && StateJson.ToJson(client)==before,"Повреждённый тик изменил мир.");
            Debug.Log($"Тики мира: PASS — {ticks} тиков, мир получателя совпадает с миром хоста побайтно; повреждённый тик отклонён без изменений.");
        }
        // Cost on the seed104 factory world: a tick with a typical change set versus a snapshot composition.
        public static void BenchBatch()
        {
            try
            {
                string path=System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../../.reference/cpu104-baseline3/fixture/world.json"));
                var world=StateJson.FromTrustedJson<WorldState>(System.IO.File.ReadAllText(path));
                WorldChanges.Track(world);
                var composer=new WorldTickComposer();var watch=new System.Diagnostics.Stopwatch();
                composer.Next(world,part=>WorldChanges.IsMarked(world,part));
                var client=StateJson.FromTrustedJson<WorldState>(StateJson.ToJson(world));
                double tickMs=0,applyMs=0,composeMs=0,idleMs=0;int rounds=20,bytes=0;
                SnapshotPartCache.Compose(world);   // warm its memory as the game does
                for(int round=0;round<rounds;round++)
                {
                    // A busy moment: 30 actors updated by their owners, 5 bodies moved, the world revision.
                    for(int i=0;i<30 && i<world.actors.Count;i++) {var a=world.actors[(i*7+round)%world.actors.Count];a.x+=.01f;WorldChanges.Mark(world,a);}
                    for(int i=0;i<5;i++) {var p=world.props[(i*13+round)%world.props.Count];p.x+=.01f;WorldChanges.Mark(world,p);}
                    world.revision++;
                    watch.Restart();var tick=composer.Next(world,part=>WorldChanges.IsMarked(world,part));watch.Stop();tickMs+=watch.Elapsed.TotalMilliseconds;
                    string wire=StateJson.ToJson(tick);bytes+=wire.Length;
                    watch.Restart();WorldTickApplier.Apply(client,StateJson.FromJson<WorldTick>(wire));watch.Stop();applyMs+=watch.Elapsed.TotalMilliseconds;
                    watch.Restart();SnapshotPartCache.Compose(world);watch.Stop();composeMs+=watch.Elapsed.TotalMilliseconds;   // also clears the marks
                    watch.Restart();composer.Next(world,part=>WorldChanges.IsMarked(world,part));watch.Stop();idleMs+=watch.Elapsed.TotalMilliseconds;
                }
                Require(StateJson.ToJson(client)==StateJson.ToJson(world),"Мир по тикам разошёлся с миром завода.");
                Debug.Log($"Тики на заводе seed104 ({world.containers.Count} контейнеров, {world.actors.Count} актёров, {world.props.Count} тел): "+
                    $"тик {tickMs/rounds:F2} мс, применение {applyMs/rounds:F2} мс, тик без изменений {idleMs/rounds:F2} мс, {bytes/rounds} символов; "+
                    $"для сравнения снимок (Compose) {composeMs/rounds:F2} мс. Editor batch, не Player.");
                EditorApplication.Exit(0);
            }
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        private static void Require(bool condition,string message) {if(!condition) throw new InvalidOperationException(message);}
    }
}
