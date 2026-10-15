using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Remains.Domain;
using Remains.Persistence;
using Remains.Serialization;

namespace Remains.Editor
{
    public static class LocalRecoveryPairChecks
    {
        private sealed class Fixture
        {
            public SaveStore store;public RecoverySnapshot oldWorld,newWorld;public ActorState oldActor,newActor;
            public string root,pending,characterJson;
        }
        public static void Run()=>RunAt(Path.Combine(Path.GetTempPath(),"remains-local-pair-"+Guid.NewGuid().ToString("N")));
        public static void RunAt(string root)
        {
            var success=Create(Path.Combine(root,"success"));
            success.store.SaveRecoveryWithCharacter(success.newWorld,success.newActor);
            CheckRecovered(success);Require(!success.store.RecoverPendingPair(),"Completed pair replayed");
            foreach(bool afterCharacter in new[] {false,true})
            {
                var failure=Create(Path.Combine(root,"failure-"+afterCharacter));Interrupt(failure,afterCharacter);
                Require(File.Exists(failure.pending) && failure.store.SelectedSnapshot().Id==failure.oldWorld.Id,"Interrupted intent/world boundary missing");
                var pendingActor=StateJson.FromJson<ActorState>(File.ReadAllText(CharacterPath(failure)));
                Require(pendingActor.inventory[0].count==(afterCharacter?4:1),"Interrupted character boundary wrong");
                var restarted=new SaveStore(failure.store.CharacterDirectoryPath,failure.store.WorldDirectoryPath);
                Require(restarted.RecoverPendingPair(),"Restart did not complete pending pair");failure.store=restarted;CheckRecovered(failure);
                Require(!restarted.RecoverPendingPair(),"Pair applied twice");
            }
            var read=Create(Path.Combine(root,"read-hook"));Interrupt(read,true);
            var fresh=new SaveStore(read.store.CharacterDirectoryPath,read.store.WorldDirectoryPath);
            Require(StateJson.FromJson<ActorState>(fresh.ReadCharacterSource()).inventory[0].count==4 && fresh.SelectedSnapshot().Id==read.newWorld.Id,
                "ReadCharacterSource exposed profile before pending world recovery");
            var host=Create(Path.Combine(root,"host"));host.store.SaveRecovery(host.newWorld);
            Interrupt(host,true,false);
            var later=host.newWorld.CreateLiveWorld();later.revision++;later.checkpointSequence++;
            var laterSnapshot=RecoverySnapshot.FromWorld(later);host.store.SaveRecovery(laterSnapshot);
            var hostRestart=new SaveStore(host.store.CharacterDirectoryPath,host.store.WorldDirectoryPath);
            Require(hostRestart.RecoverPendingPair() && hostRestart.SelectedSnapshot().Id==laterSnapshot.Id &&
                StateJson.FromJson<ActorState>(hostRestart.ReadCharacterSource()).inventory[0].count==4,
                "Host-local replay rewound newer canonical head or lost profile");
            var missing=Create(Path.Combine(root,"missing-host-archive"));
            Refuse(()=>missing.store.SaveRecoveryWithCharacter(missing.newWorld,missing.newActor,false),"Missing canonical archive accepted");
            Require(!File.Exists(missing.pending) && missing.store.SelectedSnapshot().Id==missing.oldWorld.Id,"Missing archive caused publication");
            CheckRefusal(root,"foreign-root",f=>ChangeIntent(f,j=>j["worldDirectory"]=Path.Combine(f.root,"unrelated")));
            CheckRefusal(root,"wrong-character",f=>ChangeIntent(f,j=>j["characterId"]="another-character"));
            CheckRefusal(root,"wrong-world-hash",f=>ChangeIntent(f,j=>j["worldSha256"]=new string('0',64)));
            CheckRefusal(root,"future-version",f=>ChangeIntent(f,j=>j["version"]=3));
            CheckRefusal(root,"missing-world-archive",f=>File.Delete(Path.Combine(f.store.WorldDirectoryPath,"Worlds",f.newWorld.WorldId,"Snapshots",f.newWorld.Id+".json")));
            CheckRefusal(root,"corrupt-json",f=>File.WriteAllText(f.pending,"{truncated"));
            CheckRefusal(root,"external-profile",f=>{var actor=f.oldActor.Copy();actor.x=999;File.WriteAllText(CharacterPath(f),StateJson.ToJson(actor));});
            CheckRefusal(root,"external-world",f=>{var world=f.oldWorld.CreateLiveWorld();world.revision+=20;File.WriteAllText(Path.Combine(f.store.WorldDirectoryPath,"world.json"),StateJson.ToJson(world));});
            CheckRefusal(root,"unknown-large-number",f=>ChangeIntent(f,j=>j["unknown-mod-number"]=double.MaxValue));
            var invalid=Create(Path.Combine(root,"invalid-new-payload"));
            var invalidActor=invalid.newActor.Copy();invalidActor.extensions=new Dictionary<string,JToken> {{"unknown-large-number",double.MaxValue}};
            Refuse(()=>invalid.store.SaveRecoveryWithCharacter(invalid.newWorld,invalidActor),"Invalid mod number accepted new profile");
            invalidActor=invalid.newActor.Copy();invalidActor.character="another-character";
            Refuse(()=>invalid.store.SaveRecoveryWithCharacter(invalid.newWorld,invalidActor),"Different character accepted new pair");
            Require(!File.Exists(invalid.pending) && invalid.store.SelectedSnapshot().Id==invalid.oldWorld.Id &&
                StateJson.FromJson<ActorState>(invalid.store.ReadCharacterSource()).inventory[0].count==1,"Invalid new payload changed files");
            UnityEngine.Debug.Log("Local recovery pair: PASS — exact immutable payloads/IDs/aliases/mod fields, interruptions before/after profile, replay once/read hook, host-local archive without head rewind, foreign/corrupt/changed/invalid data preserved with refusal. Cross-peer and global host/profile atomicity не утверждаются.");
        }
        private static Fixture Create(string root)
        {
            var store=new SaveStore(Path.Combine(root,"Profile"),Path.Combine(root,"Library"));
            var world=new WorldState {revision=2};WorldHistory.BeginContinuation(world);
            var actor=new ActorState {id="player:local-pair",character="local-pair",player=true,connected=true,owner="pair-owner",level="hub",x=4,y=.8f};
            actor.inventory.Add(new ItemStack {id="item:personal-caps",definition="caps",count=1});
            world.actors.Add(actor);world.containers.Add(new ContainerState {id="container:local-pair",items=new List<ItemStack> {new ItemStack {id="item:source-caps",definition="caps",count=3}}});
            var oldWorld=store.SaveWorld(world);store.SaveCharacter(actor);
            var changed=oldWorld.CreateLiveWorld();changed.revision++;changed.checkpointSequence++;
            var newActor=changed.actors[0];newActor.inventory[0].count=4;
            newActor.inventoryAliases["item:source-caps"]="item:personal-caps";
            newActor.inventory[0].extensions=new Dictionary<string,JToken> {{"pair-mod",new JObject {{"data",new JArray(decimal.MaxValue,"сохранить")}}}};
            changed.containers[0].items.Clear();changed.completedTransactions.Add("transaction:local-pair");
            var newWorld=RecoverySnapshot.FromWorld(changed);newActor=newActor.Copy();newActor.x=10.4f;
            return new Fixture {root=root,store=store,oldWorld=oldWorld,newWorld=newWorld,oldActor=actor,newActor=newActor,
                pending=Path.Combine(store.CharacterDirectoryPath,"RecoveryPair","pending.json"),characterJson=CaptureCharacter(newActor)};
        }
        private static string CaptureCharacter(ActorState actor)=>(string)typeof(SaveStore).GetMethod("CaptureCharacterJson",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[] {actor});
        private static void Interrupt(Fixture fixture,bool afterCharacter,bool publishWorld=true)
        {
            var type=typeof(SaveStore).Assembly.GetType("Remains.Persistence.LocalRecoveryPair",true);
            var save=type.GetMethod("Save",BindingFlags.NonPublic|BindingFlags.Static);
            Action<string> writer=json=>{if(afterCharacter) AtomicSaveFile.Write(CharacterPath(fixture),json);throw new IOException("Контролируемое прерывание после durable intent.");};
            bool refused=false;
            try {save.Invoke(null,new object[] {fixture.store,fixture.newWorld,fixture.characterJson,publishWorld,writer});}
            catch(TargetInvocationException error) when(error.InnerException is IOException) {refused=true;}
            Require(refused,"Controlled publication failure missing");
        }
        private static void CheckRecovered(Fixture fixture)
        {
            var saved=fixture.store.SelectedSnapshot();var world=saved.CreateLiveWorld();var actor=StateJson.FromJson<ActorState>(fixture.store.ReadCharacterSource());
            Require(saved.Id==fixture.newWorld.Id && saved.Json==fixture.newWorld.Json && File.ReadAllText(CharacterPath(fixture))==fixture.characterJson &&
                actor.id==fixture.newActor.id && actor.character==fixture.newActor.character && actor.x==10.4f && actor.inventory[0].count==4 &&
                actor.inventoryAliases["item:source-caps"]=="item:personal-caps" && actor.inventory[0].extensions["pair-mod"]["data"][0].Value<decimal>()==decimal.MaxValue &&
                world.containers[0].items.Count==0 && world.pendingGrants.Count==0 && world.completedTransactions.Count==1 && world.completedTransactions.Contains("transaction:local-pair") &&
                !File.Exists(fixture.pending),"Recovered pair changed payload/receipt/identity or granted again");
        }
        private static void CheckRefusal(string root,string name,Action<Fixture> damage)
        {
            var fixture=Create(Path.Combine(root,name));Interrupt(fixture,false);damage(fixture);
            string pending=File.ReadAllText(fixture.pending),character=File.ReadAllText(CharacterPath(fixture)),world=File.ReadAllText(Path.Combine(fixture.store.WorldDirectoryPath,"world.json"));
            var fresh=new SaveStore(fixture.store.CharacterDirectoryPath,fixture.store.WorldDirectoryPath);
            Refuse(()=>fresh.RecoverPendingPair(),"Damaged/foreign/modified intent accepted: "+name);
            Require(File.ReadAllText(fixture.pending)==pending && File.ReadAllText(CharacterPath(fixture))==character &&
                File.ReadAllText(Path.Combine(fixture.store.WorldDirectoryPath,"world.json"))==world,"Refused recovery changed source data: "+name);
            Refuse(()=>fresh.SaveCharacter(fixture.oldActor),"Standalone write overwrote pending pair: "+name);
        }
        private static void ChangeIntent(Fixture fixture,Action<JObject> change)
        {var json=JObject.Parse(File.ReadAllText(fixture.pending));change(json);File.WriteAllText(fixture.pending,json.ToString());}
        private static string CharacterPath(Fixture fixture)=>Path.Combine(fixture.store.CharacterDirectoryPath,"character.json");
        private static void Refuse(Action action,string reason)
        {
            bool refused=false;try {action();} catch(Exception error) when(error is InvalidDataException || error is InvalidOperationException ||
                error is Newtonsoft.Json.JsonException || error is TargetInvocationException {InnerException:InvalidDataException}) {refused=true;}
            Require(refused,reason);
        }
        private static void Require(bool valid,string reason) {if(!valid) throw new InvalidOperationException("Local recovery pair: "+reason);}
    }
}
