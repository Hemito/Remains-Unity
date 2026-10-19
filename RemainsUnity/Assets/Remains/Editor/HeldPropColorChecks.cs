using System;
using System.Reflection;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Networking;
using Remains.Presentation;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class HeldPropColorChecks
    {
        public static void Run()
        {
            PonyVisual.Prepare();PropCatalog.Load();RunSource();CheckHeldActor();
            var artwork=new PropArtwork();var world=World();var prop=new ContainerState
            {id="color-container",definition="trash",owner="a",level="hub",generation=3,held=true,width=.5f,height=.8f,
                appearance=new ContainerAppearanceState {version=1}};
            world.containers.Add(prop);var colors=new HeldPropColorSource();colors.Rebuild(world);
            var view=artwork.CreateContainerView(prop);
            var orders=new WorldVisualOrderTable();orders.Reconcile(world,"hub");view.BindOrder(orders,WorldVisualKind.Container,prop.id);
            try
            {
                string before=StateJson.ToJson(world);view.ApplyHeldAppearance(prop,colors);
                Require(view.HeldGlowVisible && view.HeldGlowHolder=="holder-a" && view.HeldGlowMagic==0x286CFF,"Первый подтверждённый держатель не дал свой цвет.");
                var halo=view.HeldGlowRenderer;int renderer=halo.GetInstanceID(),material=halo.sharedMaterial.GetInstanceID(),sprite=halo.sprite.GetInstanceID();
                Require(view.HeldGlowPreparedFrameCount==2,"Настоящий trash должен заранее иметь оба кадра свечения.");
                for(int i=0;i<1000;i++) view.DrawMotion(prop,"a",false,0);
                Require(halo.GetInstanceID()==renderer && halo.sharedMaterial.GetInstanceID()==material && halo.sprite.GetInstanceID()==sprite &&
                    StateJson.ToJson(world)==before,"Рисование пересоздало renderer/material/sprite или изменило игровой мир.");
                AssertColor(halo,0x286CFF,.7f);
                prop.held=false;view.DrawMotion(prop,"a",true,0);Require(!view.HeldGlowVisible,"Отпускание на паузе оставило свечение.");
                prop.owner="b";prop.generation++;prop.held=true;
                colors.Rebuild(world);view.ApplyHeldAppearance(prop,colors);AssertColor(halo,0xE040D0,.7f);
                Require(halo.GetInstanceID()==renderer && halo.sharedMaterial.GetInstanceID()==material && halo.sprite.GetInstanceID()==sprite &&
                    view.HeldGlowHolder=="holder-b","Смена держателя пересоздала кадр или оставила старого держателя.");
                prop.appearance.opened=true;prop.appearance.revision=1;view.ApplyContainerAppearance(prop);
                int openedMaterial=halo.sharedMaterial.GetInstanceID(),openedSprite=halo.sprite.GetInstanceID();
                Require(openedMaterial!=material && openedSprite!=sprite,"Открытый кадр не использует свою заранее подготовленную пару.");
                AssertColor(halo,0xE040D0,.7f);
                prop.appearance.opened=false;prop.appearance.revision=0;view.ApplyContainerAppearance(prop);
                Require(halo.sharedMaterial.GetInstanceID()==material && halo.sprite.GetInstanceID()==sprite,"Возврат кадра пересоздал ресурсы.");
                AssertColor(halo,0xE040D0,.7f);
                prop.appearance.opened=true;prop.appearance.revision=1;view.ApplyContainerAppearance(prop);
                Require(halo.sharedMaterial.GetInstanceID()==openedMaterial && halo.sprite.GetInstanceID()==openedSprite,"Повтор выбора открытого кадра пересоздал ресурсы.");
                var holder=world.Find("holder-b");holder.appearance.magic=0xFFFF20;colors.Rebuild(world);view.ApplyHeldAppearance(prop,colors);
                AssertColor(halo,0xFFFF20,.7f);Require(halo.sharedMaterial.GetInstanceID()==openedMaterial,"Изменение цвета пересоздало материал.");
                // A stale color cannot prove the next ownership epoch, even if its RGB happens to match.
                Require(colors.TryResolve(prop,out var prepared,out _) && prepared.Matches(world.worldId,prop),"Не подготовлен текущий контекст.");
                prop.generation++;Require(!prepared.Matches(world.worldId,prop),"Устаревший цвет принят новым поколением.");
                int warnings=0;
                void OnLog(string message,string stack,LogType type)
                {if(type==LogType.Warning && message.StartsWith("Свечение предмета скрыто:",StringComparison.Ordinal)) warnings++;}
                Application.logMessageReceived+=OnLog;
                try
                {
                    holder.connected=false;colors.Rebuild(world);view.ApplyHeldAppearance(prop,colors);
                    for(int i=0;i<1000;i++) {view.ApplyHeldAppearance(prop,colors);view.DrawMotion(prop,"a",false,0);}
                    Require(!view.HeldGlowVisible && warnings==1,"Отсутствующий держатель дал свечение или повторяющиеся предупреждения.");
                    holder.connected=true;colors.Rebuild(world);view.ApplyHeldAppearance(prop,colors);Require(view.HeldGlowVisible,"Восстановление держателя не вернуло цвет.");
                    holder.down=true;colors.Rebuild(world);view.ApplyHeldAppearance(prop,colors);
                    Require(!view.HeldGlowVisible && warnings==2,"Новый missing episode не сообщил contextual предупреждение.");
                }
                finally {Application.logMessageReceived-=OnLog;}
            }
            finally {UnityEngine.Object.DestroyImmediate(view.gameObject);}
            CheckRefreshOrdering();
            Debug.Log("Цвет удержания: PASS — два держателя, remote owner, отпускание, оба actual кадра, property block, неизменные материалы/геометрия.");
        }
        private static void CheckRefreshOrdering()
        {
            // Exercise the actual Changed coordinator, rather than manually
            // assigning a palette to a view after its first draw has happened.
            WeaponCatalog.Load();ActorTypes.Load();
            var root=new GameObject("Проверка порядка подтверждённого цвета предметов");
            int warnings=0;
            void OnLog(string message,string stack,LogType type)
            {if(type==LogType.Warning && message.StartsWith("Свечение предмета скрыто:",StringComparison.Ordinal)) warnings++;}
            Application.logMessageReceived+=OnLog;
            try
            {
                var session=root.AddComponent<CoopSession>();session.ConfigureRules(WeaponCatalog.Rules);
                var world=World();world.containers.Add(new ContainerState {id="ordered-container",definition="trash",owner="a",level="hub",generation=2,
                    width=.5f,height=.8f,mass=4,appearance=new ContainerAppearanceState {version=1}});
                world.props.Add(new PropState {id="ordered-prop",definition="woodbox",owner="a",level="hub",generation=2,width=1.2f,height=1.2f,mass=15});
                typeof(CoopSession).GetProperty("World").SetValue(session,world);
                typeof(CoopSession).GetProperty("LocalActorId").SetValue(session,"holder-a");
                typeof(CoopSession).GetProperty("LocalOwner").SetValue(session,"a");
                var layers=new ActorVisualLayers();layers.Orders.Reconcile(world,"hub");
                session.Changed+=()=>layers.Orders.Reconcile(session.World,session.LocalActor?.level);
                using var geometry=new SimulationGeometry(_=>new RoomLayout {id="color-room",left=-10,right=10,bottom=-10,top=10,platforms=Array.Empty<RoomSurface>(),ladders=Array.Empty<RoomLadder>()});
                var adapter=new InteractionAdapter(session);var combat=new CombatController(session,geometry);
                var grenades=new GrenadeController(session,adapter,combat,geometry);
                var visuals=root.AddComponent<InteractionVisuals>();visuals.Initialize(session,adapter,grenades,geometry,layers,combat.Reload);
                root.AddComponent<HeldPropGlowPresentation>().Initialize(session,visuals);
                var containerView=visuals.FindContainer("ordered-container");var propView=visuals.FindProp("ordered-prop");
                Require(containerView!=null && propView!=null && !containerView.HeldGlowVisible && !propView.HeldGlowVisible,"Не подготовлен исходный свободный вид.");
                int containerMaterial=containerView.HeldGlowRenderer.sharedMaterial.GetInstanceID(),propMaterial=propView.HeldGlowRenderer.sharedMaterial.GetInstanceID();
                void Changed(WorldState replacement)
                {
                    typeof(CoopSession).GetProperty("World").SetValue(session,replacement);
                    // Debug-only invocation of the exact installed Changed delegate;
                    // no network, production command, palette or draw is substituted.
                    var listeners=(Action)typeof(CoopSession).GetField("Changed",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(session);
                    listeners.Invoke();
                }
                void Expect(string holder,int magic)
                {
                    foreach(var actual in new[]{containerView,propView})
                    {
                        actual.DrawMotion(actual==containerView?session.World.containers[0]:session.World.props[0],"a",false,0);
                        Require(actual.HeldGlowVisible && actual.HeldGlowHolder==holder && actual.HeldGlowMagic==magic,"Первый Draw нового snapshot получил старую palette/epoch.");
                        AssertColor(actual.HeldGlowRenderer,magic,.7f);
                    }
                    Require(warnings==0,"Правильные holder данные дали временное missing предупреждение до первого Draw.");
                    Require(containerView.HeldGlowRenderer.sharedMaterial.GetInstanceID()==containerMaterial && propView.HeldGlowRenderer.sharedMaterial.GetInstanceID()==propMaterial,"Event-перепривязка пересоздала материалы.");
                }
                world=StateJson.Copy(world);world.containers[0].held=world.props[0].held=true;
                world.containers[0].generation=world.props[0].generation=3;Changed(world);Expect("holder-a",0x286CFF);
                world=StateJson.Copy(world);world.containers[0].owner=world.props[0].owner="b";
                world.containers[0].generation=world.props[0].generation=5;Changed(world);Expect("holder-b",0xE040D0);
                world=StateJson.Copy(world);world.containers[0].held=world.props[0].held=false;Changed(world);
                Require(!containerView.HeldGlowVisible && !propView.HeldGlowVisible && warnings==0,"Release event оставил glow или дал ложное предупреждение.");
                world=StateJson.Copy(world);world.Find("holder-b").connected=false;world.containers[0].held=world.props[0].held=true;
                world.containers[0].generation=world.props[0].generation=7;Changed(world);
                Require(!containerView.HeldGlowVisible && !propView.HeldGlowVisible && warnings==2,"Настоящий missing-holder не дал ровно по одному contextual предупреждению.");
                Changed(StateJson.Copy(world));
                for(int i=0;i<1000;i++) {containerView.DrawMotion(world.containers[0],"a",false,0);propView.DrawMotion(world.props[0],"a",false,0);}
                Require(warnings==2,"Missing-holder event/per-frame Draw повторяет предупреждения.");
                world=StateJson.Copy(world);world.worldId="held-color-next-world";world.Find("holder-b").connected=true;
                warnings=0;Changed(world);Expect("holder-b",0xE040D0);
            }
            finally {Application.logMessageReceived-=OnLog;UnityEngine.Object.DestroyImmediate(root);}
        }
        // Also callable by a managed harness after preparing the existing pony catalogue.
        public static void RunSource()
        {
            var world=World();var prop=new PropState {id="prop",owner="a",level="hub",held=true,generation=2};
            var colors=new HeldPropColorSource();colors.Rebuild(world);
            Require(colors.TryResolve(prop,out var first,out _) && first.HolderId=="holder-a" && first.Magic==0x286CFF,"Не разрешился цвет первого игрока.");
            Require(!first.Matches("another-world",prop),"Контекст другого мира принят.");
            prop.owner="b";Require(!first.Matches(world.worldId,prop),"Цвет прежнего владельца принят новым.");
            Require(colors.TryResolve(prop,out var second,out _) && second.Magic==0xE040D0,"Remote owner не разрешился.");
            prop.held=false;Require(!colors.TryResolve(prop,out _,out var freeReason) && freeReason==null,"Свободный owner ошибочно объявлен держателем.");prop.held=true;
            var holder=world.Find("holder-b");
            holder.connected=false;Reject(colors,world,prop,"отключённый держатель");holder.connected=true;
            holder.down=true;Reject(colors,world,prop,"нокдаун");holder.down=false;
            holder.health=0;Reject(colors,world,prop,"нулевое здоровье");holder.health=100;
            holder.level="factory:0";Reject(colors,world,prop,"другой уровень");holder.level="hub";
            holder.appearance.magic=-1;Reject(colors,world,prop,"некорректный цвет");holder.appearance.magic=0xE040D0;
            world.actors.Add(Player("duplicate","b",0xE040D0));Reject(colors,world,prop,"неоднозначный owner");world.actors.RemoveAt(world.actors.Count-1);
            world.actors.Add(new ActorState {id="npc",owner="b",level="hub",health=100});colors.Rebuild(world);
            Require(colors.TryResolve(prop,out var same,out _) && same.HolderId==holder.id,"NPC-владелец симуляции заменил игрока-держателя.");
            string before=StateJson.ToJson(world);colors.Rebuild(world);colors.TryResolve(prop,out _,out _);
            Require(StateJson.ToJson(world)==before,"Источники цвета изменили состояние актёров/тел.");
            colors.Rebuild(null);Require(!colors.TryResolve(prop,out _,out var missing) && missing!=null,"После Clear остался цвет старого мира.");
        }
        private static void Reject(HeldPropColorSource colors,WorldState world,PropState prop,string condition)
        {colors.Rebuild(world);Require(!colors.TryResolve(prop,out _,out var reason) && !string.IsNullOrWhiteSpace(reason),"Нет явного отказа: "+condition);}
        private static void CheckHeldActor()
        {
            var world=World();var colors=new HeldPropColorSource();colors.Rebuild(world);
            var creature=System.Linq.Enumerable.First(CreatureCatalog.Load().Creatures);
            var body=creature.spawn.Create(new TrainingPlacement {id="held-creature-color",type=creature.actorType,facing=1},"hub");
            body.hold=new ActorHoldState {holder="holder-a",token="held-color-check"};
            world.actors.Add(body);
            var visual=(AtlasActorVisual)ActorVisualFactory.Create(body);
            try
            {
                visual.Bind(body);visual.ApplyHeldAppearance(colors);
                Require(visual.HeldGlowVisible,"Удерживаемое существо не получило свечение.");
                var glow=visual.GetComponentInChildren<HeldPropGlow>();
                int prepared=glow.PreparedFrameCount;
                AssertColor(glow.Renderer,0x286CFF,.7f);
                for(int i=0;i<100;i++) {body.poseTime+=1f/60;visual.Draw();}
                Require(glow.PreparedFrameCount==prepared && glow.Visible,"Анимация существа пересоздаёт кадры свечения или теряет свечение.");
                body.hold.holder="holder-b";visual.ApplyHeldAppearance(colors);AssertColor(glow.Renderer,0xE040D0,.7f);
                var replacement=StateJson.Copy(world);replacement.Find(body.id).hold=null;colors.Rebuild(replacement);
                visual.Bind(replacement.Find(body.id));visual.ApplyHeldAppearance(colors);visual.Draw();
                Require(!visual.HeldGlowVisible,"Замена мира сохранила свечение отпущенного тела.");
                replacement.Find(body.id).hold=new ActorHoldState {holder="holder-a",token="replacement-hold"};colors.Rebuild(replacement);
                visual.Bind(replacement.Find(body.id));visual.ApplyHeldAppearance(colors);visual.Draw();
                Require(visual.HeldGlowVisible,"Замена мира не включила свечение сразу после Bind.");AssertColor(glow.Renderer,0x286CFF,.7f);
                colors.Rebuild(world);visual.Bind(body);visual.ApplyHeldAppearance(colors);
                Require(glow.PreparedFrameCount==prepared,"Смена держателя пересоздаёт кадры существа.");
                body.hold=null;visual.ApplyHeldAppearance(colors);visual.Draw();
                Require(!visual.HeldGlowVisible,"Отпущенное существо сохранило свечение.");
                body.hold=new ActorHoldState {holder="holder-a",token="held-color-check-2"};
                world.Find("holder-a").level="other";colors.Rebuild(world);
                Require(!colors.TryResolve(body,out _,out var reason) && !string.IsNullOrWhiteSpace(reason),"Нет отказа для держателя тела в другом уровне.");
            }
            finally {UnityEngine.Object.DestroyImmediate(visual.gameObject);}
        }
        private static ActorState Player(string id,string owner,int magic)=>new()
        {id=id,owner=owner,level="hub",player=true,connected=true,health=100,appearance=new CharacterAppearance {version=1,magic=magic}};
        private static WorldState World()
        {var world=new WorldState {worldId="held-color-world"};world.actors.Add(Player("holder-a","a",0x286CFF));world.actors.Add(Player("holder-b","b",0xE040D0));return world;}
        private static void AssertColor(SpriteRenderer renderer,int rgb,float intensity)
        {
            var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block);var actual=block.GetColor("_Color");var expected=PonyAppearance.FromRgb(rgb);expected.a=intensity;
            Require(block.HasColor("_Color") && (new Vector4(actual.r,actual.g,actual.b,actual.a)-new Vector4(expected.r,expected.g,expected.b,expected.a)).sqrMagnitude<.00000001f,
                "Actual renderer property block не соответствует цвету/интенсивности держателя.");
        }
        private static void Require(bool condition,string reason) {if(!condition) throw new InvalidOperationException("Цвет удержания: "+reason);}
    }
}
