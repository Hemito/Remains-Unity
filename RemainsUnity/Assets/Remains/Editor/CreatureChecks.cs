using System;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // The creature pipeline (CreatureAuthoring → CreatureCatalog → FactoryCreatureAdapter): every connected creature
    // has a loadable actor type, anatomy, atlas with the poses the game draws, an initial state matching AllData and a
    // death voice; seed 104 installs exactly the template placements of connected creatures, once; far creatures sleep.
    public static class CreatureChecks
    {
        private static readonly string[] Poses={"idle","walk","run","jump","down"};
        private static readonly string[] FamilyPoses={"idle","walk","run","jump","attack","down"};
        public static void RunBatch()
        {
            try {Run();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void Run()
        {
            CheckCorpseFrameBounds();
            Anatomy.Load();WeaponCatalog.Load();Remains.Presentation.ShotArtwork.Prepare();
            var catalog=CreatureCatalog.Load();
            Require(catalog.Creatures.Count>0,"каталог пуст");
            var exported=ExportedContentCatalog.Load();var voices=ActorDeathAudioCatalog.Load();
            // Every NPC type gets valid ground movement from its saved asset: an unauthored motion is serialized as zeros
            // and a robobrain's jump of 0 must not reach the movement controller (each broke the NPC step, 2026-10-31).
            var playerMovement=MovementSettings.Load();
            foreach(var definition in EntityAssetCatalog.Load().CreateActors().Where(d=>d.combatAi))
                try
                {
                    Remains.Gameplay.NpcController.Movement(playerMovement,definition).Validate();
                    if(NpcCreatureBehaviours.UsesRaiderBrain(definition.behavior)) Remains.Gameplay.NpcRaiderController.Movement(playerMovement,definition).Validate();
                }
                catch(InvalidOperationException error) {throw new InvalidOperationException($"{definition.id}: движение NPC — {error.Message}");}
            // A raider-brained body held down on a solid floor (a target below) does not crouch: it has no crouching hull.
            var floor=new RoomLayout {id="crouch-check",left=-10,right=10,bottom=-1,top=10,ladders=Array.Empty<RoomLadder>(),
                platforms=new[]{new RoomSurface {x=-10,y=-1,width=20,height=1}}};
            RoomSurfaces.Prepare(floor);
            foreach(var creature in catalog.Creatures.Where(c=>NpcCreatureBehaviours.UsesRaiderBrain(ActorTypes.Get(c.actorType).behavior)))
            {
                var body=creature.spawn.Create(new TrainingPlacement {id="crouch",type=creature.actorType,facing=1},"l");body.x=0;body.y=0;
                var motion=new Remains.Gameplay.MovementController(floor,Remains.Gameplay.NpcRaiderController.Movement(playerMovement,ActorTypes.Get(creature.actorType)));
                for(int i=0;i<60;i++) motion.Step(body,Vector2.down,false,1f/30);
                Require(!body.crouched,$"{creature.actorType}: удерживая «вниз» на полу, не приседает");
            }
            foreach(var creature in catalog.Creatures)
            {
                var type=ActorTypes.Get(creature.actorType);
                CheckCorpsePresentation(creature,type);
                if(NpcCreatureBehaviours.Fixed(type.behavior)) {Turret(creature,type,exported);continue;}
                if(type.behavior==NpcCreatureBehaviours.Raider) {Raider(creature,type,exported,voices);continue;}
                if(type.behavior==NpcCreatureBehaviours.SpriteBot) {SpriteBot(creature,type,exported,voices);continue;}
                if(type.behavior==NpcCreatureBehaviours.SpiderMine) {SpiderMine(creature,type,exported);continue;}
                if(type.behavior==NpcCreatureBehaviours.Roller) {Roller(creature,type,exported,voices);continue;}
                if(type.behavior==NpcCreatureBehaviours.Robobrain) {Robobrain(creature,type,exported,voices);continue;}
                if(type.behavior==NpcCreatureBehaviours.Zombie) {Zombie(creature,type,exported,voices);continue;}
                Require(type.combatAi && type.naturalAttack!=null && type.presentation=="atlas","тип "+creature.actorType);
                type.naturalAttack.Validate(creature.actorType);type.perception.Validate();
                var art=AtlasAnimationData.Get(type.artwork);
                bool family=NpcCreatureBehaviours.IsFamily(type.behavior);
                foreach(string pose in family?FamilyPoses:Poses) Require(art.Clip(pose).count>0,$"{creature.actorType}: поза {pose}");
                if(family) type.motion.Validate(creature.actorType);
                Require(Anatomy.Movement(new ActorState {anatomy=creature.actorType}).width>0,"анатомия "+creature.actorType);
                var unit=System.Xml.Linq.XElement.Parse(exported.Require("unit:"+creature.sourceId).rawXml);
                float hp=float.Parse((string)unit.Element("comb").Attribute("hp"),System.Globalization.CultureInfo.InvariantCulture);
                var spawned=creature.spawn.Create(new TrainingPlacement {id="check",type=creature.actorType,facing=1},"level");
                Require(Mathf.Approximately(spawned.health,hp) && spawned.anatomy==creature.actorType,$"{creature.actorType}: здоровье {spawned.health} ≠ AllData {hp}");
                Require(voices.TrySelect(creature.actorType,0,out _),"голос смерти "+creature.actorType);
                Animates(creature.actorType,art);
                Require(type.attackSounds is {Length:>0} && type.attackSounds.All(key=>AudioClipSource.Load(key)!=null),"звуки касания "+creature.actorType);
                Require(type.hitMaterial is "flesh" or "metal" or "slime",$"{creature.actorType}: материал попадания {type.hitMaterial}");
            }

            var world=new WorldState {worldId=Guid.NewGuid().ToString("N"),seed=104};
            FactoryPreparation.Create(world,GameAssets.Load<FactoryCatalogAsset>(FactoryCatalogAsset.ResourceKey),0);
            try
            {
                FactoryContentSources.Prepare(world,"factory:0");
                var room=FactoryContentSources.Require(world,"factory:0");
                var root=room.GetComponent<FactoryLevelRoot>();
                // A danger-chosen object (raiders) is placed only when its chosen variant and weapon are connected.
                bool Placed(string objectId,double danger,string localId)=>catalog.TryObject(objectId,out var rule) &&
                    (string.IsNullOrEmpty(rule.variantRule) || CreatureCatalog.ChooseVariant(rule,danger,FactoryRandomEncounters.VariantRandom(root,localId),out _)!=null);
                int explicitCount=root.Objects.Count(o=>o.Source.Type.StartsWith("remains:obj:",StringComparison.Ordinal) && o.Source.Params.Count==0 && o.Source.Items.Count==0 && o.Source.Links.Count==0 && o.Source.Extra==null &&
                    Placed(o.Source.Type.Substring("remains:obj:".Length),o.Room.Cell.Y/2.0,$"{o.Room.Cell.X},{o.Room.Cell.Y}:{o.Source.Id}"));
                // Random room enemies (v5) of the connected creatures, as the plan places them.
                var random=new System.Collections.Generic.HashSet<string>(FactoryRandomEncounters.Plan(root).Where(e=>(!e.Hidden || e.Unit=="zombie") && Placed(e.Unit,e.Danger,e.LocalId))
                    .Select(e=>"factory:0:"+e.LocalId),StringComparer.Ordinal);
                int expected=explicitCount+random.Count;
                var batch=RoomContentBatch.Read(room,"factory:0");
                Require(batch.npcs!=null && batch.npcs.actors.Length==expected && expected>0,$"на заводе {batch.npcs?.actors.Length} существ из {expected}");
                Require(RoomContentInstaller.Apply(world,batch),"первая установка");
                int installed=world.actors.Count(a=>catalog.Creatures.Any(c=>c.actorType==a.actorType));
                Require(installed==expected && !RoomContentInstaller.Apply(world,batch),"повторная установка");
                // A world that installed an earlier placement version gets only what it lacks.
                bool V6(ActorState a)=>ActorTypes.Get(a.actorType).behavior==NpcCreatureBehaviours.Raider;
                bool V5(ActorState a)=>random.Contains(a.id) || V6(a);
                bool V3(ActorState a)=>!V5(a) && (a.actorType=="bloodwing" || a.actorType=="slime" || a.actorType.StartsWith("bloat",StringComparison.Ordinal));
                bool V2(ActorState a)=>!V5(a) && a.actorType.StartsWith("scorp",StringComparison.Ordinal);
                void Upgrade(string patch,Func<ActorState,bool> missing,string what)
                {
                    var older=StateJson.Copy(world);
                    int lacking=older.actors.RemoveAll(a=>missing(a));
                    older.contentPatches.Remove("factory:0:"+FactoryCreatureAdapter.Patch);older.contentPatches.Add("factory:0:"+patch);
                    int kept=older.actors.Count;
                    Require(lacking>0 && RoomContentInstaller.Apply(older,batch) && older.actors.Count==kept+lacking && older.actors.Count(a=>missing(a))==lacking,what);
                }
                bool V4(ActorState a)=>!V5(a) && a.actorType.StartsWith("turret",StringComparison.Ordinal);
                Require(random.Count>0 && random.All(id=>world.actors.Any(a=>a.id==id)),"случайные враги комнат установлены");
                bool V7(ActorState a)=>V6(a) && ActorTypes.Get(a.actorType).raider.attacker is RaiderAttacker.Teeth or RaiderAttacker.Levitated;
                bool V8(ActorState a)=>ActorTypes.Get(a.actorType).behavior==NpcCreatureBehaviours.SpriteBot;
                bool V9(ActorState a)=>ActorTypes.Get(a.actorType).behavior==NpcCreatureBehaviours.SpiderMine;
                bool V10(ActorState a)=>ActorTypes.Get(a.actorType).behavior==NpcCreatureBehaviours.Roller;
                bool V11(ActorState a)=>ActorTypes.Get(a.actorType).behavior==NpcCreatureBehaviours.Robobrain;
                Require(world.actors.Any(V6),"рейдеры завода установлены");
                Require(world.actors.Any(V7),"рукопашные рейдеры завода установлены");
                Require(world.actors.Any(V8) && world.actors.Any(a=>V8(a) && random.Contains(a.id)) && world.actors.Any(a=>V8(a) && !random.Contains(a.id)),
                    "спрайтботы завода установлены — из шаблонов и случайные");
                Require(world.actors.Any(V9) && world.actors.Any(a=>V9(a) && random.Contains(a.id)) && world.actors.Any(a=>V9(a) && !random.Contains(a.id)),
                    "мины-пауки завода установлены — из шаблонов и случайные");
                Require(world.actors.Any(V10),"роллеры завода установлены");
                Require(world.actors.Any(V11) && world.actors.Where(V11).Select(a=>a.actorType).Distinct().Count()==2,"робомозги завода установлены, с обоими стволами");
                // Robots of later versions (spritebots v8, spider mines v9, rollers v10, robobrains v11) are missing from every earlier world too.
                bool V12(ActorState a)=>ActorTypes.Get(a.actorType).behavior==NpcCreatureBehaviours.Zombie;
                var zombies=world.actors.Where(V12).ToArray();
                Require(zombies.Length>0 && zombies.Any(ActorPresence.Hidden) && zombies.Any(a=>!ActorPresence.Hidden(a)) &&
                    zombies.Any(a=>ActorPresence.Hidden(a) && random.Contains(a.id)) && zombies.Select(a=>a.actorType).Distinct().Count()>=3,
                    $"зомби завода установлены: разные варианты, часть закопана ({zombies.Count(ActorPresence.Hidden)} из {zombies.Length})");
                Debug.Log($"Зомби seed104: {zombies.Length} ({string.Join(", ",zombies.GroupBy(a=>a.actorType).OrderBy(g=>g.Key).Select(g=>$"{g.Key} {g.Count()}"))}), закопано {zombies.Count(ActorPresence.Hidden)}.");
                bool Robots(ActorState a)=>V8(a) || V9(a) || V10(a) || V11(a) || V12(a);
                Upgrade("base:factory-generated:creatures-v11",V12,"переход v11 → v12 добавляет только зомби");
                Upgrade("base:factory-generated:creatures-v10",a=>V11(a) || V12(a),"переход v10 → v12 добавляет только робомозгов и зомби");
                Upgrade("base:factory-generated:creatures-v9",a=>V10(a) || V11(a),"переход v9 → v11 добавляет только роллеров и робомозгов");
                Upgrade("base:factory-generated:creatures-v8",a=>V9(a) || V10(a) || V11(a),"переход v8 → v11 добавляет только мины-пауков, роллеров и робомозгов");
                Upgrade("base:factory-generated:creatures-v7",Robots,"переход v7 → v10 добавляет только роботов");
                Upgrade("base:factory-generated:creatures-v6",a=>V7(a) || Robots(a),"переход v6 → v9 добавляет только рукопашных рейдеров и роботов");
                Upgrade("base:factory-generated:creatures-v5",a=>V6(a) || Robots(a),"переход v5 → v9 добавляет только рейдеров и роботов");
                Upgrade("base:factory-generated:creatures-v4",a=>V5(a) || Robots(a),"переход v4 → v9 добавляет только случайных врагов комнат, рейдеров и роботов");
                Upgrade("base:factory-generated:creatures-v3",a=>V4(a) || V5(a) || Robots(a),"переход v3 → v9 добавляет турели, случайных врагов и роботов");
                Upgrade("base:factory-generated:creatures-v2",a=>V3(a) || V4(a) || V5(a) || Robots(a),"переход v2 → v9 добавляет летунов, слизь, турели, случайных врагов и роботов");
                Upgrade("base:factory-generated:creatures-v1",a=>V2(a) || V3(a) || V4(a) || V5(a) || Robots(a),"переход v1 → v9 добавляет скорпионов, летунов, слизь, турели, случайных врагов и роботов");
                var layout=room.CreateLayout(false);
                int clinging=world.actors.Count(a=>V9(a) && SpiderMineCling(StateJson.Copy(a),layout)!=NpcCreatureState.NoCling);
                Debug.Log($"Мины-пауки seed104: {world.actors.Count(V9)}, из них на потолке или стене {clinging}.");
                CheckLevelWithoutLaterVersion(batch.npcs);
                Behaviours(world,room.CreateLayout(false));
                string saved=StateJson.ToJson(world);
                Require(StateJson.ToJson(StateJson.FromJson<WorldState>(saved))==saved,"сохранение мира с существами");
                var counts=string.Join(", ",world.actors.Where(a=>!a.player).GroupBy(a=>a.actorType).Select(g=>$"{g.Key} {g.Count()}"));

                var creature=world.actors.First(a=>catalog.Creatures.Any(c=>c.actorType==a.actorType));
                var player=new ActorState {id="player",player=true,connected=true,level=creature.level,x=creature.x+NpcActivity.HalfWidth+5,y=creature.y};
                world.actors.Add(player);
                Require(!NpcActivity.Awake(world,creature),"дальнее существо не спит");
                player.x=creature.x+5;Require(NpcActivity.Awake(world,creature),"ближнее существо спит");
                Debug.Log($"PASS существа: подключено {catalog.Creatures.Count} ({string.Join(", ",catalog.Creatures.Select(c=>c.actorType))}), пробелов {catalog.Gaps.Count}; завод seed104: {counts}; сон вдали.");
            }
            finally {FactoryContentSources.Clear();}
        }
        private static void CheckCorpseFrameBounds()
        {
            var pixels=new Color32[8*10];
            // Transparent margins and a registration outside the opaque band.
            pixels[3*8+2]=new Color32(255,255,255,1);
            pixels[7*8+5]=new Color32(255,255,255,255);
            var bounds=AtlasSpriteAssetEditor.MeasureOpaqueVerticalBounds(pixels,8,new Rect(1,1,6,8),6,10);
            Require(Mathf.Approximately(bounds.x,-.4f) && Mathf.Approximately(bounds.y,.1f),"непрозрачные границы не учитывают rect/pivot/alpha");
            foreach(int facing in new[]{-1,1})
            foreach(bool flipped in new[]{false,true})
            {
                float offset=Remains.Presentation.AtlasActorVisual.DeadFrameOffset(bounds,flipped);
                float bottom=flipped?-bounds.y:bounds.x;
                Require(Mathf.Abs(offset+bottom)<.00001f,$"тело висит после отражения: facing={facing}, flipY={flipped}");
            }
            bool rejected=false;
            try {AtlasSpriteAssetEditor.MeasureOpaqueVerticalBounds(new Color32[8*10],8,new Rect(1,1,6,8),6,10);}
            catch(InvalidOperationException) {rejected=true;}
            Require(rejected,"пустая графика тела молча получила фиктивную опору");
        }
        internal static void CheckCorpsePresentation(CreatureEntry creature,ActorTypeDefinition type)
        {
            var atlas=GameAssets.Load<AtlasSpriteAsset>(type.artwork);
            Require(atlas!=null && atlas.HasOpaqueVerticalBounds,"не подготовлены границы тела "+creature.actorType);
            var actor=creature.spawn.Create(new TrainingPlacement {id="corpse-support",type=creature.actorType,facing=1},"check");
            actor.Position=new Vector2(2,4);
            var visual=(Remains.Presentation.AtlasActorVisual)Remains.Presentation.ActorVisualFactory.Create(actor);
            try
            {
                visual.Bind(actor);
                // A rolling ball is drawn turned about its body centre: its feet move with the turn.
                var centre=Vector2.up*(Anatomy.Movement(actor).height*.5f);
                var expected=visual.Rolls?actor.Position+centre-(Vector2)(visual.transform.rotation*centre):actor.Position;
                Require(Vector2.Distance(visual.transform.position,expected)<.00001f,"опора трупа сдвинула живое существо "+actor.actorType);
                var view=visual.GetComponent<SpriteRenderer>();
                var frames=atlas.Frames("down");var bounds=atlas.OpaqueVerticalBounds("down");
                var clip=atlas.CreateDefinition().clips.Single(c=>c.pose=="down");
                actor.down=true;actor.health=0;actor.pose="down";
                foreach(int facing in new[]{-1,1})
                for(int frame=0;frame<clip.count;frame++)
                {
                    actor.facing=facing;actor.poseTime=(frame+.01f)/clip.framesPerSecond;visual.Draw();
                    int drawn=Array.IndexOf(frames,view.sprite);
                    Require(drawn>=0,"труп рисует неизвестный кадр "+actor.actorType);
                    float bottom=view.flipY?-bounds[drawn].y:bounds[drawn].x;
                    Require(Mathf.Abs(visual.transform.position.y+bottom-actor.y)<.00001f,
                        $"труп не на опоре: actor={actor.actorType}, frame={frame}, facing={facing}");
                    Require(actor.Position==new Vector2(2,4) && actor.vx==0 && actor.vy==0,"изображение изменило физический источник тела "+actor.actorType);
                }
            }
            finally {UnityEngine.Object.DestroyImmediate(visual.gameObject);}
        }
        // A turret: a fixed gun on the weapon AI with its gun as initial inventory, held at the part's mount (atlas
        // socket) and fired at its weapon's rate; the lamp follows the AI state (watching / attacking / off).
        // A generated raider: a weapon AI with RaiderBrain settings, every pose with the weapon point of each frame, the
        // AllData health, its weapon full in the initial inventory, a death voice.
        private static void Raider(CreatureEntry creature,ActorTypeDefinition type,ExportedContentCatalog exported,ActorDeathAudioCatalog voices)
        {
            string id=creature.actorType;
            bool levitated=type.raider?.attacker==RaiderAttacker.Levitated;
            Require(type.combatAi && type.hasWeapon && type.atlasWeaponSocket!=levitated && type.naturalAttack==null && type.presentation=="atlas" && type.bodyInventory && type.raider!=null,
                $"{id}: рейдер — боевой ИИ с оружием (в зубах — на точке атласа, телекинезом — перед телом) и настройками рейдера");
            type.raider.Validate(id);type.perception.Validate();
            var art=AtlasAnimationData.Get(type.artwork);
            foreach(string pose in new[]{"idle","walk","run","jump","down","climb"})
                Require(art.Clip(pose).count>0 && art.Clip(pose).weaponSockets?.Length==art.Clip(pose).count,$"{id}: поза {pose} с точкой оружия на каждом кадре");
            Require(art.Clip("jump").UsesMotion,$"{id}: кадр прыжка по вертикальной скорости");
            Require(Anatomy.Movement(new ActorState {anatomy=id}).width>0,"анатомия "+id);
            var unit=System.Xml.Linq.XElement.Parse(exported.Require("unit:"+creature.sourceId).rawXml);
            float hp=float.Parse((string)unit.Element("comb").Attribute("hp"),System.Globalization.CultureInfo.InvariantCulture);
            var spawned=creature.spawn.Create(new TrainingPlacement {id="check",type=id,facing=1},"level");
            var items=creature.spawn.ReadInventory().Generate("check-world",spawned.id);
            var weapon=WeaponCatalog.Get(spawned.weapon);
            var gun=items.Single(i=>i.definition==spawned.weapon);
            Require(Mathf.Approximately(spawned.health,hp) && spawned.anatomy==id && gun.weaponState?.rounds==weapon.magazineSize &&
                (string.IsNullOrEmpty(weapon.ammo) || items.Any(i=>i.definition==weapon.ammo && i.count>=weapon.magazineSize)),
                $"{id}: здоровье AllData, оружие {weapon.id} с полным магазином и запасом патронов");
            Require(voices.TrySelect(id,0,out _),"голос смерти "+id);
            // udarUnit: comb damage, plus half of a melee weapon's (of the weapon's type).
            float dam=float.Parse((string)unit.Element("comb").Attribute("damage")??"0",System.Globalization.CultureInfo.InvariantCulture);
            bool melee=WeaponCatalog.IsMelee(weapon.id);var body=type.raider.bodyHit;
            Require(body!=null && (melee?body.damage>dam:Mathf.Approximately(body.damage,dam)) &&
                (type.raider.attacker is RaiderAttacker.Teeth or RaiderAttacker.Levitated)==melee,$"{id}: удар корпусом {body?.damage} при comb damage {dam}, оружие {weapon.id}");
            if(levitated) Swings(id,type,spawned,items,weapon);
            Animates(id,art,creature.sourceId);
        }

        // A levitated weapon's swing as its owner runs it: begun at once, swept over the weapon's rapid, every contact segment
        // of one attack with its anchors, the tip within the hold and the blade; the action ends without a pause.
        private static void Swings(string id,ActorTypeDefinition type,ActorState actor,System.Collections.Generic.List<ItemStack> items,WeaponDefinition weapon)
        {
            var room=new RoomLayout {id="swing-check",left=-20,right=20,bottom=-1,top=20,ladders=Array.Empty<RoomLadder>(),
                platforms=new[]{new RoomSurface {x=-20,y=-1,width=40,height=1}}};
            RoomSurfaces.Prepare(room);
            actor.level="l";actor.x=0;actor.y=0;actor.facing=1;actor.inventory=items;actor.weaponInventoryVersion=1;
            actor.equippedWeaponItem=items.Single(i=>i.definition==weapon.id).id;actor.weapon=weapon.id;
            var target=new ActorState {id="p",player=true,connected=true,level="l",x=1.5f,y=0,health=100,actorType="pony",anatomy="pony"};
            var raiders=new Remains.Gameplay.NpcRaiderController(MovementSettings.Load(),new System.Random(1));
            raiders.Step(actor,type,target,true,room,1f/30);
            Require(NpcAttackRules.TryBegin(actor,target) && actor.npcWeaponAction?.kind==NpcWeaponActions.Swing &&
                Mathf.Approximately(actor.attackRemaining,weapon.cooldown),$"{id}: взмах начинается сразу и длится rapid оружия");
            var fired=new System.Collections.Generic.List<AttackEvent>();float dt=1f/60;var origin=Remains.Gameplay.WeaponPose.Origin(actor);
            float limit=(weapon.meleeAroundHold?weapon.holdingArea.radius:0)+weapon.range*(weapon.meleeMotion.Max(k=>k.reach))+.01f;
            while(actor.attackBusy)
            {
                raiders.Swing(actor,type,room,dt,fired.Add);
                Require(Vector2.Distance(new Vector2(actor.weaponTipX,actor.weaponTipY),origin)<=limit,$"{id}: кончик в пределах удержания и клинка");
                actor.attackRemaining-=dt;
                if(actor.attackRemaining<=0) Require(NpcAttackRules.Complete(actor,target,room)==null,$"{id}: взмах не создаёт выстрела");
            }
            Require(fired.Count>0 && fired.All(a=>a.id==fired[0].id && a.weapon==weapon.id && a.attacker==actor.id && a.meleeAnchor!=null) && actor.attackCooldown==0,
                $"{id}: касания взмаха — одно событие с опорами, следующий взмах без паузы ({fired.Count})");
        }

        private static void Turret(CreatureEntry creature,ActorTypeDefinition type,ExportedContentCatalog exported)
        {
            string id=creature.actorType;
            Require(type.combatAi && type.hasWeapon && type.atlasWeaponSocket && type.naturalAttack==null && type.presentation=="atlas" && !type.bodyInventory &&
                type.hitMaterial=="metal",$"{id}: турель — боевой ИИ с оружием на точке атласа, без добычи с тела");
            type.perception.Validate();
            var art=AtlasAnimationData.Get(type.artwork);
            foreach(string pose in new[]{"idle",Remains.Gameplay.NpcTurret.Alert,"attack",Remains.Gameplay.NpcTurret.Search,"down"})
                Require(art.Clip(pose).count==1 && art.Clip(pose).weaponSockets?.Length==1,$"{id}: поза {pose} с точкой ствола");
            Require(type.turret!=null && type.deathBlastScale>0,$"{id}: настройки наблюдения и взрыв при гибели");
            Require(art.Clip("idle").column!=art.Clip("attack").column && art.Clip("attack").column!=art.Clip("down").column,$"{id}: лампа различает наблюдение, атаку и выключение");
            var unit=System.Xml.Linq.XElement.Parse(exported.Require("unit:"+creature.sourceId).rawXml);
            float hp=float.Parse((string)unit.Element("comb").Attribute("hp"),System.Globalization.CultureInfo.InvariantCulture);
            var spawned=creature.spawn.Create(new TrainingPlacement {id="check",type=id,facing=1},"level");
            var gun=creature.spawn.ReadInventory().Generate("check-world",spawned.id).Single();
            Require(Mathf.Approximately(spawned.health,hp) && spawned.anatomy==id && gun.definition==spawned.weapon && gun.weaponState!=null && gun.count==1,
                $"{id}: здоровье AllData и ствол в начальном инвентаре");
            var weapon=WeaponCatalog.Get(spawned.weapon);
            Require(weapon.projectileSpeed>0 && string.IsNullOrEmpty(weapon.ammo) && WeaponRigs.Get(weapon.id).HasMuzzle && Remains.Presentation.ShotArtwork.TryGet(weapon.id,out _),
                $"{id}: ствол {weapon.id} без патронов, с дулом и графикой снаряда");
            // The mount is the part's placement: the gun aims at a target from there and fires at its rate.
            var actor=new ActorState {id="t",actorType=id,anatomy=id,level="l",x=0,y=0,facing=1,health=hp,weapon=weapon.id};
            gun.id="gun";actor.inventory.Add(gun);actor.weaponInventoryVersion=1;actor.equippedWeaponItem="gun";
            var mount=AtlasWeaponSockets.Anchor(actor);
            Require(mount.y>0 && Mathf.Abs(mount.x)<.05f,$"{id}: точка ствола {mount} над основанием");
            Remains.Gameplay.WeaponPose.Aim(actor,mount+new Vector2(-6,-2));
            var direction=Remains.Gameplay.WeaponPose.Direction(actor);
            Require(direction.x<0 && direction.y<0 && Vector2.Distance(Remains.Gameplay.WeaponPose.Mount(actor),mount)<.001f,$"{id}: ствол не повернулся к цели слева снизу");
            var target=new ActorState {id="p",player=true,connected=true,level="l",x=-6,y=0,health=100};
            Require(NpcAttackRules.TryBegin(actor,target,new GuardWeaponTiming()) && Mathf.Approximately(actor.attackRemaining,1f/30),$"{id}: турель стреляет без долгого прицеливания");
            Require(Mathf.Approximately(new GuardWeaponTiming().Recovery(actor,weapon),Mathf.Max(1f/30,weapon.cooldown)),$"{id}: темп турели — темп её ствола");

            // The shot leaves where the gun points: completing it does not snap the gun to the target.
            NpcAttackRules.Cancel(actor);actor.attackCooldown=0;Require(NpcAttackRules.TryBegin(actor,target,new GuardWeaponTiming()),$"{id}: второй выстрел");
            actor.weaponAngle=60;actor.weaponFlipY=false;actor.attackRemaining=0;
            var shot=NpcAttackRules.Complete(actor,target,null,new GuardWeaponTiming());
            Require(shot!=null && Mathf.Approximately(actor.weaponAngle,60) && Vector2.Angle(shot.End-shot.Origin,new Vector2(Mathf.Cos(60*Mathf.Deg2Rad),Mathf.Sin(60*Mathf.Deg2Rad)))<.5f,
                $"{id}: выстрел турели мгновенно довернул ствол");
            // Magazine with an endless supply: it empties, reloads to full and spends no items.
            Require(weapon.unlimitedReserve && weapon.magazineSize>0 && creature.spawn.ReadInventory().Generate("check-world",spawned.id).Single().weaponState.rounds==weapon.magazineSize,
                $"{id}: полный магазин с бесконечным запасом при появлении");
            NpcAttackRules.Cancel(actor);actor.attackCooldown=0;int items=actor.inventory.Count;
            int left=gun.weaponState.rounds;
            for(int i=0;i<left;i++) Require(WeaponAmmunition.SpendShot(actor,weapon,out _),$"{id}: выстрел {i} из магазина");
            Require(!WeaponAmmunition.SpendShot(actor,weapon,out _) && NpcWeaponActions.Prepare(actor)?.kind==NpcWeaponActions.Reload,$"{id}: пустой магазин ведёт к перезарядке");
            Require(WeaponAmmunition.CompleteReload(actor,weapon) && gun.weaponState.rounds==weapon.magazineSize && actor.inventory.Count==items,$"{id}: перезарядка без патронов до полного");

            // The watch states on a target that appears, stays and leaves (no geometry: the line of sight is given).
            var turret=new ActorState {id="w",actorType=id,anatomy=id,level="l",x=0,y=0,facing=1,health=hp,weapon=weapon.id};
            var random=new System.Random(1);float dt=1f/30;
            Require(!Remains.Gameplay.NpcTurret.Step(turret,type,null,false,null,null,dt,random) && turret.creature.mode==Remains.Gameplay.NpcTurret.Watch && turret.pose=="idle",$"{id}: начинает с наблюдения");
            var settings=type.turret;float start=turret.weaponAngle;
            Require(settings.patrolAngles.Any(a=>Mathf.Abs(Mathf.DeltaAngle(a,start))<.01f || Mathf.Abs(Mathf.DeltaAngle(180-a,start))<.01f),$"{id}: ствол на угле наблюдения");
            for(float t=0;t<settings.periodSeconds+.1f;t+=dt) Remains.Gameplay.NpcTurret.Step(turret,type,null,false,null,null,dt,random);
            float moved=Mathf.Abs(Mathf.DeltaAngle(turret.weaponAngle,start));
            Require(settings.patrolAngles.Length==1 || moved>0 && moved<=settings.watchTurnDegreesPerSecond*(.1f+2*dt)+.01f,$"{id}: смена угла наблюдения плавная ({moved:F1}°)");
            var intruder=new ActorState {id="p",player=true,connected=true,level="l",x=4,y=-1,health=100};
            Require(!Remains.Gameplay.NpcTurret.Step(turret,type,intruder,true,intruder.Position,null,dt,random) && turret.creature.mode==Remains.Gameplay.NpcTurret.Alert && turret.pose==Remains.Gameplay.NpcTurret.Alert,$"{id}: замечает и не стреляет сразу");
            bool fired=false;float before=turret.weaponAngle;
            for(float t=0;t<settings.alertMaxSeconds+.1f;t+=dt) fired|=Remains.Gameplay.NpcTurret.Step(turret,type,intruder,true,intruder.Position,null,dt,random);
            Require(fired && turret.creature.mode==Remains.Gameplay.NpcTurret.Attack && turret.pose=="attack",$"{id}: после тревоги атакует");
            Require(Mathf.Abs(Mathf.DeltaAngle(before,turret.weaponAngle))<=settings.attackTurnDegreesPerSecond*(settings.alertMaxSeconds+.2f)+.01f,$"{id}: ствол поворачивается не быстрее своей скорости");
            Require(!Remains.Gameplay.NpcTurret.Step(turret,type,intruder,false,intruder.Position,null,dt,random) && turret.creature.mode==Remains.Gameplay.NpcTurret.Search,$"{id}: потеряв цель, ищет");
            for(float t=0;t<settings.searchSeconds+.1f;t+=dt) Remains.Gameplay.NpcTurret.Step(turret,type,intruder,false,null,null,dt,random);
            Require(turret.creature.mode==Remains.Gameplay.NpcTurret.Watch,$"{id}: после поиска снова наблюдает");
            if(settings.fixedFacing) Require(turret.facing==1,$"{id}: бронированная не разворачивается");
            else Require(turret.facing==(Mathf.Cos(turret.weaponAngle*Mathf.Deg2Rad)<0?-1:1),$"{id}: смотрит туда, куда направлен ствол");
            turret.hold=new ActorHoldState {holder=intruder.id,token="turret-held-check"};
            var heldPosition=turret.Position;float heldAngle=turret.weaponAngle;
            Require(Remains.Gameplay.NpcTurret.Step(turret,type,intruder,true,intruder.Position,null,dt,random) &&
                turret.creature.mode==Remains.Gameplay.NpcTurret.Attack && turret.Position==heldPosition,
                $"{id}: удерживаемая турель не просыпается для атаки или её ИИ двигает удерживаемое тело");
            Require(Mathf.Abs(Mathf.DeltaAngle(heldAngle,turret.weaponAngle))<=settings.attackTurnDegreesPerSecond*dt+.01f,
                $"{id}: удержание отменило ограничение скорости поворота ствола");
            turret.hold=null;
            turret.creature.mode=Remains.Gameplay.NpcTurret.Alert;turret.creature.timer=settings.alertMinSeconds;
            turret.hold=new ActorHoldState {holder=intruder.id,token="turret-held-alert-check"};
            Require(!Remains.Gameplay.NpcTurret.Step(turret,type,intruder,true,intruder.Position,null,dt,random) &&
                turret.creature.mode==Remains.Gameplay.NpcTurret.Alert && turret.creature.timer>0,
                $"{id}: захват отменяет уже начатую задержку тревоги (исходный aiState=2)");
            turret.hold=null;
        }

        // A seed whose factory has no spritebot keeps the level at v7: no update step is empty; only the first creatures: no update.
        private static void CheckLevelWithoutLaterVersion(NpcLayout installed)
        {
            Require(FactoryCreatureAdapter.Generated(installed) && installed.patch==installed.level+":"+FactoryCreatureAdapter.Patch,"seed104 — на последней версии расстановки");
            var (patch,expansion)=FactoryCreatureAdapter.Versions(new System.Collections.Generic.Dictionary<string,System.Collections.Generic.List<string>>
                {{"rat",new() {"a"}},{"raider1",new() {"b"}}});
            Require(patch=="base:factory-generated:creatures-v7" && expansion.previousPatch=="base:factory-generated:creatures-v1" &&
                expansion.additions.SequenceEqual(new[]{"b"}) && expansion.earlierVersions.Length==0 &&
                FactoryCreatureAdapter.Generated(new NpcLayout {level="factory:0",patch="factory:0:"+patch}),
                $"уровень без спрайтботов: версия {patch}, шаги без пустых добавлений");
            var first=FactoryCreatureAdapter.Versions(new System.Collections.Generic.Dictionary<string,System.Collections.Generic.List<string>> {{"rat",new() {"a"}}});
            Require(first.patch=="base:factory-generated:creatures-v1" && first.expansion==null,"уровень только с первыми существами — без обновления");
        }

        // A spritebot: a flying gun (robolaser, no magazine) at the unit's registration, calm and hostile pictures, the
        // AllData health, vision 0.3 and no hearing, source loot dropped on death (no body to search).
        private static void SpriteBot(CreatureEntry creature,ActorTypeDefinition type,ExportedContentCatalog exported,ActorDeathAudioCatalog voices)
        {
            string id=creature.actorType;
            Require(type.combatAi && type.hasWeapon && type.atlasWeaponSocket && type.naturalAttack==null && type.presentation=="atlas" && !type.bodyInventory &&
                type.hitMaterial=="metal" && type.deathBlastScale>0 && type.motion!=null && NpcCreatureBehaviours.Flies(type.behavior),
                $"{id}: летающий робот с оружием на точке атласа, без добычи с тела");
            type.motion.Validate(id);type.perception.Validate();
            var unit=System.Xml.Linq.XElement.Parse(exported.Require("unit:"+creature.sourceId).rawXml);
            float Num(string element,string name)=>float.Parse((string)unit.Element(element).Attribute(name),System.Globalization.CultureInfo.InvariantCulture);
            Require(Mathf.Approximately(type.perception.sightDistance,Num("comb","vision")*20) && type.perception.hearingDistance==0 &&
                Mathf.Approximately(type.motion.walkSpeed,Num("move","speed")*.6f) && Mathf.Approximately(type.motion.runSpeed,type.motion.walkSpeed*1.5f),
                $"{id}: зрение {type.perception.sightDistance} м, слух {type.perception.hearingDistance}, скорость {type.motion.walkSpeed}/{type.motion.runSpeed} не по AllData");
            var art=AtlasAnimationData.Get(type.artwork);
            foreach(string pose in new[]{"idle",Remains.Gameplay.NpcCreatureController.Hostile,"attack","down"})
                Require(art.Clip(pose).count==1 && art.Clip(pose).weaponSockets?.Length==1,$"{id}: поза {pose} с точкой оружия");
            Require(art.Clip("idle").row!=art.Clip(Remains.Gameplay.NpcCreatureController.Hostile).row,$"{id}: спокойный и враждебный вид различаются");
            Require(Anatomy.Movement(new ActorState {anatomy=id}).width>0,"анатомия "+id);
            var spawned=creature.spawn.Create(new TrainingPlacement {id="check",type=id,facing=1},"level");
            var gun=creature.spawn.ReadInventory().Generate("check-world",spawned.id).Single();
            var weapon=WeaponCatalog.Get(spawned.weapon);
            Require(Mathf.Approximately(spawned.health,Num("comb","hp")) && spawned.anatomy==id && gun.definition==spawned.weapon && gun.weaponState!=null && gun.count==1,
                $"{id}: здоровье AllData и лазер в начальном инвентаре");
            Require(weapon.id=="weapon:robolaser:grade:0" && Mathf.Approximately(weapon.damage,7) && Mathf.Approximately(weapon.cooldown,1) && !weapon.UsesMagazine &&
                weapon.projectileSpeed>0 && WeaponRigs.Get(weapon.id).HasMuzzle && Remains.Presentation.ShotArtwork.TryGet(weapon.id,out _),
                $"{id}: лазер robolaser по AllData — урон 7, раз в секунду, без магазина, с дулом и лучом");
            Require(voices.TrySelect(id,0,out _),"голос смерти "+id);
            Require(NpcCreatureBehaviours.DropsLootOnDeath(type.behavior) && creature.deathLoot!=null && creature.deathLootHeight>0 &&
                creature.deathLoot.Read().entries.Single() is {definition:"item:scrap",probability:var chance} && Mathf.Approximately(chance,.2f),
                $"{id}: при гибели выпадает металлолом с шансом 0,2 (LootGen spritebot)");

            // The gun turns about the registration (setWeaponPos: X, Y − 1) and fires at its own rate.
            var actor=new ActorState {id="s",actorType=id,anatomy=id,level="l",x=0,y=0,facing=1,health=spawned.health,weapon=weapon.id};
            gun.id="gun";actor.inventory.Add(gun);actor.weaponInventoryVersion=1;actor.equippedWeaponItem="gun";
            var mount=AtlasWeaponSockets.Anchor(actor);
            Require(Mathf.Abs(mount.x)<.05f && mount.y>=0 && mount.y<.1f,$"{id}: точка лазера {mount} у опоры юнита");
            var target=new ActorState {id="p",player=true,connected=true,level="l",x=-6,y=2,health=100};
            Require(NpcAttackRules.TryBegin(actor,target,new GuardWeaponTiming()) && Mathf.Approximately(actor.attackRemaining,1f/30) &&
                Mathf.Approximately(new GuardWeaponTiming().Recovery(actor,weapon),weapon.cooldown),$"{id}: стреляет без долгого прицеливания, в темпе лазера");
            actor.attackRemaining=0;
            Require(NpcAttackRules.Complete(actor,target,null,new GuardWeaponTiming()) is {} shot && shot.weapon==weapon.id && actor.inventory.Count==1,
                $"{id}: выстрел без расхода патронов");

            // UnitSpriteBot.control in an open room: calm wandering, hostile for good once it notices a target.
            var room=new RoomLayout {id="spritebot-check",left=-30,right=30,bottom=-1,top=30,ladders=Array.Empty<RoomLadder>(),
                platforms=new[]{new RoomSurface {x=-30,y=-1,width=60,height=1}}};
            RoomSurfaces.Prepare(room);
            var bot=new ActorState {id="b",actorType=id,anatomy=id,level="l",x=0,y=10,facing=1,health=spawned.health};
            var controller=new Remains.Gameplay.NpcCreatureController();float dt=1f/30;bool fired=false;float fastest=0;
            for(int i=0;i<90;i++) {fired|=controller.Step(bot,type,null,null,room,null,dt);fastest=Mathf.Max(fastest,new Vector2(bot.vx,bot.vy).magnitude);}
            Require(!fired && bot.creature.mode==Remains.Gameplay.NpcCreatureController.Calm && bot.pose=="idle" && fastest>0 && fastest<=type.motion.walkSpeed*1.5f+.01f,
                $"{id}: без цели спокоен и блуждает не быстрее шага ×1,5 ({fastest:F2} м/с)");
            int wants=0;
            for(int i=0;i<300;i++) if(controller.Step(bot,type,null,new Vector2(bot.x+3,bot.y),room,null,dt)) wants++;
            Require(bot.creature.mode==Remains.Gameplay.NpcCreatureController.Hostile && bot.pose==Remains.Gameplay.NpcCreatureController.Hostile && wants>5 && wants<100,
                $"{id}: заметив цель, враждебен и стреляет в части кадров (около 0,1): {wants} из 300");
            for(int i=0;i<300;i++) Require(!controller.Step(bot,type,null,null,room,null,dt),$"{id}: без цели не стреляет");
            Require(bot.creature.mode==Remains.Gameplay.NpcCreatureController.Hostile && float.IsFinite(bot.x) && float.IsFinite(bot.y),$"{id}: потеряв цель, остаётся враждебным");
        }

        // Where a placed spider mine clings (its first AI step), on a copy.
        private static int SpiderMineCling(ActorState mine,RoomLayout room)
        {
            new Remains.Gameplay.NpcCreatureController().Step(mine,ActorTypes.Get(mine.actorType),null,null,room,new Remains.Gameplay.MovementController(room,MovementSettings.Load()),1f/30);
            return mine.creature.cling;
        }

        // A spider mine: a walking mine without weapon or loot, the AllData speed and jump, its death a blast of comb damage
        // within 150 px; it sleeps fixed on a ceiling, wakes, runs, blows up next to its target and searches when it loses it.
        private static void SpiderMine(CreatureEntry creature,ActorTypeDefinition type,ExportedContentCatalog exported)
        {
            string id=creature.actorType;
            Require(type.combatAi && !type.hasWeapon && type.naturalAttack==null && type.presentation=="atlas" && !type.bodyInventory && type.hitMaterial=="metal" &&
                type.motion!=null && !NpcCreatureBehaviours.Flies(type.behavior) && !NpcCreatureBehaviours.DropsLootOnDeath(type.behavior) && creature.deathLoot==null,
                $"{id}: мина-паук — боевой ИИ без оружия и добычи");
            type.motion.Validate(id);type.perception.Validate();
            var unit=System.Xml.Linq.XElement.Parse(exported.Require("unit:"+creature.sourceId).rawXml);
            float Num(string element,string name)=>float.Parse((string)unit.Element(element).Attribute(name),System.Globalization.CultureInfo.InvariantCulture);
            Require(Mathf.Approximately(type.motion.walkSpeed,Num("move","speed")*.6f) && Mathf.Approximately(type.motion.jumpSpeed,Num("move","jump")*.6f),
                $"{id}: скорость {type.motion.walkSpeed} и прыжок {type.motion.jumpSpeed} не по AllData");
            var blast=WeaponCatalog.Get(type.deathExplosion);
            Require(blast.explosive && Mathf.Approximately(blast.damage,Num("comb","damage")) && Mathf.Approximately(blast.range,3) &&
                blast.damageType==DamageTypes.FromOriginal((int)Num("comb","tipdam")),$"{id}: взрыв {blast.id} — урон {blast.damage}, радиус {blast.range} м не по оригиналу");
            var art=AtlasAnimationData.Get(type.artwork);
            foreach(string pose in new[]{"idle",Remains.Gameplay.NpcCreatureController.Woken,"walk","run","jump","down"}) Require(art.Clip(pose).count>0,$"{id}: поза {pose}");
            Require(art.Clip("walk").count==8 && art.Clip("idle").row!=art.Clip(Remains.Gameplay.NpcCreatureController.Woken).row,$"{id}: шаг из 8 кадров, сон и пробуждение различаются");
            var spawned=creature.spawn.Create(new TrainingPlacement {id="check",type=id,facing=1},"level");
            Require(Mathf.Approximately(spawned.health,Num("comb","hp")) && creature.spawn.ReadInventory()==null,$"{id}: здоровье AllData, без инвентаря");

            // Hanging under a ceiling with nothing below: fixed there asleep; it lets go once it runs.
            var room=new RoomLayout {id="spider-mine-check",left=-30,right=30,bottom=-1,top=30,ladders=Array.Empty<RoomLadder>(),
                platforms=new[]{new RoomSurface {x=-30,y=-1,width=60,height=1},new RoomSurface {x=-5,y=5,width=10,height=1}}};
            RoomSurfaces.Prepare(room);
            var ground=new Remains.Gameplay.MovementController(room,MovementSettings.Load());
            var controller=new Remains.Gameplay.NpcCreatureController();float dt=1f/30;
            var hull=Anatomy.Movement(new ActorState {anatomy=id});
            var mine=new ActorState {id="m",actorType=id,anatomy=id,level="l",x=0,y=5-hull.height-.2f,facing=1,health=spawned.health};
            for(int i=0;i<30;i++) controller.Step(mine,type,null,null,room,ground,dt);
            Require(mine.creature.cling==NpcCreatureState.Ceiling && mine.creature.mode==Remains.Gameplay.NpcCreatureController.Asleep && mine.pose=="idle" &&
                Mathf.Abs(mine.y+hull.height-5)<.02f,$"{id}: висит под потолком, спит ({mine.creature.cling}, {mine.creature.mode}, y={mine.y})");
            var far=new Vector2(200,-1);   // beyond any path it takes here: it never turns back
            controller.Step(mine,type,null,far,room,ground,dt);
            Require(mine.creature.mode==Remains.Gameplay.NpcCreatureController.Woken && mine.pose==Remains.Gameplay.NpcCreatureController.Woken,$"{id}: цель будит её");
            for(int i=0;i<45;i++) controller.Step(mine,type,null,far,room,ground,dt);
            Require(mine.creature.mode==Remains.Gameplay.NpcCreatureController.Running && mine.creature.cling==NpcCreatureState.NoCling,$"{id}: через 30–40 кадров бежит, отпустив потолок");
            for(int i=0;i<60;i++) controller.Step(mine,type,null,far,room,ground,dt);
            Require(mine.x>1 && mine.y<1 && mine.facing==1,$"{id}: упала и бежит к цели ({mine.Position})");
            var target=new ActorState {id="p",player=true,connected=true,level="l",x=mine.x+6,y=mine.y,health=100,actorType="pony",anatomy="pony"};
            Require(!Remains.Gameplay.NpcCreatureController.Detonates(mine,target),$"{id}: вдали от цели не взрывается");
            target.x=mine.x+1;
            Require(Remains.Gameplay.NpcCreatureController.Detonates(mine,target),$"{id}: в 100 px от центра цели взрывается");
            // Losing the target: it searches standing, then sleeps.
            controller.Step(mine,type,null,null,room,ground,dt);
            Require(mine.creature.mode==Remains.Gameplay.NpcCreatureController.Searching && mine.pose==Remains.Gameplay.NpcCreatureController.Woken,$"{id}: потеряв цель, ищет");
            for(int i=0;i<310;i++) controller.Step(mine,type,null,null,room,ground,dt);
            Require(mine.creature.mode==Remains.Gameplay.NpcCreatureController.Asleep,$"{id}: после поиска засыпает");

            // Self-destruction is its ordinary death, and the accepted death is the blast at its feet.
            var before=mine.Copy();var after=mine.Copy();
            var receiver=new DamageReceiver(new FixedDamage());DamageResult result=default;receiver.Applied+=r=>result=r;
            Require(receiver.SelfDestruct(after,"self-destruct:m:0") && after.down && after.health==0 && result.confirmedNpcDeath && !receiver.SelfDestruct(after,"self-destruct:m:1"),
                $"{id}: самоподрыв — гибель, один раз");
            var attack=Remains.Networking.SessionDeathBlasts.Blast(before,after);
            Require(attack!=null && attack.weapon==type.deathExplosion && attack.attacker=="m" && Mathf.Approximately(attack.originY,after.y+.06f) &&
                Remains.Networking.SessionDeathBlasts.Blast(before,before)==null && Remains.Networking.SessionDeathBlasts.Blast(after,after)==null,$"{id}: принятая гибель — взрыв у ног, только при переходе");
        }
        private sealed class FixedDamage : IDamageRule {public float Calculate(string weapon,float speed)=>10;}

        // A zombie (stage 1): ZombieBrain over the raiders' mind, a body touch of comb damage, its run and jump, the raster
        // poses with the jump by vertical speed and the dig, its LootGen loot on the searchable body; buried it is hidden
        // and fixed, a target makes it dig out and pursue; a glowing ghoul radiates, flares when angry and its touch irradiates.
        private static void Zombie(CreatureEntry creature,ActorTypeDefinition type,ExportedContentCatalog exported,ActorDeathAudioCatalog voices)
        {
            string id=creature.actorType;var s=type.raider;
            Require(type.combatAi && !type.hasWeapon && type.naturalAttack==null && type.presentation=="atlas" && type.bodyInventory && s!=null && s.zombie &&
                s.attacker==RaiderAttacker.Teeth && s.bodyHit!=null && !s.climbsLadders && NpcCreatureBehaviours.UsesRaiderBrain(type.behavior),$"{id}: зомби — ИИ зомби с ударом корпусом");
            s.Validate(id);type.perception.Validate();
            var unit=System.Xml.Linq.XElement.Parse(exported.Require("unit:"+creature.sourceId).rawXml);
            float Num(string element,string name,float fallback)=>unit.Element(element)?.Attribute(name) is {} a?float.Parse(a.Value,System.Globalization.CultureInfo.InvariantCulture):fallback;
            Require(Mathf.Approximately(s.walkSpeed,Num("move","speed",0)*.6f) && Mathf.Approximately(s.zombieRun,Num("move","run",0)*.6f) &&
                Mathf.Approximately(s.bodyHit.damage,Num("comb","damage",0)),$"{id}: шаг {s.walkSpeed}, бег {s.zombieRun}, удар {s.bodyHit.damage} не по AllData");
            var art=AtlasAnimationData.Get(type.artwork);
            foreach(string pose in new[]{"idle","walk","trot","run","jump","down",ZombieBrain.EmergingMode}) Require(art.Clip(pose).count>0,$"{id}: поза {pose}");
            Require(art.Clip("jump").UsesMotion && art.Clip("walk").loop && !art.Clip("down").loop,$"{id}: прыжок по вертикальной скорости, шаг по кругу");
            var spawned=creature.spawn.Create(new TrainingPlacement {id="check",type=id,facing=1},"level");
            Require(Mathf.Approximately(spawned.health,Num("comb","hp",0)) && creature.spawn.ReadInventory()?.entries.Any(e=>e.definition=="item:ghoulblood")==true,
                $"{id}: здоровье AllData и добыча LootGen");
            Require(voices.TrySelect(id,0,out _),"голос смерти "+id);
            bool glowing=Num("un","ss",0)==5;
            Require(glowing==(type.radiationAura!=null && type.radiationAura.strength>0) && Mathf.Approximately(type.radiationHit,Num("comb","raddamage",0)) &&
                (!glowing || s.zombieSuper==5 && Mathf.Approximately(type.radiationAura.Strength(false),1) && Mathf.Approximately(type.radiationAura.Strength(true),10.5f)),
                $"{id}: свечение и радиация удара по AllData");

            // Buried: hidden and fixed; a seen target makes it dig out for 24 frames, then pursue and touch it.
            var room=new RoomLayout {id="zombie-check",left=-30,right=30,bottom=-1,top=30,ladders=Array.Empty<RoomLadder>(),
                platforms=new[]{new RoomSurface {x=-30,y=-1,width=60,height=1}}};
            RoomSurfaces.Prepare(room);
            var zombie=StateJson.Copy(spawned);zombie.id="z";zombie.level="l";zombie.x=0;zombie.y=0;zombie.facing=-1;zombie.creature=new NpcCreatureState {mode=ZombieBrain.BuriedMode};
            var target=new ActorState {id="p",player=true,connected=true,level="l",x=-20,y=0,health=100,actorType="pony",anatomy="pony"};
            var raiders=new Remains.Gameplay.NpcRaiderController(MovementSettings.Load(),new System.Random(7));float dt=1f/30;
            Require(ActorPresence.Hidden(zombie) && !AttackScope.Includes(new AttackEvent {level="l"},zombie),$"{id}: закопанный скрыт от попаданий");
            for(int i=0;i<20;i++) raiders.Step(zombie,type,target,false,room,dt);
            Require(ActorPresence.Hidden(zombie) && zombie.x==0,$"{id}: без цели остаётся закопанным на месте");
            bool changed=false;
            for(int i=0;i<10;i++) changed|=raiders.Step(zombie,type,target,true,room,dt).changed;
            Require(zombie.creature.mode==ZombieBrain.EmergingMode && zombie.pose==ZombieBrain.EmergingMode && changed && !ActorPresence.Hidden(zombie),$"{id}: увидев цель, выкапывается");
            for(int i=0;i<60;i++) raiders.Step(zombie,type,target,true,room,dt);
            Require(zombie.creature.mode==null && zombie.x<0,$"{id}: выкопавшись, преследует ({zombie.x})");
            target.x=zombie.x-1;bool touch=false;
            for(int i=0;i<15;i++) touch|=raiders.Step(zombie,type,target,true,room,dt).bodyHit>0;
            Require(touch,$"{id}: рядом с целью бьёт корпусом");
            if(glowing)
            {
                // Its anger drains while the target is in reach (attKorp or 20% a frame): kept 10 m ahead, it flares.
                bool flared=false;
                for(int i=0;i<400 && !flared;i++) {target.x=zombie.x-10;raiders.Step(zombie,type,target,true,room,dt);flared=zombie.creature?.mode==ZombieBrain.FlareMode;}
                Require(flared,$"{id}: разозлившись, вспыхивает");
            }
        }

        // A robobrain: the raiders' state machine in its robot variant with a gun of its list (no magazine) at setWeaponPos,
        // its LootGen loot with the gun on its searchable body; it notices, pursues or holds, never jumps, touches or steps
        // back, runs only while searching and fires in bursts of aiAttackOch.
        private static void Robobrain(CreatureEntry creature,ActorTypeDefinition type,ExportedContentCatalog exported,ActorDeathAudioCatalog voices)
        {
            string id=creature.actorType;var s=type.raider;
            Require(type.combatAi && type.hasWeapon && type.atlasWeaponSocket && type.naturalAttack==null && type.presentation=="atlas" && type.bodyInventory &&
                type.hitMaterial=="metal" && s!=null && s.robot && s.attacker==RaiderAttacker.Ranged && s.jumpSpeed==0 && !s.climbsLadders && s.burstFrames==100 &&
                Mathf.Approximately(s.holdDistance,20) && NpcCreatureBehaviours.UsesRaiderBrain(type.behavior) && !NpcCreatureBehaviours.IsFamily(type.behavior),
                $"{id}: робомозг — ИИ робота (стрелок, без прыжка и лестниц, очередь 100 кадров, стойка в 20 м)");
            s.Validate(id);type.perception.Validate();
            var unit=System.Xml.Linq.XElement.Parse(exported.Require("unit:"+creature.sourceId).rawXml);
            float Num(string element,string name)=>float.Parse((string)unit.Element(element).Attribute(name),System.Globalization.CultureInfo.InvariantCulture);
            Require(Mathf.Approximately(s.walkSpeed,Num("move","speed")*.6f) && Mathf.Approximately(s.RunSpeed,s.walkSpeed*2),$"{id}: шаг {s.walkSpeed} не по AllData");
            var art=AtlasAnimationData.Get(type.artwork);
            foreach(string pose in new[]{"idle","walk","run","jump","down"})
                Require(art.Clip(pose).count>0 && art.Clip(pose).weaponSockets?.Length==art.Clip(pose).count,$"{id}: поза {pose} с точкой оружия на каждом кадре");
            Require(art.Clip("walk").count==20 && art.Clip("down").count==10 && !art.Clip("down").loop,$"{id}: шаг 20 кадров, гибель 10");
            var spawned=creature.spawn.Create(new TrainingPlacement {id="check",type=id,facing=1},"level");
            var items=creature.spawn.ReadInventory().Generate("check-world",spawned.id);
            var gun=items.Single(i=>i.definition==spawned.weapon);var weapon=WeaponCatalog.Get(spawned.weapon);
            Require(Mathf.Approximately(spawned.health,Num("comb","hp")) && (weapon.id=="weapon:robolaser:grade:0" || weapon.id=="weapon:robospark:grade:0") &&
                !weapon.UsesMagazine && gun.weaponState!=null && WeaponRigs.Get(weapon.id).HasMuzzle && Remains.Presentation.ShotArtwork.TryGet(weapon.id,out _),
                $"{id}: здоровье AllData, ствол {weapon.id} без магазина, с дулом и снарядом");
            var table=creature.spawn.ReadInventory();
            Require(table.entries.Length==5 && new[]{"item:scrap","item:frag","item:batt","item:metal_comp"}.All(d=>table.entries.Any(e=>e.definition==d)),
                $"{id}: в теле ствол и добыча LootGen robobrain");
            Require(voices.TrySelect(id,0,out _),"голос смерти "+id);
            var actor=new ActorState {id="b",actorType=id,anatomy=id,level="l",x=0,y=0,facing=1,health=spawned.health,weapon=weapon.id};
            gun.id="gun";actor.inventory.Add(gun);actor.weaponInventoryVersion=1;actor.equippedWeaponItem="gun";
            var mount=AtlasWeaponSockets.Anchor(actor);
            Require(Mathf.Abs(mount.x)<.05f && Mathf.Abs(mount.y-.8f)<.05f,$"{id}: точка ствола {mount} на 40 px над опорой");

            // UnitAIRobot.control: a target in sight 8 m away.
            var room=new RoomLayout {id="robobrain-check",left=-30,right=30,bottom=-1,top=30,ladders=Array.Empty<RoomLadder>(),
                platforms=new[]{new RoomSurface {x=-30,y=-1,width=60,height=1}}};
            RoomSurfaces.Prepare(room);
            var mind=new RaiderMind();var random=new System.Random(4);
            var senses=new RaiderSenses(true,new Vector2(8,.7f),true,new Vector2(8,.7f));
            var states=new System.Collections.Generic.HashSet<RaiderState>();int shots=0,attackFrames=0;
            for(int i=0;i<900;i++)
            {
                var intent=RaiderBrain.Tick(mind,s,actor,room,senses,false,random);
                states.Add(mind.state);
                Require(intent.jump==0 && intent.bodyHit==0 && intent.pushX==0 && intent.climb==0,$"{id}: робот не прыгает, не бьёт корпусом, не отступает и не лазает");
                if(mind.state==RaiderState.Pursue) Require(!intent.run,$"{id}: в преследовании идёт шагом");
                if(mind.state is RaiderState.Pursue or RaiderState.Hold) {attackFrames++;if(intent.shoot) shots++;}
            }
            Require(states.Contains(RaiderState.Notice) && states.Contains(RaiderState.Pursue) && states.Contains(RaiderState.Hold),$"{id}: замечает, преследует и держит позицию ({string.Join(",",states)})");
            Require(attackFrames>0 && shots>attackFrames*.6f && shots<attackFrames*.9f,$"{id}: стреляет очередями — {shots} из {attackFrames} кадров атаки (около 3/4)");
        }

        // A roller: a touch attacker (attKorp) of comb damage and tipdam, its own speed and jump, no hearing, loot dropped
        // on death, a ball turned by its visual; it rests, rolls at a noticed target, touches it with a hop, charges near it.
        private static void Roller(CreatureEntry creature,ActorTypeDefinition type,ExportedContentCatalog exported,ActorDeathAudioCatalog voices)
        {
            string id=creature.actorType;
            var unit=System.Xml.Linq.XElement.Parse(exported.Require("unit:"+creature.sourceId).rawXml);
            float Num(string element,string name)=>float.Parse((string)unit.Element(element).Attribute(name),System.Globalization.CultureInfo.InvariantCulture);
            Require(type.combatAi && !type.hasWeapon && type.naturalAttack!=null && type.presentation=="atlas" && !type.bodyInventory && type.hitMaterial=="metal" &&
                NpcCreatureBehaviours.Touches(type.behavior) && !NpcCreatureBehaviours.Flies(type.behavior) && NpcCreatureBehaviours.DropsLootOnDeath(type.behavior),
                $"{id}: роллер — боевой ИИ с касанием, добыча при гибели");
            type.naturalAttack.Validate(id);type.motion.Validate(id);type.perception.Validate();
            Require(Mathf.Approximately(type.naturalAttack.damage,Num("comb","damage")) && type.naturalAttack.damageType==DamageTypes.FromOriginal((int)Num("comb","tipdam")) &&
                Mathf.Approximately(type.motion.walkSpeed,Num("move","speed")*.6f) && Mathf.Approximately(type.naturalAttack.approachSpeed,type.motion.walkSpeed) &&
                Mathf.Approximately(type.motion.jumpSpeed,Num("move","jump")*.6f) && type.perception.hearingDistance==0,
                $"{id}: касание {type.naturalAttack.damage}, скорость {type.motion.walkSpeed}, прыжок {type.motion.jumpSpeed}, слух {type.perception.hearingDistance} не по AllData");
            Require(type.attackSounds is {Length:>0} && type.attackSounds.All(key=>AudioClipSource.Load(key)!=null) && voices.TrySelect(id,0,out _),$"{id}: звуки касания и голос смерти");
            Require(creature.deathLoot!=null && creature.deathLoot.Read().entries.Single() is {definition:"item:scrap",probability:var chance} && Mathf.Approximately(chance,.2f),
                $"{id}: при гибели выпадает металлолом с шансом 0,2 (LootGen roller)");
            var art=AtlasAnimationData.Get(type.artwork);
            foreach(string pose in new[]{"idle","walk","jump",Remains.Gameplay.NpcCreatureController.Charged,"down"}) Require(art.Clip(pose).count>0,$"{id}: поза {pose}");
            Require(art.Clip(Remains.Gameplay.NpcCreatureController.Charged).count==24 && art.Clip(Remains.Gameplay.NpcCreatureController.Charged).loop,$"{id}: заряженный вид — 24 кадра по кругу");
            var prefab=AssetDatabase.LoadAssetAtPath<Remains.Presentation.AtlasActorVisual>("Assets/Remains/Prefabs/Actors/Creatures/"+id+".prefab");
            Require(prefab!=null && prefab.Rolls,$"{id}: вид катится");
            var spawned=creature.spawn.Create(new TrainingPlacement {id="check",type=id,facing=1},"level");
            Require(Mathf.Approximately(spawned.health,Num("comb","hp")),$"{id}: здоровье AllData");

            var room=new RoomLayout {id="roller-check",left=-60,right=60,bottom=-1,top=30,ladders=Array.Empty<RoomLadder>(),
                platforms=new[]{new RoomSurface {x=-60,y=-1,width=120,height=1}}};
            RoomSurfaces.Prepare(room);
            var settings=MovementSettings.Load();settings.walkSpeed=type.naturalAttack.approachSpeed;settings.runSpeed=Math.Max(settings.runSpeed,settings.walkSpeed);settings.jumpSpeed=type.motion.jumpSpeed;
            var ground=new Remains.Gameplay.MovementController(room,settings);
            var controller=new Remains.Gameplay.NpcCreatureController();float dt=1f/30;
            var ball=new ActorState {id="r",actorType=id,anatomy=id,level="l",x=0,y=0,facing=1,health=spawned.health};
            for(int i=0;i<120;i++) Require(!controller.Step(ball,type,null,null,room,ground,dt),$"{id}: без цели не атакует");
            Require(ball.creature.mode==Remains.Gameplay.NpcCreatureController.Resting && ball.pose=="idle" && Mathf.Abs(ball.x)<.01f,$"{id}: без цели лежит ({ball.creature.mode}, x={ball.x})");
            var far=new Vector2(200,0);   // beyond any path it takes here: it never comes near
            for(int i=0;i<95;i++) controller.Step(ball,type,null,far,room,ground,dt);
            Require(ball.creature.mode==Remains.Gameplay.NpcCreatureController.Rolling,$"{id}: заметив цель, в течение 40–90 кадров катится");
            float start=ball.x;
            for(int i=0;i<30;i++) controller.Step(ball,type,null,far,room,ground,dt);
            Require(ball.x>start+1 && ball.vx>0 && ball.pose=="idle",$"{id}: катится к далёкой цели спокойным ({start} → {ball.x})");
            int touches=0;bool hopped=false,charged=false;
            for(int i=0;i<60;i++)
            {
                var near=ball.Position+Vector2.right*1.2f;
                if(controller.Step(ball,type,null,near,room,ground,dt)) touches++;
                hopped|=ball.vy>0;charged|=ball.pose==Remains.Gameplay.NpcCreatureController.Charged;
            }
            Require(touches>0 && hopped && charged,$"{id}: рядом с целью касается ({touches}), подпрыгивает ({hopped}) и заряжается ({charged})");
            for(int i=0;i<30*14;i++) controller.Step(ball,type,null,null,room,ground,dt);
            Require(ball.creature.mode==Remains.Gameplay.NpcCreatureController.Resting && ball.pose=="idle",$"{id}: потеряв цель, через 10 с успокаивается");
        }

        // A few seconds of each family on the real factory geometry with a target nearby: no NaN, never inside walls,
        // flyers move and change pose, a crawling slime stays on its floor.
        private static void Behaviours(WorldState world,RoomLayout room)
        {
            var controller=new Remains.Gameplay.NpcCreatureController();
            foreach(string kind in new[]{NpcCreatureBehaviours.Bat,NpcCreatureBehaviours.Bloat,NpcCreatureBehaviours.Slime,NpcCreatureBehaviours.SpriteBot,NpcCreatureBehaviours.SpiderMine,NpcCreatureBehaviours.Roller})
            {
                var actor=StateJson.Copy(world.actors.First(a=>ActorTypes.Get(a.actorType).behavior==kind));
                var type=ActorTypes.Get(actor.actorType);var hull=Anatomy.Movement(actor);
                var settings=MovementSettings.Load();settings.walkSpeed=type.naturalAttack?.approachSpeed??settings.walkSpeed;settings.runSpeed=Math.Max(settings.runSpeed,settings.walkSpeed);
                var ground=new Remains.Gameplay.MovementController(room,settings);
                var start=actor.Position;var poses=new System.Collections.Generic.HashSet<string>();
                Vector2 target=start+new Vector2(4,-1);
                bool crawling=false;float lowest=float.MaxValue;int wants=0;
                for(int i=0;i<600;i++)
                {
                    if(controller.Step(actor,type,null,target,room,ground,1f/60)) wants++;
                    Require(float.IsFinite(actor.x) && float.IsFinite(actor.y),$"{kind}: нечисловое положение");
                    if(NpcCreatureBehaviours.Flies(kind)) Require(ActorTerrainMotion.IsClear(room,hull,actor.Position),$"{kind}: внутри стены в {actor.Position}");
                    poses.Add(actor.pose);
                    if(kind==NpcCreatureBehaviours.Slime && actor.creature.mode=="crawl") {crawling=true;lowest=Math.Min(lowest,actor.y);}
                }
                if(NpcCreatureBehaviours.Flies(kind)) Require(Vector2.Distance(start,actor.Position)>.5f,$"{kind}: не сдвинулся за 10 с ({start} → {actor.Position})");
                if(kind==NpcCreatureBehaviours.Bat) Require(poses.Contains("walk") && poses.Contains("idle"),$"летучая мышь: позы {string.Join(",",poses)}");
                // A target in sight from the start: hostile at once, firing at some of its frames (0.1 a frame), not every one.
                if(kind==NpcCreatureBehaviours.SpriteBot)
                    Require(poses.SetEquals(new[]{Remains.Gameplay.NpcCreatureController.Hostile}) && wants>10 && wants<300,$"спрайтбот: позы {string.Join(",",poses)}, желаний стрелять {wants} из 600");
                // Woken by the target at once, it runs after 30–40 frames; it never attacks by itself (its death is the blast).
                if(kind==NpcCreatureBehaviours.SpiderMine)
                    Require(poses.Contains(Remains.Gameplay.NpcCreatureController.Woken) && (poses.Contains("walk") || poses.Contains("jump")) && wants==0 &&
                        ActorTerrainMotion.IsClear(room,hull,actor.Position),$"мина-паук: позы {string.Join(",",poses)}, режим {actor.creature.mode}, {actor.Position}");
                if(crawling) Require(start.y-lowest<.5f,$"слизь упала с пола: {start.y} → {lowest}");
                Debug.Log($"Поведение {kind} ({actor.actorType}): {start} → {actor.Position}, позы {string.Join(",",poses)}, режим {actor.creature.mode}.");
            }
        }
        // The generated prefab draws different frames as the walk pose time advances.
        // prefabName: a prefab shared by several types (a raider variant's weapons); default the type's own.
        private static void Animates(string actorType,AtlasActorArtwork art,string prefabName=null)
        {
            var prefab=AssetDatabase.LoadAssetAtPath<Remains.Presentation.AtlasActorVisual>("Assets/Remains/Prefabs/Actors/Creatures/"+(prefabName??actorType)+".prefab");
            Require(prefab!=null,"нет префаба "+(prefabName??actorType));
            var visual=UnityEngine.Object.Instantiate(prefab);
            try
            {
                var state=new ActorState {id="check",actorType=actorType,anatomy=actorType,pose="walk",facing=1};
                visual.Bind(state);var renderer=visual.GetComponent<SpriteRenderer>();
                var seen=new System.Collections.Generic.HashSet<Sprite>();
                for(int i=0;i<30;i++) {state.poseTime=i/30f;visual.Draw();seen.Add(renderer.sprite);}
                // A snapshot rewinding the same pose does not restart the drawn animation of an unarmed creature; an armed
                // one draws its replicated pose time (AtlasActorVisual.DrawnPoseTime), as the hand-made raider.
                if(art.Clip("walk").count>2 && !ActorTypes.Get(actorType).hasWeapon)
                {
                    state.poseTime=.5f;visual.Draw();var before=renderer.sprite;
                    state.poseTime=0;visual.Draw();
                    Require(renderer.sprite==before,$"{actorType}: откат времени позы перезапустил анимацию");
                }
                int expected=Math.Min(art.Clip("walk").count,2);
                Require(renderer.enabled && seen.Count>=expected,$"{actorType}: за секунду ходьбы кадров {seen.Count}, в клипе {art.Clip("walk").count}");
            }
            finally {UnityEngine.Object.DestroyImmediate(visual.gameObject);}
        }
        private static void Require(bool value,string message) {if(!value) throw new InvalidOperationException("Существа: "+message);}
    }
}
