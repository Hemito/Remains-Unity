using System;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Presentation;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class DoorBarrierChecks
    {
        private sealed class UnrestrictedAccess : IDoorAccessRule
        {public string Rejection(ActorState actor,DoorState door)=>null;}
        public static void Run()
        {
            int checks=RunDomain();RunNative();
            Debug.Log($"Разрушаемые проёмы: PASS — {checks} доменных условий, legacy JSON/asset, occupied hatch, тело/ящик/выстрел, доступ модов/NPC, SO/placement и однократное представление разрушения.");
        }
        // No Resources or Unity object creation: also executable in a standalone managed harness.
        public static int RunDomain()
        {
            int checks=0;
            void Require(bool valid,string reason) {checks++;if(!valid) throw new InvalidOperationException("Преграды: "+reason);}
            void Invalid(DoorState door,string reason)
            {bool failed=false;try {door.Validate();} catch(InvalidOperationException) {failed=true;}Require(failed,reason);}
            DoorState Barrier(string id="barrier")=>new() {id=id,definition="fixture",level="factory:0",surfaceTexture="door:wood",x=0,y=2,width=1.6f,height=.8f,
                integrity=new DoorIntegrity {maxHealth=60,health=60},interaction=new DoorInteractionVolume {width=1.6f,height=1}};
            var actor=new ActorState {id="actor",player=true,connected=true,level="factory:0",x=.8f,y=1.5f};
            var access=new UnrestrictedAccess();
            var legacy=StateJson.FromJson<DoorState>("{\"id\":\"old\",\"definition\":\"door1\",\"level\":\"factory:0\",\"surfaceTexture\":\"door:wood\",\"x\":0,\"y\":2,\"width\":1.6,\"height\":0.8}");
            legacy.Validate();
            Require(legacy.interactive && legacy.integrity==null && legacy.interaction==null && legacy.CanInteract && legacy.Blocks && !legacy.Destroyed && legacy.InteractionBounds==legacy.Bounds,
                "Прежняя JSON-дверь получила скрытую прочность или потеряла прежние возможности.");
            Require(DoorDamageOperations.Apply(legacy,"legacy-shot",5,out _) == DoorChange.Rejected && legacy.revision==0 && legacy.integrity==null,
                "Урон выдумал прочность неразрушаемой двери.");
            var legacyCopy=StateJson.Copy(legacy);Require(legacyCopy.integrity==null && legacyCopy.interaction==null && legacyCopy.interactive,"Roundtrip изменил legacy policy.");
            string legacyJson=StateJson.ToJson(legacy);
            Require(!legacyJson.Contains("Destroyed") && !legacyJson.Contains("Blocks") && !legacyJson.Contains("CanInteract") && !legacyJson.Contains("InteractionBounds"),"В JSON попали вычисляемые флаги.");
            var door=Barrier();
            foreach(float amount in new[]{0f,-1f,float.NaN,float.PositiveInfinity,float.NegativeInfinity})
            {
                string before=StateJson.ToJson(door);
                Require(DoorDamageOperations.Apply(door,"invalid",amount,out _) == DoorChange.Rejected && StateJson.ToJson(door)==before,"Невалидный amount изменил здоровье/журнал.");
            }
            Require(DoorDamageOperations.Apply(door," ",1,out _) == DoorChange.Rejected && door.revision==0,"Пустой ID атаки принят.");
            Require(DoorDamageOperations.Apply(door,"hit-a",10,out _) == DoorChange.Changed && door.integrity.health==50 && door.revision==1 && door.integrity.resolvedAttacks.SequenceEqual(new[]{"hit-a"}) && door.Blocks,
                "Первое попадание не записано точно один раз.");
            string first=StateJson.ToJson(door);
            Require(DoorDamageOperations.Apply(door,"hit-a",20,out _) == DoorChange.Unchanged && StateJson.ToJson(door)==first,"Повтор атаки снова повредил дверь.");
            var second=Barrier("other");
            Require(DoorDamageOperations.Apply(second,"hit-a",10,out _) == DoorChange.Changed && second.integrity.health==50,"Журнал ошибочно общий для разных дверей.");
            var copied=StateJson.Copy(door);
            Require(DoorDamageOperations.Apply(copied,"hit-a",10,out _) == DoorChange.Unchanged && StateJson.ToJson(copied)==first,"Загрузка потеряла дедупликацию.");
            Require(DoorDamageOperations.Apply(door,"lethal",float.MaxValue,out _) == DoorChange.Changed && door.integrity.health==0 && door.Destroyed && !door.Blocks && !door.CanInteract && !door.open && door.revision==2,
                "Разрушение не освободило закрытый объём или подменило флаг open.");
            string dead=StateJson.ToJson(door);
            Require(DoorDamageOperations.Apply(door,"lethal",1,out _) == DoorChange.Unchanged && StateJson.ToJson(door)==dead,"Дубликат lethal изменил разрушенную дверь.");
            Require(DoorDamageOperations.Apply(door,"post-death",1,out _) == DoorChange.Rejected && StateJson.ToJson(door)==dead,"После разрушения добавлен лишний receipt.");
            Require(DoorOperations.SetOpen(door,actor,door.revision,true,access,false,out _) == DoorChange.Rejected && StateJson.ToJson(door)==dead,"Пользовательский доступ открыл разрушенную дверь.");
            var restored=StateJson.Copy(door);Require(restored.Destroyed && restored.integrity.maxHealth==60 && restored.integrity.health==0 && restored.integrity.resolvedAttacks.Count==2 && !restored.Blocks,
                "Сохранение воскресило проём или потеряло journal/maxHealth.");
            var layout=new DoorLayout {patch="fixture:barriers:v1",level=door.level,doors=new[]{Barrier()}};
            var world=new WorldState();DoorContentInstaller.Apply(world,layout);
            DoorDamageOperations.Apply(world.doors[0],"installed-death",60,out _);
            var saved=StateJson.Copy(world);string savedDoor=StateJson.ToJson(saved.doors[0]);
            Require(!DoorContentInstaller.Apply(saved,layout) && StateJson.ToJson(saved.doors[0])==savedDoor,"Повторная установка воскресила разрушенную дверь.");
            saved.contentPatches.Clear();DoorContentInstaller.Apply(saved,layout);
            Require(StateJson.ToJson(saved.doors[0])==savedDoor,"Принятие имеющейся двери восстановило стартовую прочность.");
            var corrupt=Barrier();corrupt.integrity.version=2;Invalid(corrupt,"Неизвестная версия прочности принята.");
            corrupt=Barrier();corrupt.integrity.health=-1;Invalid(corrupt,"Отрицательное здоровье принято.");
            corrupt=Barrier();corrupt.integrity.health=61;Invalid(corrupt,"Здоровье выше maxHealth принято.");
            corrupt=Barrier();corrupt.integrity.maxHealth=float.NaN;Invalid(corrupt,"NaN maxHealth принят.");
            corrupt=Barrier();corrupt.integrity.resolvedAttacks=null;Invalid(corrupt,"Отсутствующий журнал молча заменён.");
            corrupt=Barrier();corrupt.integrity.resolvedAttacks.AddRange(new[]{"dup","dup"});Invalid(corrupt,"Повторный receipt в JSON принят.");
            corrupt=Barrier();corrupt.interaction.width=0;Invalid(corrupt,"Пустой interaction volume принят.");
            corrupt=Barrier();corrupt.interaction.offsetY=float.PositiveInfinity;Invalid(corrupt,"Неконечный interaction offset принят.");
            corrupt=Barrier();corrupt.interaction.version=2;Invalid(corrupt,"Неизвестная interaction version принята.");
            corrupt=Barrier();corrupt.revision=long.MaxValue;string overflow=StateJson.ToJson(corrupt);bool exhausted=false;
            try {DoorDamageOperations.Apply(corrupt,"overflow",1,out _);} catch(InvalidOperationException) {exhausted=true;}
            Require(exhausted && StateJson.ToJson(corrupt)==overflow,"Revision overflow частично изменил receipt/HP.");
            door=Barrier();door.interactive=false;
            Require(door.Blocks && !door.CanInteract && new NearbyDoorAccess().Rejection(actor,door)!=null,"Неинтерактивная стенка стала проходимой или доступной.");
            Require(DoorOperations.SetOpen(door,actor,0,true,access,false,out _) == DoorChange.Rejected && !door.open && door.revision==0,"Custom access обошёл interactive=false.");
            Require(DoorDamageOperations.Apply(door,"wall-hit",1,out _) == DoorChange.Changed && door.integrity.health==59 && door.Blocks && !door.CanInteract,"Неинтерактивная преграда не принимает отдельный damage channel.");
            ActorTypes.Register(new ActorTypeDefinition {id="barrier-fixture-player",presentation="pony"},true);
            actor.actorType="barrier-fixture-player";world=new WorldState();world.actors.Add(actor);world.doors.Add(door);
            var rules=new GameplayRuleSet(new PrototypeDamageRule(),new ZeroHealthKnockdownRule(),new HubPartyDefeatRule(),null,null,null,
                new MassLimitedTelekinesisRule(new TelekinesisSettings {maximumMass=10,range=2,releaseRange=3}),new StableProximityOwnershipRule(),access);
            Require(InteractionSelection.Select(world,actor,rules).kind==InteractionKind.None && InteractionSelection.Select(world,actor,rules,door.Bounds.center).kind==InteractionKind.None,
                "Pointer/cursor Select приняли неинтерактивную дверь через custom access.");
            door.interactive=true;
            Require(door.InteractionBounds.height==1 && door.Bounds.height==.8f && door.DistanceTo(new Vector2(.8f,2.9f))==0,"Interaction hatch подменил физическую геометрию.");
            var selection=InteractionSelection.Select(world,actor,rules,new Vector2(.8f,2.9f));
            Require(selection.id==door.id && selection.bounds==door.InteractionBounds,"Cursor target рисует физический объём вместо interaction.");
            DoorDamageOperations.Apply(door,"selection-death",60,out _);
            Require(InteractionSelection.Select(world,actor,rules).kind==InteractionKind.None && InteractionSelection.Select(world,actor,rules,door.InteractionBounds.center).kind==InteractionKind.None,
                "Разрушенная дверь осталась целью custom E.");
            // Actual horizontal hatch volume: top support, vertical sweep, prop gravity and shots all use the same Blocks state.
            world=new WorldState();door=Barrier();world.doors.Add(door);
            var geometry=new SimulationGeometry(level=>new RoomLayout {id=level,left=-5,right=5,bottom=-1,top=8,
                platforms=new[]{new RoomSurface {x=-5,y=-1,width=10,height=1,texture="floor"}},ladders=Array.Empty<RoomLadder>(),artwork=Array.Empty<RoomArtwork>()});
            world.actors.Add(new ActorState {id="loader",player=true,connected=true,level=door.level});geometry.Reconcile(world);
            var room=geometry.Get(door.level);var surfaces=room.platforms;var hull=new MovementHull {width=.4f,height=1};var feet=new Vector2(.8f,2.8f);
            Require(ActorTerrainMotion.Supported(room,hull,feet) && ActorTerrainMotion.Move(room,hull,feet,Vector2.down*.5f,false).y==2.8f,"Закрытый люк не поддерживает тело.");
            Require(ShotObstacles.FirstContact(room,new Vector2(.8f,4),new Vector2(.8f,1),out var contacted).HasValue && contacted.sourceDoorId==door.id,
                "Точное попадание не несёт stable ID люка.");
            var cargo=new PropState {id="cargo",level=door.level,x=.8f,y=3,width=.4f,height=.4f,anchorHeight=.2f,movable=true,hasPhysicalBody=true};
            PropMotion.Step(cargo,room,1f/30,null);Require(cargo.y==3 && cargo.vy==0,"Свободный ящик провалился через закрытый люк.");
            DoorDamageOperations.Apply(door,"nonlethal",1,out _);geometry.Reconcile(world);
            Require(ReferenceEquals(surfaces,room.platforms),"HP hit без topology change перестроил поверхности.");
            Require(DoorOperations.SetOpen(door,actor,door.revision,true,access,false,out _) == DoorChange.Changed,"Обычный interactive hatch не открылся.");
            geometry.Reconcile(world);Require(!ActorTerrainMotion.Supported(room,hull,feet),"Открытый люк всё ещё опора.");
            world.props.Add(new PropState {id="in-aperture",level=door.level,x=.8f,y=2.2f,width=.4f,height=.4f,hasPhysicalBody=true});
            string occupied=StateJson.ToJson(door);
            var occupancyWorld=new WorldState();occupancyWorld.props.Add(world.props[0]);
            Require(DoorOccupancy.IsOccupied(occupancyWorld,door) && DoorOperations.SetOpen(door,actor,door.revision,false,access,true,out _) == DoorChange.Rejected && StateJson.ToJson(door)==occupied,
                "Закрытие занятого люка изменило дверь или вытолкнуло ящик.");
            world.props.Clear();Require(DoorOperations.SetOpen(door,actor,door.revision,false,access,false,out _) == DoorChange.Changed,"Пустой люк не закрылся.");
            DoorDamageOperations.Apply(door,"hatch-destroy",60,out _);geometry.Reconcile(world);
            Require(ReferenceEquals(room,geometry.Get(door.level)) && !ActorTerrainMotion.Supported(room,hull,feet) && ActorTerrainMotion.Move(room,hull,feet,Vector2.down*.5f,false).y<2.8f,
                "Разрушение не освободило support/sweep на прежнем RoomLayout.");
            Require(!ShotObstacles.FirstContact(room,new Vector2(.8f,4),new Vector2(.8f,1)).HasValue,"Разрушенный люк остался препятствием выстрелу.");
            PropMotion.Step(cargo,room,1f/30,null);Require(cargo.y<3 && cargo.vy<0,"Разрушенный люк остался невидимой опорой ящика.");
            return checks;
        }
        private static void RunNative()
        {
            void Require(bool valid,string reason) {if(!valid) throw new InvalidOperationException("Представление преград: "+reason);}
            var closed=Resources.Load<Sprite>("Original/Room/OfficeOpenings/door1-close");
            var opened=Resources.Load<Sprite>("Original/Room/OfficeOpenings/door1-open");
            var destroyed=Resources.Load<Sprite>("Original/Room/OfficeOpenings/door1-die");
            var openSound=Resources.Load<AudioClip>("Original/Room/OfficeOpenings/Audio/wood_door_open");
            var closeSound=Resources.Load<AudioClip>("Original/Room/OfficeOpenings/Audio/wood_door_close");
            var destroySound=Resources.Load<AudioClip>("Original/Room/OfficeOpenings/Audio/break_wood");
            Require(closed!=null && opened!=null && destroyed!=null && openSound!=null && closeSound!=null && destroySound!=null,"Нет точных исходных Sprite/AudioClip fixtures.");
            var asset=ScriptableObject.CreateInstance<DoorAsset>();var legacy=ScriptableObject.CreateInstance<DoorAsset>();
            var root=new GameObject("Проверка разрушения проёма");var prefab=new GameObject("Префаб проверки проёма");var roomObject=new GameObject("Комната проверки проёма");
            try
            {
                asset.ConfigureBarrier("barrier-visual-fixture",new Vector2(.8f,1.6f),closed,opened,destroyed,openSound,closeSound,destroySound,true,60,new Vector2(.8f,1.8f));
                var renderer=prefab.AddComponent<SpriteRenderer>();var view=prefab.AddComponent<DoorView>();view.Configure(asset,renderer);
                var room=roomObject.AddComponent<RoomAuthoring>();room.Configure(new RoomLayout {left=-10,right=10});
                prefab.transform.SetParent(roomObject.transform,false);prefab.transform.localPosition=new Vector3(3,4,0);prefab.transform.localScale=new Vector3(2,3,1);
                var authoring=prefab.AddComponent<RoomDoorAuthoring>();authoring.Configure("authored",false,"door:wood");var state=authoring.Read(room,"hub");
                Require(state.interactive && state.integrity.health==60 && state.integrity.maxHealth==60 && state.integrity.resolvedAttacks.Count==0 && state.interaction!=null,
                    "Новое размещение не объявило capability/прочность.");
                Require(Mathf.Abs(state.Bounds.width-1.6f)<.00001f && Mathf.Abs(state.Bounds.height-4.8f)<.00001f && Mathf.Abs(state.InteractionBounds.height-5.4f)<.00001f && state.InteractionBounds.center.x==state.Bounds.center.x,
                    "Read потерял реальные nested transform/interaction bounds.");
                legacy.Configure("legacy-fixture",new Vector2(.8f,1.6f),closed,opened,openSound,closeSound);
                typeof(DoorAsset).GetField("capabilityVersion",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(legacy,0);
                typeof(DoorAsset).GetField("interactive",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(legacy,false);
                typeof(DoorAsset).GetField("interactionSize",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(legacy,Vector2.zero);
                legacy.Validate();Require(legacy.Interactive && legacy.MaxHealth==0 && legacy.InteractionSize==legacy.Size,"Unity legacy version0 потерял prior capability contract.");
                view.Configure(legacy,renderer);var oldPlacement=authoring.Read(room,"hub");
                Require(oldPlacement.interactive && oldPlacement.integrity==null && oldPlacement.interaction==null,"Read мигрировал старую дверь в новый health/interaction контракт.");
                view.Configure(asset,renderer);
                var visuals=root.AddComponent<DoorVisuals>();visuals.SetOrders(new WorldVisualOrderTable());visuals.RegisterPrefab(view);var world=new WorldState();state.id="visual";world.doors.Add(state);
                visuals.Reconcile(world,"hub");var actual=visuals.Find(state.id);var initialPosition=actual.transform.position;var initialScale=actual.transform.localScale;
                Require(actual.Artwork.sprite==closed && visuals.SoundChanges==0,"Начальная закрытая загрузка не молчит.");
                DoorDamageOperations.Apply(state,"hp-hit",10,out _);visuals.Reconcile(world,"hub");
                Require(actual.Artwork.sprite==closed && visuals.SoundChanges==0,"Обычный HP hit включил destroy sound/frame.");
                var before=world.Copy();DoorDamageOperations.Apply(state,"kill",60,out _);visuals.Reconcile(world,"hub");visuals.Reconcile(world.Copy(),"hub");
                Require(actual.Artwork.sprite==destroyed && actual.transform.position==initialPosition && actual.transform.localScale==initialScale && visuals.SoundChanges==1,
                    "Разрушение сместило привязку/масштаб или повторило sound transition.");
                visuals.Reconcile(before,"hub");var staleDefinition=before.Copy();staleDefinition.doors[0].definition="old-unloaded-type";visuals.Reconcile(staleDefinition,"hub");visuals.Reconcile(world,"hub");
                Require(actual.Artwork.sprite==destroyed && visuals.SoundChanges==1,"Запоздалый снимок воскресил рисунок и повторил разрушение.");
                visuals.Reconcile(world,"factory:0");visuals.Reconcile(world,"hub");
                Require(visuals.Find(state.id).Artwork.sprite==destroyed && visuals.SoundChanges==1,"Initial destroyed load повторил sound.");
                asset.ConfigureBarrier(asset.Id,asset.Size,closed,opened,destroyed,null,null,destroySound,false,60,asset.Size);
                asset.Validate();bool missing=false;
                try {asset.ConfigureBarrier(asset.Id,asset.Size,closed,opened,null,null,null,null,false,60);} catch(InvalidOperationException) {missing=true;}
                Require(missing,"Destructible asset без destroyed Sprite/AudioClip принят.");
                asset.ConfigureBarrier(asset.Id,new Vector2(.8f,3),closed,opened,destroyed,null,null,destroySound,false,60);
                Anatomy.Load();ActorTypes.Load();
                var npc=new ActorState {id="npc",actorType="raider",anatomy="raider",owner="fixture-owner",x=-.5f,y=0};
                var door=new DoorState {id="npc-barrier",definition=asset.Id,level=npc.level,x=0,y=0,width=.8f,height=3,surfaceTexture="door:wood",interactive=false,
                    integrity=new DoorIntegrity {health=60,maxHealth=60}};
                var npcRoom=new RoomLayout {platforms=DoorGeometry.Compose(Array.Empty<RoomSurface>(),new[]{door},npc.level)};
                Require(NpcDoorIntent.Select(npc,new Vector2(3,0),new[]{door},npcRoom)==null,"NPC попытался открыть неинтерактивную стенку.");
                door.interactive=true;Require(NpcDoorIntent.Select(npc,new Vector2(3,0),new[]{door},npcRoom)==door,"Положительный контроль обычной NPC двери не выбран.");
                var derived=npcRoom.platforms[0];npcRoom.platforms=new[]{new RoomSurface {x=door.x,y=door.y,width=door.width,height=door.height},derived};
                Require(NpcDoorIntent.Select(npc,new Vector2(3,0),new[]{door},npcRoom)==null,"Совпадающая неподвижная стена ошибочно стала дверью по одним размерам.");
                npcRoom.platforms=new[]{derived};
                DoorDamageOperations.Apply(door,"npc-death",60,out _);
                Require(NpcDoorIntent.Select(npc,new Vector2(3,0),new[]{door},npcRoom)==null,"NPC выбрал разрушенную дверь даже при запоздалой геометрии.");
            }
            finally
            {UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(prefab);UnityEngine.Object.DestroyImmediate(roomObject);UnityEngine.Object.DestroyImmediate(asset);UnityEngine.Object.DestroyImmediate(legacy);}
        }
    }
}
