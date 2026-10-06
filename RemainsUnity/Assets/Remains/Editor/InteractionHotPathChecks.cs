using System;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Editor
{
    public static class InteractionHotPathChecks
    {
        private sealed class MutableAccess : ILootRule
        {
            public bool Allow;
            public int Calls;
            public string AccessRejection(LootAccessContext context) {Calls++;return Allow && !context.body?null:"Проверка: доступ закрыт.";}
            public string ClaimRejection(LootClaimContext context)=>null;
        }
        private sealed class NoDeposit : IDepositRule
        {
            public string AccessRejection(LootAccessContext context)=>"Проверка: внесение закрыто.";
            public string Rejection(DepositContext context)=>"Проверка: внесение закрыто.";
        }
        public static void Run()
        {
            ActorTypes.Load();RunDomain(GameplayRuleSet.CreateDefault());RunHint();
            Debug.Log("Горячие пути взаимодействия: PASS — live wrappers без повторных копий, смена snapshot/world, движение/правила без stale cache, стабильные ID, coalesced hint и неизменный текст.");
        }
        // Native catalog or an explicitly registered managed fixture must be prepared.
        public static void RunDomain(GameplayRuleSet rules)
        {
            var box=new ContainerState {id="hot-box",x=.5f};var item=new ItemStack {id="hot-item",definition="caps",count=2};box.items.Add(item);
            InventorySource first=box;Require(ReferenceEquals(first,(InventorySource)box),"Повторный view контейнера создаёт новую обёртку");
            box.x=1;item.count=3;Require(first.Position.x==1 && first.items[0].count==3 && ReferenceEquals(first.items,box.items),"View кеширует данные вместо ссылки на реальный источник");
            var actor=new ActorState {id="hot-player",player=true,connected=true};var world=new WorldState();world.actors.Add(actor);world.containers.Add(box);
            Require(ReferenceEquals(first,InventorySource.Resolve(world,box.id)),"Resolve не использует view того же объекта");
            var replacement=new ContainerState {id=box.id,x=1.5f};replacement.items.Add(new ItemStack {id="replacement",definition="pin",count=1});
            var otherWorld=new WorldState();otherWorld.containers.Add(replacement);
            var replaced=InventorySource.Resolve(otherWorld,box.id);
            Require(!ReferenceEquals(first,replaced) && ReferenceEquals(replaced.items,replacement.items) && replaced.items[0].id=="replacement", "Совпадающий ID другого мира вернул прежний inventory");
            world.containers[0]=replacement;Require(ReferenceEquals(InventorySource.Resolve(world,box.id),replaced),"Новый snapshot сохраняет прежний source object");
            world.containers.Clear();Require(InventorySource.Resolve(world,box.id)==null,"Удалённый источник остался доступен");world.containers.Add(box);
            var body=new ActorState {id="hot-body",connected=false,down=true};
            var bodyView=InventorySource.FromBody(body);Require(bodyView!=null && ReferenceEquals(bodyView,InventorySource.FromBody(body)),"Body view пересоздаётся");
            body.inventory.Add(new ItemStack {id="body-item",definition="heal",count=1});body.x=5;
            Require(ReferenceEquals(bodyView.items,body.inventory) && bodyView.Position.x==5,"Body view потерял real inventory/pose");
            body.id="changed-body-id";Require(bodyView.id==InventorySource.BodyId(body.id),"Body ID кеш устарел после явной смены ID");
            int count=0;foreach(var source in InventorySource.All(world)) {Require(source!=null,"Enumeration выдала отсутствующий source");count++;}
            Require(count==2,"Enumeration потеряла источник/добавила дубликат");
            var access=new MutableAccess {Allow=true};var changing=new GameplayRuleSet(rules.Damage,rules.Knockdown,rules.PartyDefeat,access,rules.Pause,new NoDeposit(),rules.Telekinesis);
            var focus=new InteractionFocus();var cursor=first.Bounds.center;
            focus.Step(world,actor,changing,cursor,false,null);Require(focus.Target.id==box.id,"Начальный курсор не выбрал box");
            int calls=access.Calls;access.Allow=false;focus.Step(world,actor,changing,cursor,false,null);
            Require(focus.Target.kind==InteractionKind.None && access.Calls>calls,"Неизменная revision скрыла смену правила доступа");
            access.Allow=true;box.x=6;
            focus.Step(world,actor,rules,cursor,false,null);Require(focus.Target.kind==InteractionKind.None,"Неизменная revision скрыла движение источника вне доступа");
            box.x=.5f;focus.Step(world,actor,rules,cursor,false,null);Require(focus.Target.id==box.id,"Возвращение источника не обновило цель");
            actor.down=true;focus.Step(world,actor,rules,cursor,false,null);Require(focus.Target.kind==InteractionKind.None,"Down actor сохранил доступную цель");actor.down=false;
            calls=access.Calls;focus.Step(world,actor,changing,cursor,true,null);Require(access.Calls==calls && focus.Target.kind==InteractionKind.None,"Blocked UI обходит источники");
            var malformed=new ContainerState {id=InventorySource.BodyId(actor.id)};world.containers.Add(malformed);
            bool rejected=false;try {InteractionSelection.Select(world,actor,rules,cursor);} catch(InvalidOperationException) {rejected=true;}
            Require(rejected,"Префикс body у обычного контейнера молча принят");
        }
        private static void RunHint()
        {
            var root=new GameObject("Проверка hot hint",typeof(RectTransform),typeof(Canvas));InteractionHint hint=null;GameObject checkCamera=null;
            try
            {
                if(Camera.main==null)
                {
                    checkCamera=new GameObject("Камера проверки рамки",typeof(Camera));checkCamera.tag="MainCamera";
                    checkCamera.transform.position=new Vector3(0,0,-10);checkCamera.GetComponent<Camera>().orthographic=true;
                }
                root.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
                var ui=new HudElements(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));hint=new InteractionHint(root.transform,ui,()=>new Rect(-1,0,2,2));
                var box=new ContainerState {id="hint-box",mass=25};InventorySource source=box;
                hint.RefreshSource(source,true,null);int texts=hint.TextRevisionCount,places=hint.PlacementEvaluationCount;
                for(int i=0;i<1000;i++) {box.x=i*.001f;hint.RefreshSource(source,true,null);hint.Highlight(new InteractionTarget(InteractionKind.Container,box.id,source.Bounds),true);}
                Require(hint.TextRevisionCount==texts && hint.PlacementEvaluationCount==places,"Неизменный текст или Refresh/Highlight повторяют Canvas placement");
                box.mass=12;hint.RefreshSource(source,true,null);Require(hint.TextRevisionCount==texts+1,"Изменение массы потеряло обновление hint");
                box.presentationItem="caps";hint.RefreshSource(source,true,null);texts=hint.TextRevisionCount;
                hint.RefreshSource(source,true,null,true);Require(hint.TextRevisionCount==texts+1,"Pickup pending не обновил текст");
                Canvas.ForceUpdateCanvases();Require(hint.PlacementEvaluationCount>places,"Финальный Canvas pass не выполняет placement");
                foreach(float scale in new[]{1f,1.5f,2f,3f})
                {
                    root.GetComponent<Canvas>().scaleFactor=scale;Canvas.ForceUpdateCanvases();
                    int visibleEdges=0;
                    foreach(Transform child in root.transform)
                    {
                        if(child.name!="Выделение цели взаимодействия" || !child.gameObject.activeSelf) continue;
                        var edge=(RectTransform)child;
                        var corners=new Vector3[4];edge.GetWorldCorners(corners);
                        float pixels=Mathf.Min(Vector3.Distance(corners[0],corners[1]),Vector3.Distance(corners[1],corners[2]));
                        Require(Mathf.Abs(pixels-3)<.02f,$"Рамка масштабируется вместе с HUD: scale={scale}, pixels={pixels}");
                        visibleEdges++;
                    }
                    Require(visibleEdges==4,$"Рамка не имеет четырёх видимых сторон: scale={scale}, count={visibleEdges}");
                }
            }
            finally {hint?.Dispose();UnityEngine.Object.DestroyImmediate(root);if(checkCamera!=null) UnityEngine.Object.DestroyImmediate(checkCamera);}
        }
        private static void Require(bool value,string reason) {if(!value) throw new InvalidOperationException("Горячие пути взаимодействия: "+reason);}
    }
}
