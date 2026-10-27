using System;
using System.IO;
using Remains.Domain;
using Remains.Persistence;
using Remains.Serialization;
using UnityEngine;
using Remains.Content;

namespace Remains.Editor
{
    public static class GrenadeChecks
    {
        public static void MeasureFrameRates()
        {
            Remains.Content.WeaponCatalog.Load();
            foreach(int rate in new[]{30,60,144})
            {
                var room=new RoomLayout {left=-100,right=100,platforms=new[]{new RoomSurface {x=-100,y=-1,width=200,height=1}}};
                var state=new GrenadeState {y=3,vx=7,vy=3,remaining=2.5f};
                for(int i=0;i<rate*2;i++) GrenadeRules.Step(state,room,1f/rate);
                Debug.Log($"Граната через 2 с, FPS={rate}: x={state.x:R}, y={state.y:R}, vx={state.vx:R}, vy={state.vy:R}, запал={state.remaining:R}");
            }
        }
        //   batch -executeMethod Remains.Editor.GrenadeChecks.RunMessageBatch
        public static void RunMessageBatch()
        {
            try {CheckExplosionMessage();UnityEditor.EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);UnityEditor.EditorApplication.Exit(1);}
        }
        public static void Run()
        {
            CheckArtwork();
            CheckRecoveredPresence();
            CheckTerrain();
            CheckSettings();
            CheckLaunch();
            CheckRecordedConsume();
            CheckAimStrength();
            CheckFlightTime();
            CheckFreeFlight();
            CheckBounceFrameRates();
            CheckRotation();
            CheckExplosionMessage();
            var world=new WorldState();
            var source=new ActorState {id="source",owner="a",player=true,connected=true};
            var survivor=new ActorState {id="survivor",owner="b",player=true,connected=true,x=1};
            world.actors.Add(source);world.actors.Add(survivor);
            var grenade=new GrenadeState {id="g",attacker=source.id,sourcePlayer=true,level=source.level,owner="a",generation=1,x=1,remaining=1.4f};
            world.grenades.Add(grenade);source.connected=false;
            Require(GrenadeRules.ReassignAndReportChanges(world,new[]{"b"}),"Передача гранаты не отмечена для сохранения");
            Require(!GrenadeRules.ReassignAndReportChanges(world,new[]{"b"}),"Неизменная граната требует повторного снимка");
            Require(grenade.owner=="b" && grenade.generation==2 && grenade.remaining==1.4f,"Передача сохраняет остаток запала и меняет поколение");
            survivor.level="hub";GrenadeRules.Reassign(world,new[]{"b"});Require(grenade.owner==null,"Пустой уровень не получает фиктивного владельца host");
            survivor.level=source.level;GrenadeRules.Reassign(world,new[]{"b"});
            string path=Path.Combine(Path.GetTempPath(),"Remains-grenade-"+Guid.NewGuid().ToString("N"));
            try
            {
                var store=new SaveStore(path);store.SaveWorld(world);var loaded=store.LoadWorld();
                Require(loaded.grenades.Count==1 && loaded.grenades[0].remaining==1.4f && loaded.grenades[0].attacker==source.id,"Активная граната переживает сохранение без повторной выдачи предмета");
                grenade.remaining=0;var explosion=GrenadeRules.Detonate(world,grenade,Array.Empty<string>());
                Require(explosion!=null && GrenadeRules.Detonate(world,grenade,Array.Empty<string>())==null,"Один ID даёт один взрыв");
                var damage=new DamageReceiver(new PrototypeDamageRule());
                Require(GrenadeRules.Resolve(survivor,explosion,damage,grenade.Position) && survivor.health==30,"Пострадавший применяет собственное решение");
                var restored=survivor.Copy();
                Require(!GrenadeRules.Resolve(restored,explosion,new DamageReceiver(new PrototypeDamageRule()),grenade.Position) && restored.health==30,"Сохранённое решение не повторяет урон после смены симулятора");
                source.x=100;GrenadeRules.Resolve(source,explosion,damage,grenade.Position);source.x=1;
                Require(!GrenadeRules.Resolve(source,explosion,damage,grenade.Position) && source.health==100,"Промах не превращается в попадание после возвращения");
                GrenadeRules.ForgetHubActor(world,source.id);var returning=new ActorState {id=source.id,player=true,connected=true,x=1};
                Require(!GrenadeRules.Resolve(returning,explosion,damage,grenade.Position),"Исчезнувший в хабе персонаж не получает старый взрыв при новом входе");
                store.SaveWorld(world);loaded=store.LoadWorld();
                Require(loaded.grenades.Count==0 && loaded.explosions.Count==1 && loaded.Find(survivor.id).resolvedExplosions.Count==1,"Сохраняются результат взрыва и решения пострадавших");
                Debug.Log("GrenadeChecks PASS: передача, пустой уровень, сохранение полёта, однократный взрыв и решения.");
            }
            finally {if(Directory.Exists(path)) Directory.Delete(path,true);}
        }
        public static void CheckRecordedConsume()
        {
            const string transaction="grenade-consume:recovery-check";
            var source=new ActorState {id="player:grenade-journal",character="grenade-journal",player=true,level="hub",generation=7,lifecycleSequence=3};
            source.inventory.Add(new ItemStack {id="grenade-current",definition="grenade",count=5});
            source.inventoryAliases.Add("grenade-old","grenade-current");
            var stale=StateJson.FromJson<ActorState>(StateJson.ToJson(source));
            var world=new WorldState();world.actors.Add(source);
            Require(SharedInventory.Consume(source,"grenade","grenade-old",transaction) && InventoryJournal.Count(source,"grenade")==4,
                "Подтверждённый бросок не списал выбранную стопку");
            Require(source.inventoryUses.Count==1 && source.inventoryUses[0].id==transaction && source.inventoryUses[0].debits.Count==1 &&
                InventoryStacks.ResolveId(source.inventoryAliases,source.inventoryUses[0].debits[0].item)=="grenade-current" && source.inventoryUses[0].debits[0].count==1,
                "Расход гранаты отсутствует в журнале согласованной операции/aliases");
            world.completedTransactions.Add(transaction);
            string after=StateJson.ToJson(source);
            Require(!SharedInventory.Consume(source,"grenade","grenade-old",transaction) && StateJson.ToJson(source)==after,
                "Дубликат броска повторил расход или изменил журнал");
            var loadedWorld=StateJson.FromJson<WorldState>(StateJson.ToJson(world));
            var loadedStale=StateJson.FromJson<ActorState>(StateJson.ToJson(stale));
            var saved=loadedWorld.Find(source.id);
            var resumed=PlayerWorldEntry.Reconcile(loadedWorld,saved,loadedStale,out _);
            Require(InventoryJournal.Count(resumed,"grenade")==4 && resumed.inventoryUses.Count==1 && resumed.inventoryAliases["grenade-old"]=="grenade-current" &&
                InventoryJournal.Count(loadedStale,"grenade")==5 && InventoryJournal.Count(saved,"grenade")==4,
                "Устаревший профиль хаба вернул пятую гранату или изменил исходные состояния");
            var persistedProfile=StateJson.FromJson<ActorState>(StateJson.ToJson(resumed));
            var again=PlayerWorldEntry.Reconcile(loadedWorld,saved,persistedProfile,out _);
            Require(InventoryJournal.Count(again,"grenade")==4 && again.inventoryUses.Count==1 &&
                !SharedInventory.Consume(again,"grenade","grenade-old",transaction) && InventoryJournal.Count(again,"grenade")==4,
                "Повторный вход/serialized профиль/receipt дубликат повторил списание гранаты");

            const string selectedTransaction="grenade-consume:last-selected-recovery";
            var lastSelected=new ActorState {id="player:last-selected-grenade",character="last-selected-grenade",player=true,level="hub",
                weaponInventoryVersion=1,equippedWeaponItem="selected-old",weapon="grenade",weaponSelectionRevision=7};
            lastSelected.inventory.Add(new ItemStack {id="selected-current",definition="grenade",count=1});
            lastSelected.inventoryAliases.Add("selected-old","selected-current");
            var staleSelected=StateJson.Copy(lastSelected);
            Require(SharedInventory.Consume(lastSelected,"grenade","selected-old",selectedTransaction) &&
                InventoryJournal.Count(lastSelected,"grenade")==0 && lastSelected.equippedWeaponItem==null && lastSelected.weapon==null &&
                lastSelected.weaponSelectionRevision==8,"Расход последней выбранной гранаты не очистил выбор ровно один раз");
            var selectedWorld=new WorldState();selectedWorld.actors.Add(lastSelected);selectedWorld.completedTransactions.Add(selectedTransaction);
            selectedWorld=StateJson.FromJson<WorldState>(StateJson.ToJson(selectedWorld));
            staleSelected=StateJson.FromJson<ActorState>(StateJson.ToJson(staleSelected));
            var selectedSaved=selectedWorld.Find(lastSelected.id);
            var selectedResumed=PlayerWorldEntry.Reconcile(selectedWorld,selectedSaved,staleSelected,out _);
            Require(InventoryJournal.Count(selectedResumed,"grenade")==0 && selectedResumed.equippedWeaponItem==null && selectedResumed.weapon==null &&
                selectedResumed.weaponSelectionRevision==8 && selectedResumed.inventoryUses.Count==1 &&
                selectedResumed.inventoryAliases["selected-old"]=="selected-current" && InventoryJournal.Count(staleSelected,"grenade")==1 &&
                staleSelected.equippedWeaponItem=="selected-old" && staleSelected.weaponSelectionRevision==7,
                "Serialized устаревший профиль вернул последнюю гранату/выбор или изменил исходник");
            var selectedRepeated=PlayerWorldEntry.Reconcile(selectedWorld,selectedSaved,StateJson.Copy(selectedResumed),out _);
            Require(InventoryJournal.Count(selectedRepeated,"grenade")==0 && selectedRepeated.equippedWeaponItem==null && selectedRepeated.weapon==null &&
                selectedRepeated.weaponSelectionRevision==8 && selectedRepeated.inventoryUses.Count==1 &&
                !SharedInventory.Consume(selectedRepeated,"grenade","selected-old",selectedTransaction) && selectedRepeated.weaponSelectionRevision==8,
                "Повторный вход/дубликат последней гранаты повторил дебит или эпоху выбора");

            var final=new ActorState {weaponInventoryVersion=1,equippedWeaponItem="final-alias",weapon="grenade",weaponSelectionRevision=7};
            final.inventory.Add(new ItemStack {id="final-current",definition="grenade",count=1});
            final.inventory.Add(new ItemStack {id="other-grenades",definition="grenade",count=2});
            final.inventoryAliases.Add("final-alias","final-current");
            Require(SharedInventory.Consume(final,"grenade","final-alias","grenade-consume:final") && InventoryStacks.Find(final,"final-alias")==null &&
                final.equippedWeaponItem==null && final.weapon==null && final.weaponSelectionRevision==8 && final.inventory[0].count==2 && final.inventoryUses.Count==1,
                "Последняя выбранная граната/alias не сохранила прежний cleanup или потратила другую стопку");
            var holding=new ActorState {id="player:holding-grenade",character="holding-grenade",player=true,level="hub",
                weaponInventoryVersion=1,equippedWeaponItem="final-current",weapon="grenade",heldItemId="held-caps",heldItemPoseValid=true};
            holding.inventory.Add(new ItemStack {id="final-current",definition="grenade",count=1});
            holding.inventory.Add(new ItemStack {id="held-caps",definition="caps",count=3});
            var staleHolding=StateJson.Copy(holding);
            Require(SharedInventory.Consume(holding,"grenade","final-current","grenade-consume:while-held") &&
                holding.heldItemId=="held-caps" && holding.heldItemPoseValid && holding.inventoryUses.Count==1,
                "Cleanup последней гранаты сбросил другую удерживаемую вещь");
            var holdingWorld=new WorldState();holdingWorld.actors.Add(holding);holdingWorld.completedTransactions.Add("grenade-consume:while-held");
            holdingWorld=StateJson.FromJson<WorldState>(StateJson.ToJson(holdingWorld));
            var resumedHolding=PlayerWorldEntry.Reconcile(holdingWorld,holdingWorld.Find(holding.id),StateJson.Copy(staleHolding),out _);
            Require(InventoryJournal.Count(resumedHolding,"grenade")==0 && resumedHolding.inventoryUses.Count==1 &&
                resumedHolding.heldItemId=="held-caps" && resumedHolding.heldItemPoseValid && InventoryStacks.Find(resumedHolding,"held-caps").count==3 &&
                resumedHolding.weaponSelectionRevision==staleHolding.weaponSelectionRevision,
                "Вход с расходом последней гранаты сбросил другую held вещь/позу или изменил её эпоху");

            var reserved=new ActorState();reserved.inventory.Add(new ItemStack {id="reserved",definition="grenade",count=5});
            reserved.inventory.Add(new ItemStack {id="free",definition="grenade",count=2});reserved.inventoryReservations.Add("reserved",5);
            string reservedBefore=StateJson.ToJson(reserved);
            Require(!SharedInventory.Consume(reserved,"grenade","reserved","grenade-consume:reserved") && StateJson.ToJson(reserved)==reservedBefore,
                "Зарезервированный бросок обошёл резерв другой стопкой или оставил журнал");
            var wrong=new ActorState();wrong.inventory.Add(new ItemStack {id="chosen",definition="grenade",count=5});
            wrong.inventory.Add(new ItemStack {id="heal",definition="heal",count=2});string wrongBefore=StateJson.ToJson(wrong);
            Require(!SharedInventory.Consume(wrong,"heal","chosen","grenade-consume:wrong") && StateJson.ToJson(wrong)==wrongBefore,
                "Неверное definition выбранного ID списало предмет или записало журнал");
        }
        private static void CheckRotation()
        {
            var room=new RoomLayout {left=-100,right=100,platforms=Array.Empty<RoomSurface>()};
            foreach(int rate in new[]{30,60,144})
            {
                var state=new GrenadeState {id="spin",rotationVersion=1,angle=15,angularVelocity=120,mirrored=true,y=20,vx=3,vy=6,remaining=2};
                for(int i=0;i<rate;i++) GrenadeRules.Step(state,room,1f/rate);
                Require(Mathf.Abs(state.angle-135)<.001f && state.angularVelocity==120 && state.mirrored,
                    $"Вращение свободного полёта зависит от FPS: {rate}, угол={state.angle}");
                Require(Mathf.Abs(state.x-3)<.0001f && Mathf.Abs(state.y-17)<.0001f,"Вращение изменило поступательный полёт");
            }
            var original=new GrenadeState {id="resume-spin",rotationVersion=1,angle=15,angularVelocity=120,mirrored=true,y=20,remaining=2};
            GrenadeRules.Step(original,room,.371f);var restored=original.Copy();
            Require(restored.angle==original.angle && restored.angularVelocity==120 && restored.mirrored && restored.rotationVersion==1,
                "Сохранение потеряло угол, скорость или отражение гранаты");
            var paused=restored.Copy();GrenadeRules.Step(restored,room,0);
            Require(restored.angle==paused.angle && restored.angularVelocity==paused.angularVelocity,"Пауза изменила вращение");
            GrenadeRules.Step(restored,room,.629f);Require(Mathf.Abs(restored.angle-135)<.001f,"Возобновление сбросило фазу вращения");
            room.platforms=new[]{new RoomSurface {x=-100,y=-1,width=200,height=1}};
            var bounce=new GrenadeState {id="spin-impact",rotationVersion=1,y=.3f,vy=-5,angle=30,angularVelocity=180,remaining=2};
            GrenadeRules.Step(bounce,room,.1f);
            Require(bounce.angularVelocity<180 && bounce.angularVelocity>=0 && bounce.angle>30,"Удар не погасил вращение или сбросил угол");
            var legacy=new GrenadeState {id="legacy-spin",x=3,y=4,remaining=1};
            GrenadeRotation.Prepare(legacy);
            Require(legacy.rotationVersion==1 && legacy.angle==0 && legacy.angularVelocity==0 && legacy.x==3 && legacy.y==4 && legacy.remaining==1,
                "Миграция старой гранаты изменила её полёт");
            legacy.rotationVersion=2;bool rejected=false;
            try {GrenadeRotation.Prepare(legacy);} catch(InvalidOperationException) {rejected=true;}
            Require(rejected,"Неизвестная версия вращения молча принята");
        }
        private static void CheckArtwork()
        {
            var prefab=Remains.Presentation.GrenadeArtwork.LoadPrefab();
            var first=UnityEngine.Object.Instantiate(prefab);var second=UnityEngine.Object.Instantiate(prefab);
            try
            {
                Require(Mathf.Approximately(prefab.sprite.pixelsPerUnit,400) && prefab.sprite.texture.width==304 && prefab.sprite.texture.height==304,
                    "Потеряна исходная графика гранаты8× с прежним world canvas.");
                ItemHubVectorReserveAuthoring.Check();
                var tint=prefab.color;first.color=Color.magenta;first.transform.position=Vector3.one;
                Require(second.color==tint && prefab.color==tint && first.sprite==prefab.sprite &&
                    first.sharedMaterial==prefab.sharedMaterial && second.transform.position==prefab.transform.position,
                    "Экземпляры гранаты должны читать ресурсы префаба и сохранять независимость состояния");
            }
            finally {UnityEngine.Object.DestroyImmediate(first.gameObject);UnityEngine.Object.DestroyImmediate(second.gameObject);}
            var asset=GameAssets.Load<Remains.Content.WeaponAsset>("Content/weapons/grenade");
            Require(asset.Presentation!=null && asset.Presentation.Id=="grenade" &&
                asset.Presentation.Artwork.sprite==prefab.sprite && asset.Presentation.Artwork.sharedMaterial==prefab.sharedMaterial &&
                !asset.Presentation.ReadRig().HasMuzzle && asset.Presentation.Artwork.sortingOrder==100,
                "Удерживаемая граната должна использовать графику полёта и не иметь фиктивного дула");
            var iconRoot=new GameObject("Проверка иконки гранаты",typeof(RectTransform),typeof(UnityEngine.UI.Image));
            try
            {
                var icon=iconRoot.GetComponent<UnityEngine.UI.Image>();
                new Remains.Presentation.InventoryArtwork().Set(icon,"grenade");
                var expected=GameAssets.Load<Remains.Content.InventoryIconAsset>("Original/Inventory/icons").CreateLookup()["grenade"];
                Require(icon.sprite==expected,"Удерживаемый префаб не должен подменять авторскую иконку инвентаря");
            }
            finally {UnityEngine.Object.DestroyImmediate(iconRoot);}
            try
            {
                asset.SetGrenadeFlight(null);bool rejected=false;
                try {Remains.Presentation.GrenadeArtwork.LoadPrefab();} catch(InvalidOperationException) {rejected=true;}
                Require(rejected,"Потеря ссылки на графику гранаты не должна скрываться runtime-сборкой");
            }
            finally {asset.SetGrenadeFlight(prefab);}
        }
        private static void CheckTerrain()
        {
            foreach(float frameTime in new[]{1f/30,1f/60,1f/144})
            {
                float dt=Mathf.Max(frameTime,(float)GrenadeRules.SimulationStep);
                var room=new RoomLayout {left=-20,right=20,platforms=new[]{new RoomSurface {x=1,y=0,width=.05f,height=4}}};
                var right=new GrenadeState {x=.7f,y=2,vx=100,remaining=2};
                GrenadeRules.Step(right,room,dt);
                Require(right.x<=.9001f && Mathf.Approximately(right.vx,-40),"Граната должна отскочить от тонкой стены справа с заданной потерей скорости");
                var left=new GrenadeState {x=1.35f,y=2,vx=-100,remaining=2};
                GrenadeRules.Step(left,room,dt);
                Require(left.x>=1.1499f && left.vx>0,"Граната должна отскочить от стены слева");
                room.platforms=new[]{new RoomSurface {x=-10,y=2,width=20,height=.05f}};
                var up=new GrenadeState {x=0,y=1.7f,vy=100,remaining=2};
                GrenadeRules.Step(up,room,dt);
                Require(up.y<=1.9001f && up.vy<0,"Потолок должен отражать гранату вниз");
                var down=new GrenadeState {x=0,y=2.35f,vy=-100,vx=2,remaining=2};
                GrenadeRules.Step(down,room,dt);
                Require(down.y>=2.1499f && down.vy>0 && Mathf.Approximately(down.vx,1.2f),"Пол должен отражать и тормозить гранату");
                room.platforms[0].oneWay=true;
                up=new GrenadeState {x=0,y=1.7f,vy=100,remaining=2};
                GrenadeRules.Step(up,room,dt);
                Require(up.y>2 && up.vy>0,"Односторонняя платформа не является потолком");
                Require(Math.Abs(up.remaining-up.simulationRemainder-(2-dt))<.00001,"Столкновения не должны менять темп запала");
            }
        }
        private static void CheckSettings()
        {
            var definition=Remains.Content.WeaponCatalog.Get("grenade");
            Require(Mathf.Approximately(definition.grenadeFuseSeconds,2.5f),"Исходный запал должен быть 2,5 с");
            var invalid=Remains.Serialization.StateJson.Copy(definition);
            foreach(float value in new[]{-1f,1.1f,float.NaN})
            {
                invalid.grenadeRestitution=value;
                bool rejected=false;
                try {Remains.Content.WeaponCatalogLoader.Load(
                    Remains.Serialization.StateJson.ToJson(new Remains.Content.WeaponList {weapons=new[]{invalid}}),
                    Path.Combine(Path.GetTempPath(),"remains-no-mods-"+Guid.NewGuid().ToString("N")));}
                catch(InvalidDataException) {rejected=true;}
                Require(rejected,"Некорректный отскок должен отклоняться при загрузке");
            }
            float saved=definition.grenadeRestitution;
            invalid=Remains.Serialization.StateJson.Copy(definition);invalid.grenadeGravity=0;
            bool missingGravityRejected=false;
            try {Remains.Content.WeaponCatalogLoader.Load(
                Remains.Serialization.StateJson.ToJson(new Remains.Content.WeaponList {weapons=new[]{invalid}}),
                Path.Combine(Path.GetTempPath(),"remains-no-mods-"+Guid.NewGuid().ToString("N")));}
            catch(InvalidDataException) {missingGravityRejected=true;}
            Require(missingGravityRejected,"Отсутствующее ускорение гранаты не должно незаметно отключать падение");
            try
            {
                definition.grenadeRestitution=.2f;
                var room=new RoomLayout {left=-20,right=20,platforms=new[]{new RoomSurface {x=1,y=0,width=.05f,height=4}}};
                var grenade=new GrenadeState {x=.7f,y=2,vx=100,remaining=1.1f};
                GrenadeRules.Step(grenade,room,.01f);
                Require(Mathf.Approximately(grenade.vx,-20),"Отскок должен читать загруженное определение");
                Require(Math.Abs(grenade.remaining-grenade.simulationRemainder-1.09)<.00001,"Настройки не должны перезапускать уже горящий запал");
            }
            finally {definition.grenadeRestitution=saved;}
        }
        private static void CheckLaunch()
        {
            var actor=new ActorState {id="thrower",x=5,y=2,level="hub",generation=7,lifecycleSequence=3};
            actor.inventory.Add(SharedInventory.Create("grenade",2));
            var standing=Remains.Networking.GrenadeLaunchRequest.Capture(actor,new Vector2(8,3));
            actor.crouched=true;
            var crouching=Remains.Networking.GrenadeLaunchRequest.Capture(actor,new Vector2(8,3));
            Require(crouching.originY<standing.originY,"Приседание должно снижать точку броска");
            float capturedX=crouching.originX;actor.x=-10;actor.y=0;
            Require(Remains.Networking.GrenadeLaunchRequest.Matches(actor,crouching) && crouching.originX==capturedX,
                "Старая позиция координатора не должна подменять локальную точку броска");
            var capturedVelocity=new Vector2(crouching.vx,crouching.vy);
            Require(capturedVelocity.sqrMagnitude>0 && capturedVelocity!=new Vector2(standing.vx,standing.vy),
                "Скорость запроса должна учитывать локальную стойку при выпуске");
            actor.level="factory:0";
            Require(!Remains.Networking.GrenadeLaunchRequest.Matches(actor,crouching),"Старый бросок не переносится в другой уровень");
            actor.level="hub";actor.generation++;
            Require(!Remains.Networking.GrenadeLaunchRequest.Matches(actor,crouching),"Переподключение отменяет старый запрос");
            actor.generation--;actor.lifecycleSequence++;
            Require(!Remains.Networking.GrenadeLaunchRequest.Matches(actor,crouching),"Новое воплощение отменяет старый запрос");
            actor.lifecycleSequence--;crouching.aimX=float.NaN;
            Require(!Remains.Networking.GrenadeLaunchRequest.Matches(actor,crouching),"Неконечный прицел должен отклоняться до расхода");
            crouching.aimX=8;crouching.vx=float.PositiveInfinity;
            Require(!Remains.Networking.GrenadeLaunchRequest.Matches(actor,crouching),"Неконечная скорость должна отклоняться до расхода");
            actor.weaponInventoryVersion=1;actor.crouched=false;
            var chosen=SharedInventory.Create("grenade",3);actor.inventory.Add(chosen);
            Require(WeaponInventory.Select(actor,chosen.id,out _),"Стопка гранат не выбирается");
            var aim=actor.Position+new Vector2(4,2);Remains.Gameplay.WeaponPose.Aim(actor,aim);
            var held=Remains.Networking.GrenadeLaunchRequest.Capture(actor,aim);
            Require(held.item==chosen.id && (new Vector2(held.originX,held.originY)-Remains.Gameplay.WeaponPose.Mount(actor)).magnitude<.00001f,
                "Бросок должен начинаться в удерживаемом предмете и расходовать выбранную стопку");
            Require(SharedInventory.Consume(actor,"grenade",held.item) && chosen.count==2 && actor.inventory[0].count==2,
                "Бросок потратил другую стопку");
            actor.inventoryReservations[chosen.id]=2;
            Require(!SharedInventory.Consume(actor,"grenade",held.item) && actor.inventory[0].count==2,
                "Резерв выбранной стопки не должен обходиться расходом другой стопки");
        }
        private static void CheckAimStrength()
        {
            var definition=Remains.Content.WeaponCatalog.Get("grenade");
            var origin=new Vector2(0,.8f);
            Vector2 Launch(Vector2 aim)=>Remains.Gameplay.GrenadeLaunch.Velocity(origin,aim,0,definition);
            var near=Launch(new Vector2(.5f,0));var far=Launch(new Vector2(5,0));
            Require(near.magnitude<far.magnitude,"Близкий прицел должен ослаблять бросок");
            var low=Launch(new Vector2(1,1));var high=Launch(new Vector2(1,3));
            Require(high.magnitude>low.magnitude && high.y>low.y,"Поднятие прицела должно увеличивать усилие до предела");
            var distant=Launch(new Vector2(100,50));
            Require(Mathf.Abs(distant.magnitude-definition.grenadeMaxLaunchSpeed)<.00001f,"Бросок не должен превышать заданную скорость");
            var down=Launch(new Vector2(0,-3));
            Require(down.x==0 && down.y<0,"Оставшийся вертикальный прицел должен бросать вниз без добавочного подъёма");
            Require(Launch(origin)==Vector2.zero,"Прицел в точке выпуска не должен задавать произвольное направление");
            var left=Launch(new Vector2(-5,0));
            Require(Mathf.Abs(left.x+far.x)<.00001f && Mathf.Abs(left.y-far.y)<.00001f,"Направления броска должны быть симметричны");
            var translated=Remains.Gameplay.GrenadeLaunch.Velocity(origin+new Vector2(17,-9),new Vector2(22,-9),-9,definition);
            Require((translated-far).magnitude<.00001f,"Сила броска не должна зависеть от положения комнаты в мире");
            var room=new RoomLayout {left=-100,right=100,platforms=Array.Empty<RoomSurface>()};
            Vector2? checkpoint=null;
            foreach(int rate in new[]{30,60,144})
            {
                var state=new GrenadeState {y=20,vx=far.x,vy=far.y,remaining=2};
                for(int i=0;i<rate;i++) GrenadeRules.Step(state,room,1f/rate);
                if(checkpoint.HasValue) Require((state.Position-checkpoint.Value).magnitude<.0001f,"Прицельный бросок зависит от FPS");
                checkpoint=state.Position;
            }
            foreach(float value in new[]{0f,-1f,float.NaN,float.PositiveInfinity})
            {
                var invalid=Remains.Serialization.StateJson.Copy(definition);invalid.grenadeFullEffortDistance=value;
                bool rejected=false;
                try {Remains.Content.WeaponCatalogLoader.Load(
                    Remains.Serialization.StateJson.ToJson(new Remains.Content.WeaponList {weapons=new[]{invalid}}),
                    Path.Combine(Path.GetTempPath(),"remains-no-mods-"+Guid.NewGuid().ToString("N")));}
                catch(InvalidDataException) {rejected=true;}
                Require(rejected,"Ошибочное расстояние полного усилия должно отклоняться с диагностикой");
            }
        }
        private static void CheckFlightTime()
        {
            var room=new RoomLayout {left=-100,right=100,platforms=Array.Empty<RoomSurface>()};
            var state=new GrenadeState {x=0,y=20,vx=3,vy=0,remaining=.1f};
            GrenadeRules.Step(state,room,.5f);
            Require(state.remaining==0 && Mathf.Approximately(state.x,.3f),"Длинный кадр не должен продлевать полёт за конец запала");
            Require(Mathf.Approximately(state.vy,-1.8f),"Граната должна использовать своё ускорение 18 м/с²");
            var position=state.Position;GrenadeRules.Step(state,room,.2f);
            Require(state.Position==position,"Истёкший запал не должен позволять дополнительный шаг");
            state=new GrenadeState {y=20,remaining=2};
            GrenadeRules.Step(state,room,0);
            Require(state.y==20 && state.remaining==2 && state.vy==0,"Нулевой шаг не должен расходовать запал или менять скорость");
        }
        private static void CheckFreeFlight()
        {
            var room=new RoomLayout {left=-100,right=100,platforms=Array.Empty<RoomSurface>()};
            foreach(int rate in new[]{1,30,60,144})
            {
                var state=new GrenadeState {x=0,y=20,vx=3,vy=6,remaining=2};
                for(int i=0;i<rate;i++) GrenadeRules.Step(state,room,1f/rate);
                // Independent kinematic checkpoint after one second: acceleration is -18 m/s².
                Require(Mathf.Abs(state.x-3)<.0001f && Mathf.Abs(state.y-17)<.0001f &&
                    Mathf.Abs(state.vy+12)<.0001f && Mathf.Abs(state.remaining-1)<.0001f,
                    $"Свободный полёт зависит от частоты кадров: {rate}, position={state.Position}, vy={state.vy}");
            }
            var original=new GrenadeState {y=20,vx=3,vy=6,remaining=2};
            GrenadeRules.Step(original,room,.37f);
            var restored=original.Copy();
            GrenadeRules.Step(restored,room,.63f);
            Require(Mathf.Abs(restored.y-17)<.0001f && Mathf.Abs(restored.x-3)<.0001f,
                "Восстановление посреди полёта не должно менять траекторию");
        }
        private static void CheckBounceFrameRates()
        {
            var room=new RoomLayout {left=-100,right=100,platforms=new[]{new RoomSurface {x=-100,y=-1,width=200,height=1},
                new RoomSurface {x=9,y=0,width=.1f,height=5}}};
            GrenadeState reference=null;
            foreach(int rate in new[]{30,60,144})
            {
                var state=new GrenadeState {y=3,vx=7,vy=3,remaining=2.5f};
                for(int i=0;i<rate*2;i++) GrenadeRules.Step(state,room,1f/rate);
                if(reference==null) reference=state;
                else Require(Vector2.Distance(reference.Position,state.Position)<.00001f &&
                    Vector2.Distance(reference.Velocity,state.Velocity)<.00001f && Mathf.Abs(reference.remaining-state.remaining)<.00001f,
                    $"Отскоки зависят от FPS: {rate}, позиция={state.Position}, эталон={reference.Position}");
            }
            var uninterrupted=new GrenadeState {y=3,vx=7,vy=3,remaining=2.5f};
            GrenadeRules.Step(uninterrupted,room,.371f);
            Require(uninterrupted.simulationRemainder>0,"Проверка передачи должна попадать между тактами");
            var restored=uninterrupted.Copy();
            Require(Math.Abs(restored.simulationRemainder-uninterrupted.simulationRemainder)<1e-12,"Сохранение потеряло остаток такта");
            GrenadeRules.Step(restored,room,1.629f);
            Require(Vector2.Distance(reference.Position,restored.Position)<.00001f &&
                Vector2.Distance(reference.Velocity,restored.Velocity)<.00001f,
                "Неравномерные кадры или восстановление сбили такты отскоков");
        }
        private static void CheckRecoveredPresence()
        {
            var world=new WorldState();
            var active=new ActorState {id="active",player=true,connected=true,level="hub",owner="live"};
            var waiting=new ActorState {id="waiting",player=true,level="hub",owner="stale",attackBusy=true};
            var body=new ActorState {id="body",player=true,level="factory:0",owner="stale",down=true};
            world.actors.Add(active);world.actors.Add(waiting);world.actors.Add(body);
            new OwnershipService().Reassign(world,new[]{"live"});
            Require(!ActorPresence.IsPresent(waiting) && waiting.owner==null && !waiting.attackBusy,"Сохранённый персонаж хаба не становится телом с чужим владельцем");
            Require(ActorPresence.IsPresent(body),"Отключённое тело вне хаба остаётся в мире");
            var grenade=new GrenadeState {id="presence",level="hub",attacker=active.id,sourcePlayer=true};world.grenades.Add(grenade);
            var explosion=GrenadeRules.Detonate(world,grenade,Array.Empty<string>());
            Require(explosion.players.Count==1 && explosion.players[0]==active.id && !AttackScope.Includes(explosion.attack,waiting),"Взрыв не адресуется отсутствующему персонажу хаба");
            waiting.connected=true;
            Require(ActorPresence.IsPresent(waiting) && !GrenadeRules.Resolve(waiting,explosion,new DamageReceiver(new PrototypeDamageRule()),Vector2.zero),"Вход в хаб не превращает прошлый взрыв в новое попадание");
            explosion.attack.level=body.level;
            Require(AttackScope.Includes(explosion.attack,body),"Отключённое тело вне хаба остаётся доступно урону");
        }
        // The explosion as clients receive it, including the grenade probe's repeat of a received one: AttackMessage on
        // the wire, then the client handler's Read. A natural type or a volley here broke the client in GameplayAudio.
        private static void CheckExplosionMessage()
        {
            var world=new WorldState();
            var grenade=new GrenadeState {id="message",attacker="thrower",sourcePlayer=true,level="hub",owner="a",generation=1,x=1};
            world.grenades.Add(grenade);var sent=GrenadeRules.Detonate(world,grenade,Array.Empty<string>()).attack;
            static AttackEvent Hop(AttackEvent attack)
            {
                var writer=new Mirror.NetworkWriter();writer.Write(Remains.Networking.AttackMessage.From(attack));
                var reader=new Mirror.NetworkReader(writer.ToArraySegment());var message=reader.Read<Remains.Networking.AttackMessage>();
                Require(reader.Position==writer.Position,"AttackMessage оставил непрочитанные bytes");
                return message.Read();
            }
            foreach(var received in new[]{Hop(sent),Hop(Hop(sent))})
            {
                Require(!NpcNaturalAttack.IsNatural(received) && received.naturalAttackType==null && received.naturalAttackTarget==null && received.volley==null,
                    $"Взрыв гранаты после AttackMessage стал естественной атакой или дробиной залпа: type='{received.naturalAttackType}', volley='{received.volley}'");
                Require(received.id==sent.id && received.weapon=="grenade" && received.level==sent.level && received.attacker==sent.attacker &&
                    received.rangedPose==null && received.meleeAnchor==null && received.factoryEnemy==null,"AttackMessage изменил взрыв гранаты");
            }
            Debug.Log("GrenadeChecks PASS: взрыв гранаты через AttackMessage и повтор пробы — не естественная атака, не залп, отсутствующие поля остаются null.");
        }
        private static void Require(bool condition,string reason) {if(!condition) throw new InvalidOperationException("GrenadeChecks: "+reason);}
    }
}
