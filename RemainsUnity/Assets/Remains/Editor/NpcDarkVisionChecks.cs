using System;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Sight by the light around a player (user 2026-11-06). Run: the rule, also in NpcPerceptionChecks. RunBatch: the real
    // light of seed104, which needs the graphics device: the place under a lamp must read lit, the dark places dark.
    public static class NpcDarkVisionChecks
    {
        public static void Run()
        {
            var perception=new NpcPerception(new NpcPerceptionSettings {sightDistance=20,closeDistance=1,recognitionSeconds=.3f,
                memorySeconds=4,hearingDistance=8,eyeHeight=.8f,targetHeight=.8f});
            var npc=new ActorState {id="npc",owner="o",facing=1,health=100};
            var pony=new ActorState {id="pony",player=true,connected=true,owner="o",x=15,health=100};
            bool Clear(Vector2 from,Vector2 to)=>true;
            Require(perception.CanSee(npc,pony,Clear),"без данных о свете зрение не полное");
            Require(perception.CanSee(npc,pony,Clear,_=>1),"на свету цель не видна на полной дальности");
            Require(!perception.CanSee(npc,pony,Clear,_=>0),"в темноте цель видна на полной дальности");
            pony.x=6;Require(perception.CanSee(npc,pony,Clear,_=>0),"в темноте не видно рядом");
            pony.x=9;Require(!perception.CanSee(npc,pony,Clear,_=>0),"пустые копыта в темноте видны слишком далеко");
            pony.equippedWeaponItem="pistol-item";Require(perception.CanSee(npc,pony,Clear,_=>0),"рог не выдаёт предмет в копытах");
            pony.equippedWeaponItem=null;pony.heldItemId="box";Require(perception.CanSee(npc,pony,Clear,_=>0),"рог не выдаёт вещь в телекинезе");
            pony.heldItemId=null;pony.x=.5f;
            Require(new NpcPerception(new NpcPerceptionSettings {sightDistance=2,closeDistance=1.5f,recognitionSeconds=.3f,memorySeconds=4,
                hearingDistance=8,eyeHeight=.8f,targetHeight=.8f}).CanSee(npc,pony,Clear,_=>0),"близкое обнаружение пропало в темноте");
            Require(Mathf.Approximately(NpcDarkVision.Share(0,false),NpcDarkVision.DarkShare) && NpcDarkVision.Share(1,false)==1 &&
                NpcDarkVision.Share(.6f,true)==1,"доля зрения вне своих границ");
            pony.x=15;var memory=new NpcPerceptionMemory();
            perception.Step(npc,memory,new[]{pony},Clear,1,_=>0);
            Require(npc.target==null,"шаг восприятия не учитывает темноту");
            perception.Step(npc,memory,new[]{pony},Clear,1,_=>1);
            Require(npc.target==pony.id,"шаг восприятия не видит на свету");
            Debug.Log("Зрение в темноте: PASS — правило дальности, рог, близкое обнаружение.");
        }
        public static void RunBatch()
        {
            GameObject visual=null;LevelLight light=null;
            try
            {
                Anatomy.Load();WeaponCatalog.Load();ActorTypes.Load();Run();
                var world=new WorldState {seed=104,mechanismVersion=1};
                FactoryPreparation.Create(world,AssetDatabase.LoadAssetAtPath<FactoryCatalogAsset>(FactoryCatalogAuthoring.AssetPath),0);
                FactoryContentSources.Prepare(world,"factory:0");
                var authoring=RoomAuthoring.Load("factory:0",world);
                RoomContentInstaller.Apply(world,RoomContentBatch.Read(authoring,"factory:0"));
                var room=authoring.CreateLayout(false);
                visual=AuthoredRoomVisual.Create("factory:0",room,world);
                var view=visual.GetComponent<FactoryLevelView>();
                view.Synchronize(room,world,1);
                light=view.CreateLevelLight(useGridFill:true);light.BuildAll();
                var lamps=light.Sources.All.Where(s=>s.Kind==LevelLightSourceKind.Lamp && s.Enabled).ToArray();
                var near=lamps.Select(s=>LevelBrightness.Read(light.Fill,s.Bounds.center+Vector2.down*1.5f)).OrderBy(v=>v).ToArray();
                float median=near[near.Length/2];
                Require(median>=NpcDarkVision.FullLight,$"под лампой темно: {median:F3}");
                var npcs=world.actors.Where(a=>!a.player).Select(a=>LevelBrightness.Read(light.Fill,a.Position+Vector2.up*.8f)).ToArray();
                float dark=(float)npcs.Count(v=>v<=0)/npcs.Length;
                Require(dark>0 && dark<1,$"сетка света однородна: тёмных мест {dark:P0}");
                Require(LevelBrightness.Read(light.Fill,new Vector2(-100,-100))==0,"вне уровня светло");
                Debug.Log($"Зрение в темноте: PASS — под лампой {median:F2}, тёмных мест NPC {dark:P0}.");
                EditorApplication.Exit(0);
            }
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
            finally {light?.Dispose();if(visual!=null) UnityEngine.Object.DestroyImmediate(visual);FactoryContentSources.Clear();}
        }
        private static void Require(bool value,string message) {if(!value) throw new InvalidOperationException("Зрение в темноте: "+message);}
    }
}
