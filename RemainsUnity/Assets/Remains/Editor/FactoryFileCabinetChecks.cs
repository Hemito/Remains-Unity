using System;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class FactoryFileCabinetChecks
    {
        public static void CheckDefinition(PropDefinition definition)
        {
            Require(definition!=null && definition.id==FactoryFileCabinetAuthoring.Id && definition.name==FactoryFileCabinetAuthoring.Name &&
                definition.resource==FactoryFileCabinetAuthoring.Resource && definition.width==FactoryFileCabinetAuthoring.Width &&
                definition.height==FactoryFileCabinetAuthoring.Height && definition.mass==FactoryFileCabinetAuthoring.MassKg &&
                definition.movable && definition.hasPhysicalBody,"Определение отличается от принятой политики картотеки.");
            PropBodyRules.Validate(definition.movable,definition.hasPhysicalBody,definition.mass,"type=filecab");
        }
        // Managed checks may run without importing assets. Uses actual physics, inventory bounds and persistence consumers.
        public static void CheckBodyContract()
        {
            var definition=FactoryFileCabinetAuthoring.CreateDefinition();CheckDefinition(definition);
            foreach(int fps in new[]{30,60,144})
            {
                var room=new RoomLayout {id="filecab-check",left=-10,right=10,bottom=-1,top=10,
                    platforms=new[]{new RoomSurface {x=-10,y=-.2f,width=20,height=.2f,texture="floor"}},ladders=Array.Empty<RoomLadder>()};
                var cabinet=new ContainerState {id="filecab-check",definition=definition.id,level="factory:0",owner="local",generation=7,
                    x=0,y=3,width=definition.width,height=definition.height,mass=definition.mass,anchorHeight=0,
                    movable=definition.movable,hasPhysicalBody=definition.hasPhysicalBody};
                cabinet.items.Add(new ItemStack {id="filecab:pin",definition="pin",count=2});
                cabinet.itemAliases.Add("old-pin-id","filecab:pin");
                string inventory=StateJson.ToJson(cabinet.items),aliases=StateJson.ToJson(cabinet.itemAliases);
                Require(new TelekinesisTarget(cabinet).Movable,"Картотека не допускает обычный TK.");
                for(int i=0;i<2*fps;i++) PropMotion.Step(cabinet,room,1f/fps,null);
                Require(Mathf.Abs(cabinet.y)<.001f && cabinet.vy==0 && cabinet.owner=="local" && cabinet.generation==7,
                    $"Картотека не приземлилась на пол без изменения владельца при {fps} FPS.");
                InventorySource source=cabinet;
                Require(source.Bounds==new Rect(-.32f,0,.64f,1.6f) && source.Find("old-pin-id")?.count==2,
                    "Курсорные границы или alias инвентаря отличаются от фактического тела.");
                var cargo=new PropState {id="cargo",level=cabinet.level,x=0,y=2.1f,width=.4f,height=.4f,anchorHeight=0,mass=1};
                for(int i=0;i<fps;i++) PropMotion.Step(cargo,room,1f/fps,null,new PropState[]{cabinet});
                Require(Mathf.Abs(cargo.y-1.6f)<.001f && cargo.vy==0,
                    $"Реальный prop не поддерживается узким телом картотеки при {fps} FPS; source cutoff <40px не должен применяться.");
                Require(StateJson.ToJson(cabinet.items)==inventory && StateJson.ToJson(cabinet.itemAliases)==aliases,
                    "Свободная физика или опора изменили содержимое картотеки.");
                var restored=StateJson.Copy(cabinet);
                Require(restored.owner==cabinet.owner && restored.generation==cabinet.generation && restored.hasPhysicalBody && restored.movable &&
                    StateJson.ToJson(restored.items)==inventory && StateJson.ToJson(restored.itemAliases)==aliases,
                    "Сохранение потеряло тело, владельца, содержимое или alias.");
            }
        }
        public static void Run()
        {
            CheckBodyContract();var asset=FactoryFileCabinetAuthoring.ValidateArtifacts();
            var data=asset.CreateDefinition();
            foreach(float anchor in new[]{0f,data.height*.5f})
            {
                var root=UnityEngine.Object.Instantiate(asset.Prefab.gameObject);
                try
                {
                    var view=root.GetComponent<PropView>();
                    var state=new PropState {id="filecab-drawn-check",definition=data.id,owner="local",level="factory:0",x=3,y=2+anchor,
                        width=data.width,height=data.height,anchorHeight=anchor,mass=data.mass};
                    view.DrawMotion(state,"local",false,0);var bounds=view.Artwork.bounds;
                    Require(Mathf.Abs(bounds.min.y-2)<.001f && Mathf.Abs(bounds.center.x-state.x)<.001f &&
                        Mathf.Abs(bounds.size.x-data.width)<.001f && Mathf.Abs(bounds.size.y-data.height)<.001f,
                        $"Нарисованная картотека не совпадает с физическим телом: anchorHeight={anchor}, bounds={bounds}.");
                }
                finally {UnityEngine.Object.DestroyImmediate(root);}
            }
            var holder=new ActorState {level="factory:0",x=1,y=0,mana=100};
            var cabinet=new ContainerState {id="filecab-tk-check",definition=data.id,level=holder.level,x=0,y=0,mass=data.mass,
                width=data.width,height=data.height,anchorHeight=0};
            Require(new MassLimitedTelekinesisRule(TelekinesisSettings.Load()).Rejection(holder,cabinet,false)==null,
                "Текущий профиль TK не позволяет поднять принятую картотеку 12 кг рядом с игроком.");
            Debug.Log("Картотека: PASS — native 32×80, body-centre/pivot, совпадение рисунка и тела для обеих привязок; TK, реальная свободная физика и штабелирование при 30/60/144 FPS, содержимое/alias/reload.");
        }
        private static void Require(bool condition,string reason)
        {if(!condition) throw new InvalidOperationException("Картотека: "+reason);}
    }
}
