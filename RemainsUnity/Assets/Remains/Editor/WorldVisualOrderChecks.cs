using System;
using Remains.Domain;
using Remains.Presentation;
using Remains.Serialization;
using UnityEngine;
using UnityEngine.Rendering;

namespace Remains.Editor
{
    public static class WorldVisualOrderChecks
    {
        public static void Run()
        {
            RunPure();CheckGroups();
            Debug.Log("Слои мира: PASS — категории, независимость от позиции/владельца/порядка снимка, падение/смерть/телекинез, цельный актёр с оружием, смена уровня/мира и отложенное удаление старого эффекта.");
        }
        public static void RunPure()
        {
            CheckPriorityAndStability();CheckStateTransitions();CheckTransientLifecycle();
        }
        private static void CheckPriorityAndStability()
        {
            var world=new WorldState();
            world.actors.Add(new ActorState {id="z-target",actorType="training-dummy",level="hub"});
            world.actors.Add(new ActorState {id="z-body",down=true,level="hub"});
            world.actors.Add(new ActorState {id="a-npc",level="hub"});
            world.actors.Add(new ActorState {id="0-player",player=true,connected=true,level="hub"});
            world.props.Add(new PropState {id="z-prop",level="hub"});
            world.props.Add(new PropState {id="000-held",level="hub",held=true});
            world.containers.Add(new ContainerState {id="a-container",level="hub"});
            world.doors.Add(new DoorState {id="b-door",level="hub"});
            world.actors.Add(new ActorState {id="other-level",level="factory:0"});
            var table=new WorldVisualOrderTable();table.Reconcile(world,"hub");
            string effectWorld=table.RegisterTransient(WorldVisualKind.Effect,"000-effect","hub",WorldVisualCategory.Effect);
            int[] ordered={WorldVisualOrderPolicy.SceneryRoomOrder,table.GetOrder(WorldVisualKind.Actor,"z-target"),
                table.GetOrder(WorldVisualKind.Container,"a-container"),table.GetOrder(WorldVisualKind.Door,"b-door"),
                table.GetOrder(WorldVisualKind.Prop,"z-prop"),table.GetOrder(WorldVisualKind.Actor,"z-body"),
                table.GetOrder(WorldVisualKind.Actor,"a-npc"),table.GetOrder(WorldVisualKind.Actor,"0-player"),
                table.GetOrder(WorldVisualKind.Prop,"000-held"),table.GetOrder(WorldVisualKind.Effect,"000-effect")};
            for(int i=1;i<ordered.Length;i++) Require(ordered[i]>ordered[i-1],"ID или позиция отменили категорию / общий tie-breaker предметов и дверей");
            Require(!table.TryGet(WorldVisualKind.Actor,"other-level",out _,out _),"Другой уровень попал в видимую таблицу");
            int rebuilds=table.Rebuilds;string before=StateJson.ToJson(world);
            world.actors.Reverse();world.props.Reverse();world.containers.Reverse();world.doors.Reverse();
            table.Reconcile(world.Copy(),"hub");Require(table.Rebuilds==rebuilds,"Перестановка снимка пересобрала одинаковые категории");
            Require(table.GetOrder(WorldVisualKind.Prop,"z-prop")==ordered[4],"Снимок изменил tie-breaker");
            // Render policy never writes category/order into persistent gameplay state.
            world.actors.Reverse();world.props.Reverse();world.containers.Reverse();world.doors.Reverse();
            Require(StateJson.ToJson(world)==before,"Таблица изменила сериализованное состояние");
            for(int i=0;i<400;i++)
            {
                var actor=world.actors[2];actor.x+=.01f;actor.facing=-actor.facing;actor.weaponAngle++;
                actor.owner="handoff-owner";actor.generation++;table.Reconcile(world,"hub");
            }
            Require(table.Rebuilds==rebuilds,"Движение/наведение/смена владельца требуют пересортировки");
            table.ReleaseTransient(WorldVisualKind.Effect,"000-effect",effectWorld);
        }
        private static void CheckStateTransitions()
        {
            var world=new WorldState();var actor=new ActorState {id="actor",player=true,connected=true,level="hub"};
            var npc=new ActorState {id="npc",level="hub"};var prop=new PropState {id="cargo",level="hub"};
            world.actors.Add(actor);world.actors.Add(npc);world.props.Add(prop);
            var table=new WorldVisualOrderTable();table.Reconcile(world,"hub");
            Require(table.GetOrder(WorldVisualKind.Actor,actor.id)>table.GetOrder(WorldVisualKind.Actor,npc.id),"Игрок ниже живого NPC");
            actor.down=true;table.Reconcile(world,"hub");
            Require(table.GetOrder(WorldVisualKind.Actor,actor.id)<table.GetOrder(WorldVisualKind.Actor,npc.id),"Нокдаун оставил тело перед живым NPC");
            actor.hold=new ActorHoldState {holder="holder",token="hold",pendingAttack=true};table.Reconcile(world,"hub");
            Require(table.GetCategory(WorldVisualKind.Actor,actor.id)==WorldVisualCategory.Body,"Ещё ожидающий окончания атаки хват поднял тело в слой TK");
            actor.hold.pendingAttack=false;prop.held=true;table.Reconcile(world,"hub");
            Require(table.GetCategory(WorldVisualKind.Actor,actor.id)==WorldVisualCategory.Telekinesis &&
                table.GetCategory(WorldVisualKind.Prop,prop.id)==WorldVisualCategory.Telekinesis,"Активный хват не поднял тело/груз");
            actor.hold=null;prop.held=false;table.Reconcile(world,"hub");
            Require(table.GetCategory(WorldVisualKind.Actor,actor.id)==WorldVisualCategory.Body &&
                table.GetCategory(WorldVisualKind.Prop,prop.id)==WorldVisualCategory.OrdinaryObject,"Отпускание оставило передний слой");
            actor.down=false;actor.health=100;table.Reconcile(world,"hub");
            Require(table.GetCategory(WorldVisualKind.Actor,actor.id)==WorldVisualCategory.Player,"Подъём не вернул слой игрока");
            npc.health=0;table.Reconcile(world,"hub");
            Require(table.GetCategory(WorldVisualKind.Actor,npc.id)==WorldVisualCategory.Body,"Нулевое HP без down оставило живой слой");
            actor.level="factory:0";actor.connected=false;table.Reconcile(world,"factory:0");
            Require(table.GetCategory(WorldVisualKind.Actor,actor.id)==WorldVisualCategory.Body,"Отключённое тело с HP100 осталось в слое живого игрока");
            actor.level="hub";table.Reconcile(world,"hub");
            Require(!table.TryGet(WorldVisualKind.Actor,actor.id,out _,out _),"Отключённый персонаж хаба видим");
        }
        private static void CheckTransientLifecycle()
        {
            var world=new WorldState();var table=new WorldVisualOrderTable();table.Reconcile(world,"hub");
            string first=table.RegisterTransient(WorldVisualKind.Grenade,"flight","hub",WorldVisualCategory.OrdinaryObject);
            int order=table.GetOrder(WorldVisualKind.Grenade,"flight");
            string second=table.RegisterTransient(WorldVisualKind.Grenade,"flight","hub",WorldVisualCategory.OrdinaryObject);
            table.ReleaseTransient(WorldVisualKind.Grenade,"flight",first);
            Require(table.GetOrder(WorldVisualKind.Grenade,"flight")==order,"Отложенный Destroy старого view удалил новый полёт");
            world.grenades.Add(new GrenadeState {id="flight",level="hub"});table.Reconcile(world,"hub");
            table.ReleaseTransient(WorldVisualKind.Grenade,"flight",second);
            Require(table.GetOrder(WorldVisualKind.Grenade,"flight")==order,"Удаление view изменило авторитетную гранату");
            world.grenades.Clear();table.Reconcile(world,"hub");
            Require(!table.TryGet(WorldVisualKind.Grenade,"flight",out _,out _),"Законченный полёт сохранил регистрацию");
            string oldWorld=table.RegisterTransient(WorldVisualKind.Effect,"same-id","hub",WorldVisualCategory.Effect);
            var replacement=new WorldState();table.Reconcile(replacement,"hub");
            Require(!table.TryGet(WorldVisualKind.Effect,"same-id",out _,out _),"Эффект другого мира попал в новый мир");
            string newWorld=table.RegisterTransient(WorldVisualKind.Effect,"same-id","hub",WorldVisualCategory.Effect);
            table.ReleaseTransient(WorldVisualKind.Effect,"same-id",oldWorld);
            Require(table.TryGet(WorldVisualKind.Effect,"same-id",out _,out _),"Старый мир удалил новое представление с тем же ID");
            table.ReleaseTransient(WorldVisualKind.Effect,"same-id",newWorld);
        }
        private static void CheckGroups()
        {
            var root=new GameObject("Проверка слоёв целых объектов");
            try
            {
                var world=new WorldState();var actor=new ActorState {id="z-player",level="hub",player=true,connected=true};
                world.actors.Add(actor);world.actors.Add(new ActorState {id="a-npc",level="hub"});
                var table=new WorldVisualOrderTable();table.Reconcile(world,"hub");var layers=new ActorVisualLayers(root.transform,table);
                var body=new GameObject("Тело");var inner=body.AddComponent<SortingGroup>();inner.sortingOrder=20;
                var part=new GameObject("Часть тела").AddComponent<SpriteRenderer>();part.transform.SetParent(body.transform);part.sortingOrder=20;
                var weapon=new GameObject("Оружие").AddComponent<SpriteRenderer>();weapon.sortingOrder=100;
                var group=layers.Attach(actor.id,body.transform);layers.Attach(actor.id,weapon.transform);
                Require(ReferenceEquals(RenderSorting.FindOwner(part),group.Sorting) && ReferenceEquals(RenderSorting.FindOwner(weapon),group.Sorting),"Часть или оружие не принадлежат внешней группе актёра");
                Require(group.Depth.Orders==table && group.Depth.Category==WorldVisualCategory.Player,"Группа использует иной источник порядка");
                int partOrder=part.sortingOrder,weaponOrder=weapon.sortingOrder;actor.down=true;table.Reconcile(world,"hub");
                Require(group.Depth.Category==WorldVisualCategory.Body && group.Order<table.GetOrder(WorldVisualKind.Actor,"a-npc"),"Группа не переместилась целиком при нокдауне");
                Require(part.sortingOrder==partOrder && weapon.sortingOrder==weaponOrder,"Мировой слой переписал внутреннюю позу частей");
                body.transform.localScale=new Vector3(-1,1,1);actor.hold=new ActorHoldState {holder="a-npc",token="hold"};table.Reconcile(world,"hub");
                Require(group.Depth.Category==WorldVisualCategory.Telekinesis && group.transform.localScale==Vector3.one && weapon.transform.parent==group.transform,"Поворот тела или TK нарушили независимое оружие/группу");
                actor.owner="new-owner";actor.generation++;int order=group.Order;table.Reconcile(world.Copy(),"hub");
                Require(group.Order==order,"Передача владельца перемешала составного актёра");
                layers.Synchronize(world,"hub");Require(layers.TryGetGroup(actor.id,out var found) && found==group,"Resolver не возвращает целую группу");
                var oldEffect=new GameObject("Старый эффект").AddComponent<SpriteRenderer>();oldEffect.transform.SetParent(root.transform);
                oldEffect.gameObject.AddComponent<WorldVisualOrderBinding>().Initialize(table,WorldVisualKind.Effect,"world-effect",null,oldEffect,"hub");
                table.Reconcile(new WorldState(),"hub");Require(oldEffect.forceRenderingOff,"Эффект прошлого мира остался видим");
                var newEffect=new GameObject("Новый эффект").AddComponent<SpriteRenderer>();newEffect.transform.SetParent(root.transform);
                newEffect.gameObject.AddComponent<WorldVisualOrderBinding>().Initialize(table,WorldVisualKind.Effect,"world-effect",null,newEffect,"hub");
                Require(oldEffect.forceRenderingOff && !newEffect.forceRenderingOff,"Повтор ID в новом мире воскресил старый renderer");
                UnityEngine.Object.DestroyImmediate(oldEffect.gameObject);
                Require(table.TryGet(WorldVisualKind.Effect,"world-effect",out _,out _) && !newEffect.forceRenderingOff,"Отложенное удаление старого мира удалило новый renderer");
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
        }
        private static void Require(bool valid,string reason)
        {if(!valid) throw new InvalidOperationException("Слои мира: "+reason);}
    }
}
