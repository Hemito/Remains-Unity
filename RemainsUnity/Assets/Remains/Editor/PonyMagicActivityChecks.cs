using System;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    // Detached state fixtures and the actual compiled/native pony prefab. These
    // checks do not claim ordinary networking, lifting admission or pixel parity.
    public static class PonyMagicActivityChecks
    {
        public static void Run()
        {
            Anatomy.Load();WeaponCatalog.Load();ActorTypes.Load();PropCatalog.Load();PonyVisual.Prepare();
            CheckRoles();CheckSource();CheckRenderer();CheckRenderer("armor.antirad");
            Debug.Log("Свечение рога: PASS — настоящие выбранные экземпляры/резервы, приоритет обычной вещи, подтверждённое удержание 25/50 кг и тела, stale epoch/release/down; actual bare и antirad PonyVisual меняют только opacity/enabled на прежнем кадре, сохраняют RGB/emission/mesh/material/матрицы/слои и пул. Heavy state здесь detached fixture, не штатный Q при лимите 30 кг.");
        }

        private static void CheckRoles()
        {
            Require(PonyMagicRenderRoles.Contains(new PonyRenderPart {sourceShape="238",sourcePath="head/morda/magic/2",slot="magic"}),
                "Не зарегистрирована точная базовая графическая роль рога.");
            Require(PonyMagicRenderRoles.Contains(new PonyRenderPart {sourceShape="238",sourcePath="head/morda/wear:273//magic/2",slot="magic"}),
                "Не зарегистрирован точный antirad alias графической роли рога.");
            Require(!PonyMagicRenderRoles.Contains(new PonyRenderPart {sourceShape="238",sourcePath="unrelated-clothing/graphic",slot="magic"}) &&
                !PonyMagicRenderRoles.Contains(new PonyRenderPart {sourceShape="other-shape",sourcePath="head/morda/magic/2",slot="magic"}),
                "Tint slot/частично совпавшее имя превратили постороннюю графику в horn activity.");
        }

        private static void CheckSource()
        {
            var fixture=new Fixture();var source=new PonyMagicActivitySource();
            Expect(fixture,source,0,PonyMagicAction.None);
            Select(fixture.Actor,fixture.Pistol.id);Expect(fixture,source,.5f,PonyMagicAction.SelectedItem,fixture.Pistol.id);
            var replacement=fixture.Actor.Copy();
            AssertActivity(source.Get(replacement),.5f,PonyMagicAction.SelectedItem,fixture.Pistol.id,0,"замена объекта того же контекста");

            Require(HeldItemSelection.Select(fixture.Actor,fixture.Ordinary.id,out var reason),"Не выбрана настоящая обычная вещь: "+reason);
            Require(fixture.Actor.weapon=="pistol" && fixture.Actor.equippedWeaponItem==fixture.Pistol.id,
                "Fixture не воспроизводит ordinary selection поверх сохранённого выбора оружия.");
            Expect(fixture,source,.5f,PonyMagicAction.SelectedItem,fixture.Ordinary.id);
            fixture.Actor.inventoryReservations[fixture.Ordinary.id]=fixture.Ordinary.count;
            Expect(fixture,source,0,PonyMagicAction.None);
            fixture.Actor.inventoryReservations.Clear();
            Select(fixture.Actor,fixture.Pistol.id);
            fixture.Actor.inventoryReservations[fixture.Pistol.id]=1;
            Expect(fixture,source,0,PonyMagicAction.None);
            fixture.Actor.inventoryReservations.Clear();Select(fixture.Actor,null);

            // Mere simulation ownership of an unheld object is not telekinesis.
            fixture.Light.owner=fixture.Actor.owner;fixture.Light.held=false;
            Expect(fixture,source,0,PonyMagicAction.None);
            fixture.Light.held=true;
            Expect(fixture,source,.5f,PonyMagicAction.Telekinesis,fixture.Light.id,25);
            fixture.Light.held=false;
            AssertActivity(source.Get(fixture.Actor),0,PonyMagicAction.None,null,0,"release без нового кадра/rebuild");
            fixture.Heavy.held=true;
            Expect(fixture,source,1,PonyMagicAction.Telekinesis,fixture.Heavy.id,50);
            fixture.Heavy.held=false;
            AssertActivity(source.Get(fixture.Actor),0,PonyMagicAction.None,null,0,"heavy release без rebuild");

            fixture.Light.held=true;Expect(fixture,source,.5f,PonyMagicAction.Telekinesis,fixture.Light.id,25);
            foreach(Action<ActorState> change in new Action<ActorState>[]
                {a=>a.generation++,a=>a.lifecycleSequence++,a=>a.owner="other-owner",a=>a.level="factory:0"})
            {
                var stale=fixture.Actor.Copy();change(stale);
                AssertActivity(source.Get(stale),0,PonyMagicAction.None,null,0,"устаревшая эпоха/owner/level");
            }
            fixture.Light.generation++;
            AssertActivity(source.Get(fixture.Actor),0,PonyMagicAction.None,null,0,"новое поколение предмета не подтверждено");
            Expect(fixture,source,.5f,PonyMagicAction.Telekinesis,fixture.Light.id,25);
            fixture.Actor.down=true;
            AssertActivity(source.Get(fixture.Actor),0,PonyMagicAction.None,null,0,"down без rebuild");
            fixture.Actor.down=false;fixture.Actor.health=0;
            AssertActivity(source.Get(fixture.Actor),0,PonyMagicAction.None,null,0,"HP0 без rebuild");
            fixture.Actor.health=100;fixture.Actor.connected=false;
            AssertActivity(source.Get(fixture.Actor),0,PonyMagicAction.None,null,0,"неактивный персонаж хаба");
            fixture.Actor.connected=true;fixture.Light.held=false;

            // Body hold has an explicit actor ID/token, unlike simulation owner.
            fixture.Body.hold=new ActorHoldState {holder=fixture.Actor.id,token="magic-body-hold",pendingAttack=true};
            Expect(fixture,source,0,PonyMagicAction.None);
            fixture.Body.hold.pendingAttack=false;
            float bodyMass=ActorTypes.Get(fixture.Body.actorType).bodyMassKg;
            Require(bodyMass>0,"У actual pony type отсутствует положительная масса тела.");
            Expect(fixture,source,bodyMass>=50?1:.5f,PonyMagicAction.Telekinesis,fixture.Body.id,bodyMass);
            fixture.Body.hold=null;
            AssertActivity(source.Get(fixture.Actor),0,PonyMagicAction.None,null,0,"release тела без rebuild");

            fixture.Colors.Rebuild(null);source.Rebuild(null,fixture.Colors);
            AssertActivity(source.Get(fixture.Actor),0,PonyMagicAction.None,null,0,"очистка мира");
            var otherWorld=new WorldState {worldId="magic-check-other-world"};fixture.Colors.Rebuild(otherWorld);
            bool rejected=false;
            try {source.Rebuild(fixture.World,fixture.Colors);} catch(InvalidOperationException) {rejected=true;}
            Require(rejected,"Источники цветов другого мира приняты для horn activity.");
        }

        private static void CheckRenderer(string clothing=null)
        {
            var fixture=new Fixture();var source=new PonyMagicActivitySource();
            if(clothing!=null)
            {
                var item=SharedInventory.Create(clothing,1);fixture.Actor.inventory.Add(item);
                Require(CosmeticEquipment.TryEquip(fixture.Actor,item.id,false,out var reason),
                    "Не надет actual antirad экземпляр: "+reason);
            }
            var visual=ActorVisualFactory.Create(fixture.Actor) as PonyVisual;
            Require(visual!=null,"Actual actor prefab не содержит PonyVisual.");
            try
            {
                visual.ConfigureMagicPreview(1);visual.Bind(fixture.Actor);visual.DrawPose(0);
                if(clothing!=null)
                    Require(visual.CurrentDrawPlan.HasEquipment && visual.CurrentDrawPlan.Frames(visual.DrawnClip)[visual.DrawnFrame]
                        .Any(p=>p.sourceShape=="238" && p.sourcePath=="head/morda/wear:273//magic/2" && PonyMagicRenderRoles.Contains(p)),
                        "Надетый antirad не показал точный compiled alias роли рога.");
                Require(visual.TryGetMagicRenderer(out var magic) && magic!=null,
                    "У actual idle кадра отсутствует объявленная horn role.");
                var idle=new RenderSnapshot(visual,magic);
                AssertRenderer(visual,magic,1,"явный preview");
                fixture.Rebuild(source);visual.ConfigureMagicSource(source);
                Draw(visual,fixture,magic,idle,0,"preview→world empty на прежнем кадре");
                Select(fixture.Actor,fixture.Pistol.id);fixture.Rebuild(source);
                Draw(visual,fixture,magic,idle,.5f,"выбранный настоящий пистолет");
                AssertActivity(visual.DrawnMagicActivity,.5f,PonyMagicAction.SelectedItem,fixture.Pistol.id,0,"drawn pistol witness");
                Require(HeldItemSelection.Select(fixture.Actor,fixture.Ordinary.id,out var reason),"Не выбрана ordinary fixture: "+reason);
                fixture.Rebuild(source);Draw(visual,fixture,magic,idle,.5f,"ordinary selection");
                Require(visual.DrawnMagicActivity.SourceId==fixture.Ordinary.id,
                    "Actual renderer сообщил прежний пистолет вместо ordinary selection.");
                fixture.Actor.inventoryReservations[fixture.Ordinary.id]=fixture.Ordinary.count;
                fixture.Rebuild(source);Draw(visual,fixture,magic,idle,0,"полный резерв ordinary selection");
                fixture.Actor.inventoryReservations.Clear();Select(fixture.Actor,null);fixture.Rebuild(source);
                Draw(visual,fixture,magic,idle,0,"пустые копыта");
                fixture.Light.held=true;fixture.Rebuild(source);
                Draw(visual,fixture,magic,idle,.5f,"подтверждённые 25 кг");
                AssertActivity(visual.DrawnMagicActivity,.5f,PonyMagicAction.Telekinesis,fixture.Light.id,25,"drawn light hold witness");
                fixture.Light.held=false;
                Draw(visual,fixture,magic,idle,0,"release25 без смены кадра/rebuild");
                fixture.Heavy.held=true;fixture.Rebuild(source);
                Draw(visual,fixture,magic,idle,1,"подтверждённые 50 кг");
                AssertActivity(visual.DrawnMagicActivity,1,PonyMagicAction.Telekinesis,fixture.Heavy.id,50,"drawn heavy hold witness");
                fixture.Heavy.held=false;
                Draw(visual,fixture,magic,idle,0,"release50 без смены кадра/rebuild");

                fixture.Light.held=true;fixture.Rebuild(source);Draw(visual,fixture,magic,idle,.5f,"перед down");
                fixture.Actor.down=true;visual.DrawPose(0);
                Require(visual.TryGetMagicRenderer(out magic) && magic!=null,"Нет horn role actual down кадра.");
                AssertRenderer(visual,magic,0,"down немедленно без rebuild");
                // Passive down selects its terminal frame; admission/revive selects
                // a living pose normally. Equal sample time does not imply equal
                // frame across that lifecycle transition. Rebaseline actual down,
                // then verify the ordinary idle publication against its own frame.
                var down=new RenderSnapshot(visual,magic);
                Draw(visual,fixture,magic,down,0,"повтор down на том же actual кадре");
                fixture.Actor.down=false;fixture.Actor.lifecycleSequence++;fixture.Actor.pose="idle";fixture.Rebuild(source);
                visual.DrawPose(0);
                Require(visual.TryGetMagicRenderer(out magic) && magic!=null,"Нет horn role actual idle после revive.");
                AssertRenderer(visual,magic,.5f,"revive показал обычный living idle");
                idle.AssertUnchanged(visual,magic,"revive вернул исходный living idle");
                Draw(visual,fixture,magic,idle,.5f,"повтор living idle после revive");
                fixture.Light.held=false;Select(fixture.Actor,null);fixture.Rebuild(source);
                Draw(visual,fixture,magic,idle,0,"release на том же living idle после revive");
                int children=visual.transform.childCount;
                for(int i=0;i<100;i++) Draw(visual,fixture,magic,idle,0,"повтор неподвижного Draw");
                Require(visual.transform.childCount==children,"Repeated Draw создал новые части/объекты.");
            }
            finally {UnityEngine.Object.DestroyImmediate(visual.gameObject);}
        }

        private static void Draw(PonyVisual visual,Fixture fixture,MeshRenderer magic,RenderSnapshot snapshot,float opacity,string label)
        {
            string before=StateJson.ToJson(fixture.World);visual.DrawPose(0);
            Require(visual.TryGetMagicRenderer(out var current) && current==magic,"Horn renderer заменён: "+label);
            AssertRenderer(visual,magic,opacity,label);snapshot.AssertUnchanged(visual,magic,label);
            if(opacity==0) AssertActivity(visual.DrawnMagicActivity,0,PonyMagicAction.None,null,0,label);
            Require(StateJson.ToJson(fixture.World)==before,"Рисование изменило игровой world/actor: "+label);
        }
        private static void AssertRenderer(PonyVisual visual,MeshRenderer renderer,float opacity,string label)
        {
            var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block);var actual=block.GetColor("_Color");
            Require(block.HasColor("_Color") && Math.Abs(actual.a-opacity)<.00001 && renderer.enabled==(opacity>0) &&
                block.GetFloat("_Emission")==1 &&
                Math.Abs(visual.DrawnMagicActivity.Opacity-opacity)<.00001,
                $"Actual opacity/видимость расходятся: {label}, color={actual}, enabled={renderer.enabled}, drawn={visual.DrawnMagicActivity.Opacity}.");
        }
        private static void Expect(Fixture fixture,PonyMagicActivitySource source,float opacity,PonyMagicAction action,string id=null,float mass=0)
        {
            string before=StateJson.ToJson(fixture.World);fixture.Rebuild(source);
            AssertActivity(source.Get(fixture.Actor),opacity,action,id,mass,"подготовленное состояние");
            Require(StateJson.ToJson(fixture.World)==before,"Источник activity изменил domain world.");
        }
        private static void AssertActivity(PonyMagicActivity actual,float opacity,PonyMagicAction action,string id,float mass,string label)
        {
            Require(actual.Opacity==opacity && actual.Action==action && actual.SourceId==id && actual.MassKg==mass,
                $"Неверная activity: {label}, actual={actual.Action}/{actual.Opacity}/{actual.SourceId}/{actual.MassKg}, expected={action}/{opacity}/{id}/{mass}.");
        }
        private static void Select(ActorState actor,string id)
        {Require(WeaponInventory.Select(actor,id,out var reason),"Не выполнен actual выбор экземпляра: "+reason);}

        private sealed class Fixture
        {
            public readonly WorldState World=new() {worldId="pony-magic-check-world"};
            public readonly ActorState Actor,Body;
            public readonly ItemStack Pistol,Ordinary;
            public readonly ContainerState Light;
            public readonly PropState Heavy;
            public readonly HeldPropColorSource Colors=new();
            public Fixture()
            {
                Actor=new ActorState {id="pony-magic-holder",character="pony-magic-holder",owner="magic-owner",player=true,connected=true,
                    controlStateVersion=1,generation=3,lifecycleSequence=7,level="hub",anatomy="pony",actorType="pony",pose="idle",
                    weapon=null,weaponInventoryVersion=1,loadoutVersion=1,x=2,y=3,
                    // Appearance migration is completed before presentation and
                    // tested separately; this fixture starts at the current schema.
                    appearance=new CharacterAppearance {version=CharacterAppearance.CurrentVersion,magic=0x286CFF}};
                // SharedInventory allocates proper stack identities; the real content
                // magazine creates a valid version1 weapon instance, not a bare token.
                Pistol=SharedInventory.Create("pistol",1);Pistol.weaponState=new WeaponItemState {rounds=WeaponCatalog.Get("pistol").magazineSize};
                Ordinary=SharedInventory.Create("p10",12);Actor.inventory.Add(Pistol);Actor.inventory.Add(Ordinary);
                Body=new ActorState {id="pony-magic-body",owner="other-owner",player=true,connected=true,generation=2,
                    lifecycleSequence=4,level="hub",anatomy="pony",actorType="pony",weapon=null};
                World.actors.Add(Actor);World.actors.Add(Body);
                Light=new ContainerState {id="pony-magic-light",definition="mcrate1",level="hub",owner=Actor.owner,generation=2,
                    hasPhysicalBody=true,movable=true,mass=25,width=1,height=1,appearance=new ContainerAppearanceState {version=1}};
                Heavy=new PropState {id="pony-magic-heavy",definition="box",level="hub",owner=Actor.owner,generation=2,
                    hasPhysicalBody=true,movable=true,mass=50,width=1.2f,height=1.2f};
                World.containers.Add(Light);World.props.Add(Heavy);
            }
            public void Rebuild(PonyMagicActivitySource source) {Colors.Rebuild(World);source.Rebuild(World,Colors);}
        }

        private sealed class RenderSnapshot
        {
            private readonly string clip;
            private readonly int frame;
            private readonly MeshRenderer[] renderers;
            private readonly Mesh[] meshes;
            private readonly Material[] materials;
            private readonly Matrix4x4[] matrices;
            private readonly Color[] colors;
            private readonly float[] emission;
            private readonly int[] order,layer;
            private readonly bool[] enabled;
            private readonly Matrix4x4 root;
            public RenderSnapshot(PonyVisual visual,MeshRenderer magic)
            {
                clip=visual.DrawnClip;frame=visual.DrawnFrame;root=visual.transform.localToWorldMatrix;
                renderers=visual.GetComponentsInChildren<MeshRenderer>(true);
                meshes=renderers.Select(r=>r.GetComponent<MeshFilter>().sharedMesh).ToArray();
                materials=renderers.Select(r=>r.sharedMaterial).ToArray();matrices=renderers.Select(r=>r.transform.localToWorldMatrix).ToArray();
                order=renderers.Select(r=>r.sortingOrder).ToArray();layer=renderers.Select(r=>r.sortingLayerID).ToArray();enabled=renderers.Select(r=>r.enabled).ToArray();
                colors=new Color[renderers.Length];emission=new float[renderers.Length];
                for(int i=0;i<renderers.Length;i++) {var block=new MaterialPropertyBlock();renderers[i].GetPropertyBlock(block);colors[i]=block.GetColor("_Color");emission[i]=block.GetFloat("_Emission");}
                int index=Array.IndexOf(renderers,magic);
                Require(index>=0 && emission[index]==1,"Actual horn role не сохраняет исходный self-emission.");
            }
            public void AssertUnchanged(PonyVisual visual,MeshRenderer magic,string label)
            {
                Require(visual.DrawnClip==clip && visual.DrawnFrame==frame && visual.DrawnTime==0 && visual.transform.localToWorldMatrix.Equals(root),
                    "Activity сменила неподвижный кадр/Transform: "+label);
                Require(visual.GetComponentsInChildren<MeshRenderer>(true).SequenceEqual(renderers),"Пересоздан пул renderer: "+label);
                for(int i=0;i<renderers.Length;i++)
                {
                    var renderer=renderers[i];var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block);var color=block.GetColor("_Color");
                    Require(renderer.GetComponent<MeshFilter>().sharedMesh==meshes[i] && renderer.sharedMaterial==materials[i] &&
                        renderer.transform.localToWorldMatrix.Equals(matrices[i]) && renderer.sortingOrder==order[i] && renderer.sortingLayerID==layer[i] &&
                        color.r==colors[i].r && color.g==colors[i].g && color.b==colors[i].b && block.GetFloat("_Emission")==emission[i] &&
                        (renderer==magic || color.a==colors[i].a && renderer.enabled==enabled[i]),
                        "Activity изменила RGB/ресурс/матрицу/порядок либо постороннюю часть: "+label+", part="+i);
                }
            }
        }
        private static void Require(bool condition,string detail)
        {if(!condition) throw new InvalidOperationException("Свечение рога: "+detail);}
    }
}
