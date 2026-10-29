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
            foreach(var creature in catalog.Creatures)
            {
                var type=ActorTypes.Get(creature.actorType);
                CheckCorpsePresentation(creature,type);
                if(NpcCreatureBehaviours.Fixed(type.behavior)) {Turret(creature,type,exported);continue;}
                if(type.behavior==NpcCreatureBehaviours.Raider) {Raider(creature,type,exported,voices);continue;}
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
                var random=new System.Collections.Generic.HashSet<string>(FactoryRandomEncounters.Plan(root).Where(e=>!e.Hidden && Placed(e.Unit,e.Danger,e.LocalId))
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
                Require(world.actors.Any(V6),"рейдеры завода установлены");
                Require(world.actors.Any(V7),"рукопашные рейдеры завода установлены");
                Upgrade("base:factory-generated:creatures-v6",V7,"переход v6 → v7 добавляет только рукопашных рейдеров");
                Upgrade("base:factory-generated:creatures-v5",V6,"переход v5 → v7 добавляет только рейдеров");
                Upgrade("base:factory-generated:creatures-v4",V5,"переход v4 → v6 добавляет только случайных врагов комнат и рейдеров");
                Upgrade("base:factory-generated:creatures-v3",a=>V4(a) || V5(a),"переход v3 → v5 добавляет турели и случайных врагов");
                Upgrade("base:factory-generated:creatures-v2",a=>V3(a) || V4(a) || V5(a),"переход v2 → v5 добавляет летунов, слизь, турели и случайных врагов");
                Upgrade("base:factory-generated:creatures-v1",a=>V2(a) || V3(a) || V4(a) || V5(a),"переход v1 → v5 добавляет скорпионов, летунов, слизь, турели и случайных врагов");
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
                Require((Vector2)visual.transform.position==actor.Position,"опора трупа сдвинула живое существо "+actor.actorType);
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

        // A few seconds of each family on the real factory geometry with a target nearby: no NaN, never inside walls,
        // flyers move and change pose, a crawling slime stays on its floor.
        private static void Behaviours(WorldState world,RoomLayout room)
        {
            var controller=new Remains.Gameplay.NpcCreatureController();
            foreach(string kind in new[]{NpcCreatureBehaviours.Bat,NpcCreatureBehaviours.Bloat,NpcCreatureBehaviours.Slime})
            {
                var actor=StateJson.Copy(world.actors.First(a=>ActorTypes.Get(a.actorType).behavior==kind));
                var type=ActorTypes.Get(actor.actorType);var hull=Anatomy.Movement(actor);
                var settings=MovementSettings.Load();settings.walkSpeed=type.naturalAttack.approachSpeed;settings.runSpeed=Math.Max(settings.runSpeed,settings.walkSpeed);
                var ground=new Remains.Gameplay.MovementController(room,settings);
                var start=actor.Position;var poses=new System.Collections.Generic.HashSet<string>();
                Vector2 target=start+new Vector2(4,-1);
                bool crawling=false;float lowest=float.MaxValue;
                for(int i=0;i<600;i++)
                {
                    controller.Step(actor,type,null,target,room,ground,1f/60);
                    Require(float.IsFinite(actor.x) && float.IsFinite(actor.y),$"{kind}: нечисловое положение");
                    if(NpcCreatureBehaviours.Flies(kind)) Require(ActorTerrainMotion.IsClear(room,hull,actor.Position),$"{kind}: внутри стены в {actor.Position}");
                    poses.Add(actor.pose);
                    if(kind==NpcCreatureBehaviours.Slime && actor.creature.mode=="crawl") {crawling=true;lowest=Math.Min(lowest,actor.y);}
                }
                if(NpcCreatureBehaviours.Flies(kind)) Require(Vector2.Distance(start,actor.Position)>.5f,$"{kind}: не сдвинулся за 10 с ({start} → {actor.Position})");
                if(kind==NpcCreatureBehaviours.Bat) Require(poses.Contains("walk") && poses.Contains("idle"),$"летучая мышь: позы {string.Join(",",poses)}");
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
