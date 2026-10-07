using System;
using System.Linq;
using System.Reflection;
using Remains.Content;
using Remains.Domain;
using Remains.Networking;
using Remains.Presentation;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class CasingEffectsChecks
    {
        public const string SpritePath="Assets/Remains/Resources/Original/Weapons/ShotEffects/casing.png";
        public static void Run()
        {
            CheckNativeSprite();CasingEffectCatalog.Load();
            Require(CasingEffectCatalog.TryGet("pistol",out var profile),"Не установлен явный профиль пистолета");
            Require(!CasingEffectCatalog.TryGet("energy",out _) && Resources.Load<WeaponAsset>("Content/weapons/energy").CasingProfile==null,"Energy получила неподтверждённую гильзу");
            Require(profile.Sprite==AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath),"Профиль не использует исходную гильзу");
            Reject(()=>CasingEffectCatalog.TryGet("missing:casing-weapon",out _),"Неизвестное оружие молча получило no-casing");
            CheckInvalidProfile(profile.Sprite);
            foreach(int fps in new[]{30,60,144})CheckDetached(profile,fps);
            CheckPool();CheckContexts();
            Debug.Log("Гильзы и captured shot PASS: исходный вектор8×/сохранённая регистрация, pool64, duplicate/rejected/energy, detached30/60/144, paused, same-world replacement, world/level teardown, item/selection/owner/generation/lifecycle guards. Original runtime не сравнивался.");
        }
        private static void CheckNativeSprite()
        {
            VectorArtworkReserveChecks.VerifyEntry("casing");
        }
        private static void CheckInvalidProfile(Sprite sprite)
        {
            var profile=ScriptableObject.CreateInstance<CasingEffectProfile>();var catalog=ScriptableObject.CreateInstance<WeaponAssetCatalog>();
            var weapon=ScriptableObject.CreateInstance<WeaponAsset>();var duplicate=ScriptableObject.CreateInstance<WeaponAsset>();
            try
            {
                Reject(()=>profile.Configure(null,Vector2.zero),"Отсутствующий sprite принят");
                Reject(()=>profile.Configure(sprite,new Vector2(float.NaN,0)),"Неконечный socket принят");
                Reject(()=>profile.Configure(sprite,Vector2.zero,lifetime:0),"Нулевой срок принят");
                Reject(()=>profile.Configure(sprite,Vector2.zero,gravity:-1),"Отрицательная gravity принята");
                Reject(()=>profile.Configure(sprite,Vector2.zero,rotationSpeed:float.PositiveInfinity),"Неконечное вращение принято");
                profile.Configure(sprite,Vector2.zero);weapon.SetDefinition(new WeaponDefinition {id="test:pistol",projectileSpeed=1});weapon.SetCasingProfile(profile);
                duplicate.SetDefinition(new WeaponDefinition {id="test:pistol",projectileSpeed=1});catalog.SetAssets(new[]{weapon,duplicate});
                Reject(()=>CasingEffectCatalog.Load(catalog),"Дубли назначения не отклонены");
                Require(CasingEffectCatalog.TryGet("pistol",out _),"Неудачная загрузка опубликовала частичный каталог");
                catalog.SetAssets(new[]{weapon});weapon.SetDefinition(new WeaponDefinition {id="test:melee",projectileSpeed=0});
                Reject(()=>CasingEffectCatalog.Load(catalog),"Melee получила гильзу");
                catalog.SetAssets(new WeaponAsset[]{null});Reject(()=>CasingEffectCatalog.Load(catalog),"Отсутствующий ассет молча пропущен");
                weapon.SetDefinition(new WeaponDefinition {id="test:pistol",projectileSpeed=1});catalog.SetAssets(new[]{weapon});
                UnityEngine.Object.DestroyImmediate(profile);Reject(()=>CasingEffectCatalog.Load(catalog),"Назначенный destroyed SO молча стал намеренным no-casing");
            }
            finally {UnityEngine.Object.DestroyImmediate(profile);UnityEngine.Object.DestroyImmediate(catalog);UnityEngine.Object.DestroyImmediate(weapon);UnityEngine.Object.DestroyImmediate(duplicate);}
        }
        private static ActorState Actor()=>new()
        {id="casing-actor",owner="casing-owner",player=true,connected=true,level="hub",weapon="pistol",generation=2,lifecycleSequence=3,
            weaponInventoryVersion=1,equippedWeaponItem="pistol-instance-A",weaponSelectionRevision=7};
        private static CoopSession Session(GameObject root,ActorState actor)
        {
            var session=root.AddComponent<CoopSession>();var world=new WorldState {worldId="casing-check-world"};world.actors.Add(actor);
            typeof(CoopSession).GetProperty("World").SetValue(session,world);typeof(CoopSession).GetProperty("LocalActorId").SetValue(session,actor.id);
            typeof(CoopSession).GetProperty("LocalOwner").SetValue(session,actor.owner);return session;
        }
        private static AttackEvent Shot(CoopSession session,string id="casing-shot")
        {
            var actor=session.LocalActor;return new AttackEvent {id=id,attacker=actor.id,sourceOwner=actor.owner,sourcePlayer=true,level=actor.level,weapon=actor.weapon,
                rangedPose=new RangedShotPose {worldId=session.World.worldId,rootX=4,rootY=5,angleDegrees=15,weaponItem=actor.equippedWeaponItem,
                    selectionRevision=actor.weaponSelectionRevision,actorGeneration=actor.generation,actorLifecycle=actor.lifecycleSequence,hasCasing=actor.weapon=="pistol",casingX=4,casingY=5}};
        }
        private static CasingEffects Effects(GameObject root,CoopSession session)
        {var orders=new WorldVisualOrderTable();orders.Reconcile(session.World,session.LocalActor.level);var effects=root.AddComponent<CasingEffects>();effects.Initialize(session,orders);return effects;}
        private static void Pause(CoopSession session,bool paused,long revision)
        {typeof(SessionPause).GetMethod("Receive",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(session.Pause,new object[]{new PauseStateMessage {revision=revision,levels=paused?new[]{session.LocalActor.level}:Array.Empty<string>()}});}
        private static void Rejected(CoopSession session,string id)
        {typeof(SessionProjectiles).GetMethod("ReceiveRejection",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(session.Projectiles,new object[]{new ProjectileRejectedMessage {id=id,reason="Проверочный отказ гильзы"}});}
        private static void CheckDetached(CasingEffectProfile profile,int fps)
        {
            var root=new GameObject("Проверка отделённой гильзы "+fps);
            try
            {
                var actor=Actor();var session=Session(root,actor);var effects=Effects(root,session);var shot=Shot(session);effects.Observe(shot);
                Require(effects.PreparedCount==64 && effects.ActiveCount==1,"Нет подготовленного пула/эффекта");
                Require(effects.TryGet(shot.id,out var start) && start.Position==new Vector2(4,5),"Нет captured socket");
                var material=start.Renderer.sharedMaterial;var sprite=start.Renderer.sprite;
                Require(material.shader.name=="Remains/WorldLit" && material.mainTexture==profile.Sprite.texture && start.Renderer.sortingOrder>=WorldVisualOrderPolicy.FirstOrder(WorldVisualCategory.Effect),"Материал/общий слой эффекта неверен");
                effects.Observe(StateJson.Copy(shot));Require(effects.CreatedCount==1,"Дубликат создал вторую гильзу");
                Pause(session,true,1);effects.Step(.4f);Require(effects.TryGet(shot.id,out var frozen) && frozen.Age==0 && frozen.Position==start.Position,"Пауза сдвинула отделённую гильзу");Pause(session,false,2);
                actor.Position=new Vector2(40,30);actor.weapon="energy";actor.equippedWeaponItem="other";actor.weaponSelectionRevision++;actor.generation++;actor.lifecycleSequence++;actor.down=true;actor.owner="next-owner";
                int steps=Mathf.FloorToInt(.2f*fps);for(int step=0;step<steps;step++)effects.Step(1f/fps);effects.Step(.2f-steps/(float)fps);
                Require(effects.TryGet(shot.id,out var current) && Mathf.Abs(current.Age-.2f)<.000001f && current.Socket==start.Socket && current.Renderer.sharedMaterial==material && current.Renderer.sprite==sprite,
                    "Гильза переиспользует нового actor/weapon или пересоздаёт ресурсы");
                var velocity=CasingEffects.InitialVelocity(15,false,profile.HorizontalSpeed,profile.VerticalSpeed);
                Require(Vector2.Distance(current.Position,CasingEffects.PositionAt(start.Socket,velocity,profile.Gravity,.2f))<.00001f,"Разделение шага изменило траекторию");
                var replacement=StateJson.Copy(session.World);typeof(CoopSession).GetProperty("World").SetValue(session,replacement);effects.Step(0);
                Require(effects.ActiveCount==1 && effects.TryGet(shot.id,out _),"Same-world snapshot стёр отделённую гильзу");
                Rejected(session,shot.id);Require(effects.ActiveCount==0,"Отказ не снял совпадающую гильзу");effects.Observe(shot);Require(effects.ActiveCount==0,"Rejected replay воскресил гильзу");
                replacement.Find(actor.id).down=false;replacement.Find(actor.id).weapon="energy";effects.Observe(Shot(session,"energy"));Require(effects.ActiveCount==0,"Energy создала гильзу");
                replacement.Find(actor.id).weapon="pistol";var next=Shot(session,"next");effects.Observe(next);effects.Step(profile.Lifetime+.001f);effects.Observe(next);
                Require(effects.ActiveCount==0 && effects.CreatedCount==2,"Истечение/дубликат повторно создали гильзу");
                var late=Shot(session,"world-stale");session.World.worldId="next-world";effects.Step(0);effects.Observe(late);Require(effects.ActiveCount==0,"Поздний другой мир получил гильзу");
                // Restore alone is never an emission source. No AttackReceived is replayed.
                session.World.projectiles.Add(new ProjectileState {launch=late});effects.Step(0);Require(effects.ActiveCount==0,"Восстановленный flight переиграл гильзу");
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
        }
        private static void CheckPool()
        {
            var root=new GameObject("Проверка ограниченного пула гильз");
            try
            {
                var actor=Actor();var session=Session(root,actor);var effects=Effects(root,session);
                for(int i=0;i<CasingEffects.Capacity;i++)effects.Observe(Shot(session,"pooled:"+i));
                Require(effects.ActiveCount==64 && root.GetComponentsInChildren<SpriteRenderer>(true).Length==64,"Пул вырос/потерял изображения");
                effects.Observe(Shot(session,"overflow"));Require(effects.OverflowCount==1 && effects.ActiveCount==64 && effects.PreparedCount==64,"Переполнение молча расширило пул");
                Rejected(session,"pooled:0");Require(effects.ActiveCount==63 && effects.TryGet("pooled:1",out _),"Отказ удалил другой эффект");
                effects.Observe(Shot(session,"replacement"));Require(effects.ActiveCount==64 && effects.PreparedCount==64,"Освобождённый слот не использован");
                actor.level="factory:0";effects.Step(0);Require(effects.ActiveCount==0,"Выход с уровня оставил старые изображения");
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
        }
        private static void CheckContexts()
        {
            CheckShotContext((a,s)=>a.equippedWeaponItem="pistol-instance-B","item");
            CheckShotContext((a,s)=>a.weaponSelectionRevision++,"selection");
            CheckShotContext((a,s)=>a.generation++,"generation");
            CheckShotContext((a,s)=>a.lifecycleSequence++,"lifecycle");
            CheckShotContext((a,s)=>a.owner="new-owner","owner");
            CheckShotContext((a,s)=>a.down=true,"down");
            CheckShotContext((a,s)=>a.level="factory:0","level");
            CheckShotContext((a,s)=>s.World.worldId="new-world","world");
            CheckShotContext((a,s)=>a.weapon="energy","weapon");
        }
        private static void CheckShotContext(Action<ActorState,CoopSession> mutate,string reason)
        {
            var root=new GameObject("Captured shot guard · "+reason);
            try
            {
                var actor=Actor();var session=Session(root,actor);var animation=root.AddComponent<WeaponShotAnimation>();animation.Initialize(session);
                var rest=Resources.Load<WeaponAsset>("Content/weapons/pistol").Presentation.Artwork.sprite;var shot=Shot(session);animation.Observe(shot);
                Require(animation.Select(actor,rest)!=rest,"Captured выстрел не показал кадр: "+reason);
                Pause(session,true,1);animation.Step(.4f);Require(animation.Select(actor,rest)!=rest,"Pause продвинула captured clip");Pause(session,false,2);
                mutate(actor,session);Require(animation.Select(actor,rest)==rest,"Старый кадр при новом "+reason);
                shot.id="late:"+reason;animation.Observe(shot);Require(animation.Select(actor,rest)==rest,"Поздний выстрел прикреплён к новому "+reason);
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
        }
        private static void Reject(Action action,string reason)
        {bool rejected=false;try{action();}catch(InvalidOperationException){rejected=true;}Require(rejected,reason);}
        private static void Require(bool condition,string reason)
        {if(!condition)throw new InvalidOperationException("Гильзы/captured shot: "+reason);}
    }
}
