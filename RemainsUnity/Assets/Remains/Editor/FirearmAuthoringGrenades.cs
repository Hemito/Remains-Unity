using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // The original's hand grenades with a plain blast (AllData weapon tip 4, WThrow; user 2026-11-01: all grenades, the
    // plain ones first): hgren, plagr, spgren, dinamit. The ID is the formal item ID (item:<id>) — the original keeps
    // thrown weapons as inventory items (WThrow.getAmmo), so loot, names and icons already match, and saved formal stacks
    // become working. Throwing and flight are the port's hand-made grenade's (its launch, bounce and spin, copied); from
    // the original come the blast (damexpl, expl px at 50 per metre, tipdam), the fuse (char time frames) and the art (the
    // projectile symbol in flight, the appearance in the hooves). The hand-made grenade keeps the original's own grenade.
    // Fire grenades (fgren, molotov) splash burning droplets (FireSplash, FireRules). Not yet: the fall sounds (snd fall),
    // the dynamite's fuse hiss (snd shoot dinamit_f), knock and destroy; grenades with gas, acid, ice and impulse.
    public static partial class FirearmAuthoring
    {
        internal static readonly string[] PlainGrenades={"dinamit","hgren","plagr","spgren"};
        // Burning droplets (FireSplash): the molotov (a bottle, bumc) and the incendiary grenade (a fuse, radial).
        internal static readonly string[] FireGrenades={"fgren","molotov"};
        private const int FireDroplets=15;private const float FirePatch=.35f,FirePulseFrames=10,BurningSeconds=7,ShatterSpeed=3,SureShatterSpeed=6;

        private static string Grenade(string wid,XElement root,JObject[] graphics,ExportedContentCatalog exported,WeaponDefinition baseGrenade,
            SerializedProperty list,List<JObject> audio,HashSet<string> assets)
        {
            string id="item:"+wid;
            var character=root.Element("char");
            if(character==null || root.Attribute("throwtip")!=null) return "не ручная граната (нет char или throwtip)";
            // Fire (tipdam 3, dop igni, pulses explkol) is splashed as burning droplets; other effects come later.
            bool fire=(string)root.Element("dop")?.Attribute("effect")=="igni" && (int)Num(character,"tipdam",0)==3;
            if(!fire && (character.Attribute("explkol")!=null || character.Attribute("expltip")!=null || root.Element("dop")!=null)) return "граната с эффектом";
            if(!exported.TryItem(id,out _)) return "предмет "+id+" не импортирован";
            string Binding(string role)=>graphics.FirstOrDefault(b=>(string)b["role"]==role)?["resource"]?.ToString();
            string appearance=Binding("appearance-grade-0"),projectile=Binding("projectile-grade-0");
            if(appearance==null || projectile==null) return "нет вида или вида в полёте";
            Sprite[] held,flying;
            try {held=Frames(exported,appearance);flying=Frames(exported,projectile);}
            catch(InvalidDataException error) {return error.Message;}

            var definition=StateJson.Copy(baseGrenade);
            definition.id=id;definition.damageType=DamageTypes.FromOriginal((int)Num(character,"tipdam",4));
            definition.damage=Num(character,"damexpl");definition.range=Num(character,"expl")/PixelsPerMetre;
            definition.grenadeFuseSeconds=Num(character,"time",75)/FramesPerSecond;
            if(fire)
            {
                // Bullet.explRun every explPeriod 10 frames, explkol times; Effect burning t=7 with dop damage a second.
                bool bottle=Num(root.Element("phis"),"bumc",0)>0;
                float reach=Mathf.Sqrt(definition.grenadeGravity*definition.range);
                definition.fire=new FireSplash {droplets=FireDroplets,spread=bottle?reach*.5f:reach,radial=!bottle,patchRadius=FirePatch,
                    pulseSeconds=FirePulseFrames/FramesPerSecond,pulses=(int)Num(character,"explkol"),pulseDamage=definition.damage,
                    igniteDamage=Num(root.Element("dop"),"damage",0),igniteSeconds=BurningSeconds,shatter=bottle,shatterSpeed=ShatterSpeed,sureShatterSpeed=SureShatterSpeed};
                // A bottle is lit when thrown (user 2026-11-02): no fuse, its "fuse" is its fuel burning from the neck.
                if(bottle) definition.grenadeFuseSeconds=FireRules.Fuel(definition.fire);
            }
            if(!definition.IsGrenade || definition.damage<=0 || definition.range<=0) return "нет взрыва или запала";

            string file="grenade-"+Safe(wid);
            var view=Prefab(id,file,held[0],null,0,assets);
            string flightPath=PrefabFolder+"/"+file+"-flight.prefab";assets.Add(flightPath);
            var node=new GameObject(id+" в полёте");SpriteRenderer flight;
            try
            {
                var renderer=node.AddComponent<SpriteRenderer>();renderer.sprite=flying[0];
                renderer.sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(PrefabFolder+"/"+file+".mat");
                flight=PrefabUtility.SaveAsPrefabAsset(node,flightPath).GetComponent<SpriteRenderer>();
            }
            finally {UnityEngine.Object.DestroyImmediate(node);}

            string assetPath=WeaponFolder+"/"+file+".asset";assets.Add(assetPath);
            var asset=AssetDatabase.LoadAssetAtPath<WeaponAsset>(assetPath);bool fresh=asset==null;
            if(fresh) asset=ScriptableObject.CreateInstance<WeaponAsset>();
            asset.SetDefinition(definition);asset.SetPresentation(view);asset.SetGrenadeFlight(flight);
            if(fresh) AssetDatabase.CreateAsset(asset,assetPath);else EditorUtility.SetDirty(asset);
            bool listed=false;
            for(int i=0;i<list.arraySize;i++) listed|=list.GetArrayElementAtIndex(i).objectReferenceValue==asset;
            if(!listed) {list.arraySize++;list.GetArrayElementAtIndex(list.arraySize-1).objectReferenceValue=asset;}
            audio.Add(new JObject {["weapon"]=id,["clip"]="grenade-explosion",["kind"]="explosion",["minGain"]=1,["maxGain"]=1});
            return null;
        }
    }
}
