using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Numbered factories and their kinds (user 2026-11-06, world-structure): which levels are numbers, which routes they
    // allow, that a number always names the same kind, and that each kind really is populated as it claims.
    public static class FactoryProfileChecks
    {
        public static void RunBatch()
        {
            try {Anatomy.Load();WeaponCatalog.Load();ActorTypes.Load();Run();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void Run()
        {
            Numbers();Routes();Layouts();StoredVersions();
            var catalog=FactoryProfileCatalog.Load();
            Choice(catalog);
            Populations(catalog);
            Debug.Log("Номерные заводы: PASS — номера, маршруты, постоянный вид номера и население каждого вида.");
        }
        private static void Numbers()
        {
            Require(WorldFactories.TryIndex("factory:-27",out int index) && index==-27,"отрицательный номер не читается");
            Require(WorldFactories.TryIndex("factory:0",out index) && index==0,"основной завод не читается");
            foreach(string level in new[]{"hub","factory:","factory:x","factory:+1","factory:007","factory:-0","factory:10000",null})
                Require(!WorldFactories.IsFactoryLevel(level),"это не номер завода: "+level);
        }
        private static void Routes()
        {
            var actor=new ActorState {id="a",connected=true,level="hub",health=100};
            Require(TravelRules.Rejection(actor,"hub","factory:-1",0,0)==null,"из хаба нельзя на завод −1");
            actor.level="factory:-1";
            Require(TravelRules.Rejection(actor,"factory:-1","hub",0,0)==null,"с завода −1 нельзя в хаб");
            Require(TravelRules.Rejection(actor,"factory:-1","factory:2",0,0)!=null,"между заводами нет прямого пути");
        }
        // Only the entry is fixed and a factory reaches for rooms it has not used (user 2026-11-06).
        private static void Layouts()
        {
            var rooms=new List<Remains.RoomFormat.RoomFile>(FactoryCatalogAsset.Load().Templates.Values);
            string entry=rooms.Single(r=>r.Options.TryGetValue("tip",out string tip) && tip=="beg0").Template;
            int total=rooms.Count(r=>!(r.Options.TryGetValue("tip",out string tip) && tip=="back"));
            int samples=0,distinct=0,sameTop=0,lost=0,cut=0;
            var firstTop=(string)null;
            foreach(ulong seed in new ulong[]{104,105,777})
                for(int index=-20;index<=20;index++)
                {
                    var layout=Remains.RoomFormat.FactoryLayoutGenerator.Generate(rooms,"plant",seed,index,4,21);
                    var first=layout.Cell(0,0);
                    Require(first.Template==entry && first.Start && !first.Mirror,$"seed {seed}, завод {index}: первая комната не вход");
                    Require(layout.Cells.Count(c=>c.Start)==1,$"seed {seed}, завод {index}: обязательна не одна комната");
                    Require(layout.Seams.Any(s=>s.X==0 && s.Y==0),$"seed {seed}, завод {index}: из входа нет прохода");
                    foreach(string issue in layout.Issues)
                    {
                        Require(issue.StartsWith("Нет прохода от входа",StringComparison.Ordinal),$"seed {seed}, завод {index}: {issue}");
                        cut++;lost+=issue.Count(c=>c==',');
                    }
                    string top=string.Join("|",Enumerable.Range(1,3).Select(x=>layout.Cell(x,0).Template));
                    firstTop??=top;if(top==firstTop) sameTop++;
                    samples++;distinct+=layout.Cells.Select(c=>c.Template).Distinct().Count();
                }
            Require(sameTop<samples/4,$"верхний ряд почти не меняется: {sameTop} из {samples}");
            float average=(float)distinct/samples;
            Require(lost==0,$"есть оторванные клетки: {lost} в {cut} раскладках");
            Require(average>=total-2,$"заводу в среднем не хватает комнат: {average:0.0} из {total}");
            Debug.Log($"Номерные заводы: раскладок {samples}, разных комнат в среднем {average:0.0} из {total}, совпадений верхнего ряда {sameTop}, раскладок с оторванными клетками {cut}, таких клеток всего {lost}.");
        }
        // A saved layout of the previous generator is read as it is, a world must keep loading after the rule changed.
        private static void StoredVersions()
        {
            var world=NewGameWorld.Create(104);var catalog=FactoryCatalogAsset.Load();var record=world.factories.Single();
            var layout=Remains.RoomFormat.FactoryLayoutReader.Read(record.layout,"проверка");
            layout.GeneratorVersion=Remains.RoomFormat.FactoryLayoutGenerator.OldestStoredVersion;record.layout=layout.Write();
            Require(WorldFactories.Require(world,record.level,FactoryCatalogAsset.DefinitionId,catalog.ContentFingerprint).GeneratorVersion==layout.GeneratorVersion,"прежняя раскладка не читается");
            layout.GeneratorVersion=Remains.RoomFormat.FactoryLayoutGenerator.OldestStoredVersion-1;record.layout=layout.Write();
            bool rejected=false;
            try {WorldFactories.Require(world,record.level,FactoryCatalogAsset.DefinitionId,catalog.ContentFingerprint);}
            catch(System.IO.InvalidDataException) {rejected=true;}
            Require(rejected,"раскладка без маркеров ролей принята");
        }
        private static void Choice(FactoryProfileCatalog catalog)
        {
            Require(catalog.For(104,0)==catalog.Main && catalog.Main.OriginalRooms && catalog.Main.OriginalRaiders,"завод 0 должен остаться оригинальным");
            var seen=new HashSet<string>();
            for(int i=-40;i<=40;i++)
            {
                seen.Add(catalog.For(104,i).id);
                Require(catalog.For(104,i).id==catalog.For(104,i).id,"номер меняет вид");
            }
            Require(seen.Count==catalog.profiles.Length,$"за 81 номер встретилось {seen.Count} видов из {catalog.profiles.Length}");
            Require(Enumerable.Range(1,40).Any(i=>catalog.For(105,i).id!=catalog.For(104,i).id),"другой мир обязан давать другие виды");
        }
        private static void Populations(FactoryProfileCatalog catalog)
        {
            var world=NewGameWorld.Create(104);
            var factories=FactoryCatalogAsset.Load();
            var main=Count(world,"factory:0");
            // One number of each kind; negative when possible, so the sign goes through every step.
            var numbers=catalog.profiles.Select(p=>Enumerable.Range(1,200).Select(i=>-i).First(i=>catalog.For(104,i)==p)).ToArray();
            string looked=$"основной {main}";
            try
            {
                foreach(int number in numbers)
                {
                    var record=FactoryPreparation.Create(world,factories,number);
                    var profile=catalog.Saved(record);
                    Require(profile==catalog.For(104,number),"завод не записал свой вид");
                    var count=Count(world,record.level);
                    looked+=$"; {number} {profile.id}: {count}";
                    Debug.Log("Номерные заводы: "+looked);
                    Require(count.containers>0 && count.creatures>0,$"завод {number} пуст: {count}");
                    Require(world.actors.Where(a=>a.level==record.level).All(a=>a.id.StartsWith(record.level+":",StringComparison.Ordinal)),"ID жителей без номера завода");
                    if(profile.id=="remains:factory:raiders")
                    {
                        Require(count.raiders*2>count.creatures,$"на заводе рейдеров их не большинство: {count}");
                        Require(count.shooters*3>=count.raiders,$"на заводе рейдеров мало стрелков: {count}");
                    }
                    if(profile.id=="remains:factory:guarded") Require(count.robots*2>count.creatures,$"на нетронутом заводе боты не большинство: {count}");
                    if(profile.id=="remains:factory:infested") Require(count.monsters*4>count.creatures*3,$"на заражённом заводе мало тварей: {count}");
                }
                Require(world.actors.Select(a=>a.id).Distinct().Count()==world.actors.Count,"ID жителей разных заводов совпали");
            }
            finally {FactoryContentSources.Clear();}
        }
        private struct Count_
        {
            public int creatures,raiders,shooters,robots,monsters,containers;
            public override string ToString()=>$"существ {creatures} (рейдеров {raiders}, стрелков {shooters}, ботов {robots}, тварей {monsters}), контейнеров {containers}";
        }
        private static Count_ Count(WorldState world,string level)
        {
            FactoryContentSources.Prepare(world,level);
            RoomContentInstaller.Apply(world,RoomContentBatch.Read(RoomAuthoring.Load(level,world),level));
            var result=new Count_ {containers=world.containers.Count(c=>c.level==level)};
            foreach(var actor in world.actors.Where(a=>a.level==level && !a.player))
            {
                result.creatures++;
                var behavior=ActorTypes.Get(actor.actorType).behavior;
                if(actor.actorType.StartsWith("raider",StringComparison.Ordinal))
                {
                    result.raiders++;
                    if(actor.actorType.StartsWith("raider5",StringComparison.Ordinal) || actor.actorType.StartsWith("raider7",StringComparison.Ordinal)) result.shooters++;
                }
                else if(behavior==NpcCreatureBehaviours.Robobrain || behavior==NpcCreatureBehaviours.SpriteBot || behavior==NpcCreatureBehaviours.Roller ||
                    behavior==NpcCreatureBehaviours.SpiderMine || actor.actorType.StartsWith("turret",StringComparison.Ordinal)) result.robots++;
                else result.monsters++;
            }
            return result;
        }
        private static void Require(bool value,string message) {if(!value) throw new InvalidOperationException("Номерные заводы: "+message);}
    }
}
