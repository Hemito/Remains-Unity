using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Persistence;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class PropBodyChecks
    {
        public static void Run()=>Run(true);
        public static void Run(bool native)
        {
            CheckLegacyAndValidation();CheckPhysicsAndInteraction();CheckPose();CheckReload();CheckFileLoad();CheckExpansion();
            if(native) {CheckAuthoring();Debug.Log("Физические тела: PASS — legacy defaults, NoBody/TK/опора/дверь/поза/E, reload и добавочные v1/v2/v3 без возврата удалённого содержимого.");}
        }
        private static ContainerState Wall()=>new()
        {
            id="factory:0:check:wall",definition="check:wall",level="factory:0",x=3,y=1,
            movable=false,hasPhysicalBody=false,mass=0,width=1.46f,height=.98f,anchorHeight=0
        };
        private static TelekinesisSettings Settings()=>new()
        {maximumMass=100,freeMass=5,range=12,releaseRange=13,manaPerSecondPerRootKg=1,minimumGrabMana=0};
        private static void CheckLegacyAndValidation()
        {
            var legacy=StateJson.FromJson<ContainerState>("{\"id\":\"legacy\",\"mass\":25,\"mod:data\":{\"exact\":12345678901234567890,\"nested\":[true,null]}}");
            Require(legacy.movable && legacy.hasPhysicalBody,"Старый JSON изменил переносимость или наличие тела");
            var definition=StateJson.FromJson<PropDefinition>("{\"id\":\"legacy\",\"resource\":\"original\",\"mass\":25,\"width\":1.2,\"height\":1.2}");
            Require(definition.movable && definition.hasPhysicalBody,"Старое определение потеряло legacy defaults");
            var restored=StateJson.Copy(legacy);PropBodyRules.Validate(restored);
            Require(JToken.DeepEquals(legacy.extensions["mod:data"],restored.extensions["mod:data"]),"Reload потерял неизвестные данные старого контейнера");
            PropBodyRules.Validate(true,true,1,"check");PropBodyRules.Validate(false,true,1,"check");
            PropBodyRules.Validate(false,false,0,"check");PropBodyRules.Validate(false,false,2,"check");
            Reject(()=>PropBodyRules.Validate(true,false,0,"NoBody movable"),"NoBody movable");
            foreach(float mass in new[]{0,-1,float.NaN,float.PositiveInfinity})
                Reject(()=>PropBodyRules.Validate(true,true,mass,"Body mass"),"Body mass");
            foreach(float mass in new[]{-1,float.NaN,float.PositiveInfinity})
                Reject(()=>PropBodyRules.Validate(false,false,mass,"NoBody mass"),"NoBody mass");
            var wall=Wall();PropBodyRules.Validate(wall);
            wall.held=true;Reject(()=>PropBodyRules.Validate(wall),wall.id);wall.held=false;
            wall.vx=.01f;Reject(()=>PropBodyRules.Validate(wall),wall.id);wall.vx=0;
            wall.vy=float.NaN;Reject(()=>PropBodyRules.Validate(wall),wall.id);
        }
        private static void CheckPhysicsAndInteraction()
        {
            var wall=Wall();var moving=new PropState {id="moving",level=wall.level,x=3};
            var start=new Vector2(3,3);var end=new Vector2(3,.5f);
            Require(!PropSupport.Resolve(moving,start,ref end,new[]{wall}) && end.y==.5f,"NoBody стал опорой для груза");
            var body=StateJson.Copy(wall);body.hasPhysicalBody=true;body.mass=25;
            Require(PropSupport.Resolve(moving,start,ref end,new[]{body}) && Mathf.Abs(end.y-1.98f)<.00001f,
                "Неподвижное физическое тело перестало быть опорой");
            var world=new WorldState();world.containers.Add(wall);
            var door=new DoorState {id="door",definition="check",level=wall.level,surfaceTexture="floor",x=2.5f,y=1,width=1,height=1};
            Require(!DoorOccupancy.IsOccupied(world,door),"NoBody мешает закрытию двери");
            world.containers[0]=body;Require(DoorOccupancy.IsOccupied(world,door),"Старое тело исчезло из проверки двери");world.containers[0]=wall;
            string before=StateJson.ToJson(wall);
            PropMotion.Step(wall,null,.1f,new Vector2(20,20),new[]{moving});
            Require(StateJson.ToJson(wall)==before,"NoBody получил физическое движение");
            var actor=new ActorState {id="player",owner="owner",level=wall.level,connected=true,player=true,mana=100,x=3,y=1};
            var rule=new MassLimitedTelekinesisRule(Settings());
            Require(!new TelekinesisTarget(wall).Movable && rule.Rejection(actor,wall,false)?.Contains("закреплён")==true,"NoBody перенесён телекинезом");
            InventorySource source=wall;
            Require(!source.Mass.HasValue && ((InventorySource)body).Mass==25,"UI получил выдуманную массу или потерял прежнюю");
            Require(source.Bounds==new Rect(2.27f,1,1.46f,.98f),"E bounds стали графическим AABB");
            var rules=new GameplayRuleSet(new PrototypeDamageRule(),new ZeroHealthKnockdownRule(),new HubPartyDefeatRule(),null,null,null,rule,new StableProximityOwnershipRule());
            var selected=InteractionSelection.Select(world,actor,rules,new Vector2(3,1.5f));
            Require(selected.kind==InteractionKind.Container && selected.id==wall.id && selected.bounds==source.Bounds,"NoBody исчез из выбора E");
        }
        private static void CheckPose()
        {
            var wall=Wall();wall.owner="coordinator";wall.generation=7;wall.sequence=3;
            var actor=new ActorState {id="player",owner=wall.owner,level=wall.level,player=true,connected=true};
            var world=new WorldState();world.containers.Add(wall);world.actors.Add(actor);
            Require(!PropPoseScope.CanPublish(world,actor,wall,false),"NoBody разрешил публикацию позы");
            string before=StateJson.ToJson(wall);
            Require(!PropReplication.Accept(wall,7,4,30,10,5,2) && StateJson.ToJson(wall)==before,"Поза изменила закреплённый объект");
            var badLocal=StateJson.Copy(wall);badLocal.x=40;badLocal.sequence=100;badLocal.hasPhysicalBody=true;badLocal.movable=true;badLocal.mass=25;
            PropReplication.PreserveNewerPose(badLocal,wall,wall.owner);
            Require(StateJson.ToJson(wall)==before,"Старая локальная поза перезаписала fixed authoritative state");
            var cargo=new PropState {id="legacy",owner="owner",generation=1};
            Require(PropReplication.Accept(cargo,1,1,4,3,2,1) && cargo.x==4 && cargo.sequence==1,"Прежний переносимый объект потерял позы");
            var incoming=StateJson.Copy(cargo);incoming.x=0;incoming.sequence=0;
            PropReplication.PreserveNewerPose(cargo,incoming,"owner");Require(incoming.x==4 && incoming.sequence==1,"Прежнее сохранение новой локальной позы сломано");
        }
        private static void CheckReload()
        {
            var world=new WorldState {worldId=Guid.NewGuid().ToString("N")};var wall=Wall();world.containers.Add(wall);
            wall.items.Add(new ItemStack {id=wall.id+":item",definition="medkit",count=2});
            wall.extensions=new System.Collections.Generic.Dictionary<string,JToken> { ["mod:wall"]=JObject.Parse("{\"exact\":12345678901234567890,\"child\":[1,2]}") };
            var restored=new RecoverySnapshot(StateJson.ToJson(world)).CreateLiveWorld();
            Require(StateJson.ToJson(restored)==StateJson.ToJson(world),"Recovery изменил fixed state, лут или unknown данные");
            restored.containers[0].items.Clear();restored=new RecoverySnapshot(StateJson.ToJson(restored)).CreateLiveWorld();
            Require(restored.containers[0].items.Count==0 && !restored.containers[0].movable && !restored.containers[0].hasPhysicalBody,"Reload восстановил лут или тело");
            foreach(int broken in new[]{0,1,2})
            {
                var bad=StateJson.Copy(world);var fixedProp=bad.containers[0];
                if(broken==0) fixedProp.held=true;else if(broken==1) fixedProp.vx=1;else fixedProp.movable=true;
                RejectData(()=>new RecoverySnapshot(StateJson.ToJson(bad)),fixedProp.id);
            }
        }
        private static void CheckExpansion()
        {
            const string level="factory:0",old="factory:0:old",left="factory:0:box:left",right="factory:0:box:right";
            var v1=new PhysicalLayout {level=level,patch=level+":physical-v1",props=new[]{new PropState {id=old,definition="legacy",level=level,anchorHeight=0}}};
            var v2=StateJson.Copy(v1);v2.patch=level+":physical-v2";
            v2.props=v2.props.Concat(new[]{new PropState {id=left,definition="bigbox",level=level,anchorHeight=0},new PropState {id=right,definition="bigbox",level=level,anchorHeight=0}}).ToArray();
            v2.expansion=new PhysicalExpansion {previousPatch=v1.patch,additions=new[]{left,right}};
            var v3=StateJson.Copy(v2);v3.patch=level+":physical-v3";v3.containers=new[]{Wall()};
            v3.expansion=new PhysicalExpansion {previousPatch=v2.patch,additions=new[]{Wall().id},
                earlierVersions=new[]{new ContentExpansionStep {previousPatch=v1.patch,additions=new[]{Wall().id,left,right}}}};
            var fresh=new WorldState();PhysicalContentInstaller.Apply(fresh,v3);
            Require(fresh.props.Count==3 && fresh.containers.Count==1,"Fresh v3 неполон");
            var from1=new WorldState();PhysicalContentInstaller.Apply(from1,v1);from1.props.Clear();
            PhysicalContentInstaller.Apply(from1,v3);
            Require(from1.props.Count==2 && !from1.props.Any(p=>p.id==old) && from1.containers.Count==1,"v1→v3 вернул удалённый объект или потерял v2 добавления");
            var from2=new WorldState();PhysicalContentInstaller.Apply(from2,v1);PhysicalContentInstaller.Apply(from2,v2);
            from2.props.RemoveAll(p=>p.id==old || p.id==left);var moved=from2.props.Single();moved.x=17;moved.held=true;moved.owner="saved";moved.generation=9;
            moved.extensions=new System.Collections.Generic.Dictionary<string,JToken> { ["mod:body"]=new JValue("keep") };
            string kept=StateJson.ToJson(from2.props);PhysicalContentInstaller.Apply(from2,v3);
            Require(StateJson.ToJson(from2.props)==kept && from2.containers.Count==1,"v2→v3 вернул ящик или изменил старое состояние");
            from2.containers[0].items.Clear();from2=StateJson.Copy(from2);
            Require(!PhysicalContentInstaller.Apply(from2,v3) && StateJson.ToJson(from2.props)==kept,"Reload v3 сбросил содержимое");
            from2.containers.Clear();string removed=StateJson.ToJson(from2);
            Require(!PhysicalContentInstaller.Apply(from2,v3) && StateJson.ToJson(from2)==removed,"Удалённый v3 стенд воскрес");
        }
        private static void CheckFileLoad()
        {
            string root=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"Remains-PropBodyChecks-"+Guid.NewGuid().ToString("N")));
            string path=Path.Combine(root,"world.json");Directory.CreateDirectory(root);
            try
            {
                var store=new SaveStore(root);var world=new WorldState {worldId=Guid.NewGuid().ToString("N")};world.containers.Add(Wall());
                string json=StateJson.ToJson(world);File.WriteAllText(path,json);
                Require(StateJson.ToJson(store.LoadWorld())==json,"Прямой LoadWorld изменил неподвижное состояние");
                foreach(int broken in new[]{0,1,2})
                {
                    var bad=StateJson.Copy(world);var wall=bad.containers[0];
                    if(broken==0) wall.held=true;else if(broken==1) wall.vy=1;else wall.movable=true;
                    json=StateJson.ToJson(bad);File.WriteAllText(path,json);
                    RejectData(()=>store.LoadWorld(),wall.id);
                    Require(File.ReadAllText(path)==json,"Отказ LoadWorld переписал исходник");
                    Require(StateJson.ToJson(store.LoadWorld(false))==json,"Raw load потерял данные для сохранения источника");
                }
            }
            finally
            {
                string parent=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;
                if(!root.StartsWith(parent,StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(root).StartsWith("Remains-PropBodyChecks-",StringComparison.Ordinal))
                    throw new InvalidOperationException("Небезопасный путь очистки проверки физических тел: "+root);
                Directory.Delete(root,true);
            }
        }
        private static void CheckAuthoring()
        {
            PropCatalog.Load();var original=StateJson.Copy(PropCatalog.Get("mcrate1"));
            Require(original.movable && original.hasPhysicalBody && original.mass==25,"Старый сохранённый SO mcrate1 потерял физическое тело или переносимость");
            var asset=ScriptableObject.CreateInstance<PropAsset>();var root=new GameObject("Проверка контракта тела");
            try
            {
                var definition=StateJson.Copy(original);definition.movable=false;definition.hasPhysicalBody=false;definition.mass=0;definition.width=1.46f;definition.height=.98f;
                PropCatalog.Register(definition,true);var created=PropCatalog.Create("check:created",definition.id,"factory:0",new Vector2(2,3));
                Require(!created.movable && !created.hasPhysicalBody && created.mass==0 && created.anchorHeight==0,"PropCatalog.Create потерял NoBody policy");
                asset.SetDefinition(definition);var room=root.AddComponent<RoomAuthoring>();var content=root.AddComponent<RoomPhysicalContent>();content.Configure("physical-v3");
                var child=new GameObject("Стенд");child.transform.SetParent(root.transform,false);child.transform.localPosition=new Vector3(2,3,0);
                child.AddComponent<RoomContainerPlacement>().Configure("wall",asset);
                var additions=new[]{"wall"};var older=new[]{new ContentExpansionStep {previousPatch="physical-v1",additions=new[]{"wall","old-box"}}};
                content.ConfigureExpansion("physical-v2",additions,older);additions[0]="changed";older[0].additions[0]="changed";older[0].previousPatch="changed";
                var layout=RoomPhysicalContent.Read(room,"factory:0");var placed=layout.containers.Single();
                Require(!placed.movable && !placed.hasPhysicalBody && placed.mass==0 && placed.anchorHeight==0 && placed.x==2 && placed.y==3,"RoomPhysicalContent потерял NoBody policy/anchor");
                Require(layout.expansion.additions.Single()=="factory:0:wall" && layout.expansion.earlierVersions.Single().previousPatch=="factory:0:physical-v1" &&
                    layout.expansion.earlierVersions.Single().additions[0]=="factory:0:wall","ConfigureExpansion сохранил внешние mutable ссылки или потерял цепочку");
            }
            finally {PropCatalog.Register(original,true);UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(asset);}
        }
        private static void Reject(Action action,string context)
        {
            try {action();} catch(InvalidOperationException error) {Require(error.Message.Contains(context),"Отказ потерял контекст: "+context);return;}
            throw new InvalidOperationException("Физические тела: ошибочное состояние принято: "+context);
        }
        private static void RejectData(Action action,string context)
        {
            try {action();} catch(InvalidDataException error) {Require(error.Message.Contains(context) && error.InnerException!=null,"Отказ recovery потерял контекст");return;}
            throw new InvalidOperationException("Физические тела: повреждённый recovery принят: "+context);
        }
        private static void Require(bool value,string reason) {if(!value) throw new InvalidOperationException("Физические тела: "+reason);}
    }
}
