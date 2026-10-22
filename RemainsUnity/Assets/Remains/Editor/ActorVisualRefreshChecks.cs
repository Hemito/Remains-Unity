using System;
using System.Collections.Generic;
using System.Reflection;
using Remains.Domain;
using Remains.Presentation;
using Remains.Runtime;
using UnityEngine;

namespace Remains.Editor
{
    public sealed class CountingActorVisual : ActorVisual
    {
        public ActorState Bound;
        public int Binds,HeldUpdates;
        public HeldPropColorSource Colors;
        public override string ActorId=>Bound?.id;
        public override void Bind(ActorState actor) {Bound=actor;Binds++;}
        public override void ApplyHeldAppearance(HeldPropColorSource colors) {Colors=colors;HeldUpdates++;}
        public override void AppendLightOccluders(List<RoomRadianceField.LightOccluder> output) {}
    }
    public static class ActorVisualRefreshChecks
    {
        public static void Run()
        {
            var root=new GameObject("Неактивная проверка адресных представлений");root.SetActive(false);
            try
            {
                var runtime=root.AddComponent<PrototypeRuntime>();
                Require(runtime.Session==null && !root.activeInHierarchy,"Тест запустил композицию runtime");
                var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                var visuals=(Dictionary<string,ActorVisual>)typeof(PrototypeRuntime).GetField("visuals",flags).GetValue(runtime);
                var refresh=typeof(PrototypeRuntime).GetMethod("RefreshActorVisuals",flags);
                var world=new WorldState();var first=new ActorState {id="first",level="hub"};var second=new ActorState {id="second",level="hub"};
                world.actors.Add(first);world.actors.Add(second);
                CountingActorVisual Add(ActorState actor)
                {
                    var child=new GameObject(actor.id);child.transform.SetParent(root.transform,false);
                    var visual=child.AddComponent<CountingActorVisual>();visuals.Add(actor.id,visual);return visual;
                }
                var one=Add(first);var two=Add(second);var colors=new HeldPropColorSource();
                void Refresh(WorldState state,string level,IReadOnlyCollection<string> parts,bool force=false)
                    =>refresh.Invoke(runtime,new object[] {state,level,parts,force,colors});
                Refresh(world,"hub",null);
                Require(one.Binds==1 && two.Binds==1 && ReferenceEquals(one.Bound,first),"Первичный полный bind неверен");
                var replacement=first.Copy();replacement.x=4;
                var next=new WorldState {worldId=world.worldId,actors=new List<ActorState> {replacement,second}};
                Refresh(next,"hub",new HashSet<string> {"actors:first"});
                Require(one.Binds==2 && ReferenceEquals(one.Bound,replacement) && two.Binds==1,"Адресный rebind потерял новую ссылку или повторил unchanged bind");
                // Holder-only changes still reach an unrelated held target through the common source pass.
                var updatedColors=new HeldPropColorSource();colors=updatedColors;
                Refresh(next,"hub",new HashSet<string> {"actors:first"});
                Require(two.Binds==1 && two.HeldUpdates==3 && ReferenceEquals(two.Colors,updatedColors),"Unchanged target потерял общий held-color pass");
                next.actors.Remove(replacement);Refresh(next,"hub",new HashSet<string> {"actors:first"});
                Require(!visuals.ContainsKey(first.id) && one==null,"Удалённый actor сохранил view");
                second.level="factory:0";Refresh(next,"factory:0",new HashSet<string>());
                Require(two.Binds==2 && visuals.ContainsKey(second.id),"Смена видимого уровня пропустила полный bind");
                Refresh(next,"factory:0",null);Require(two.Binds==3,"Null parts не вызвал полный bind");
                Refresh(next,"factory:0",new HashSet<string>(),true);Require(two.Binds==4,"Entry/identity force не вызвал полный bind");
                next.worldId=Guid.NewGuid().ToString("N");Refresh(next,"factory:0",new HashSet<string>());
                Require(two.Binds==5,"Новый мир не вызвал полный bind");
                visuals.Clear(); // The test owns all fake children; runtime never became active.
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
        }
        private static void Require(bool valid,string reason) {if(!valid) throw new InvalidOperationException("Представления актёров: "+reason);}
    }
}
