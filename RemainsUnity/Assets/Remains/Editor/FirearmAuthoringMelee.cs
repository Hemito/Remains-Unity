using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // The original's melee weapons (AllData weapon tip 1, WClub), every grade with its own item image:
    //   numbers   damage, tipdam, rapid frames → the swing's duration (and the pause between swings), phis long → the blade;
    //   swing     WClub.actions: the weapon hangs at the aim within Pers.meleeS + meleeR (40 + 100 px) and its blade turns
    //             around that point. t_attack runs rapid frames: the first sixth winds back, the contact lasts to the half,
    //             the rest returns. mtip 0 swings (anim −1/4 → 1, rot = −90° + (−30° + anim·180°)·storona, y down; the held
    //             point is 0.8 blade behind and 0.3 below the aim), mtip 1 thrusts (the blade along the aim pushed by
    //             anim·atDlina, 100 px; held at the aim);
    //   artwork   frame 1 of the appearance, drawn from its registration along the blade;
    //   sounds    snd shoot as the swing (gain 0.5–1, Snd.ps in WClub).
    // Not carried over: contact along the blade from minlong (only its end touches), the charged blow (pow) and the
    // fourth-blow combo, knock and destroy, the cut/poison effects (dop), the pivot following the aim during a swing.
    // Listed as gaps: mtip 2 (saws), energy blades (vis lasm), alicorn weapons.
    public static partial class FirearmAuthoring
    {
        private const float MeleeHoldPixels=40+100,ThrustPixels=100;

        private static string Melee(string id,string wid,int grade,XElement root,JObject[] graphics,JObject[] sounds,
            ExportedContentCatalog exported,SerializedProperty list,List<JObject> audio,HashSet<string> assets)
        {
            var character=Merged(root,"char",grade);var phis=Merged(root,"phis",grade);var vis=Merged(root,"vis",grade);
            int mtip=(int)Num(root,"mtip",0);
            if(mtip is not (0 or 1)) return $"особый способ удара (mtip {mtip})";
            if(Num(vis,"lasm",0)>0) return "энергоклинок (vis lasm)";
            if(root.Attribute("alicorn")!=null) return "оружие аликорна";
            string Binding(JObject[] bindings,string role)=>bindings.FirstOrDefault(b=>(string)b["role"]==role)?["resource"]?.ToString();
            string appearance=Binding(graphics,"appearance-grade-"+grade);
            if(appearance==null || Binding(graphics,"item-image-grade-"+grade)==null) return "нет вида или изображения предмета этого грейда";
            if(!exported.TryItem(id,out _)) return "предмет не импортирован";
            string swingSound=Binding(sounds,"shoot");
            if(swingSound==null) return "нет звука взмаха";
            Sprite[] frames;
            try {frames=Frames(exported,appearance);}
            catch(InvalidDataException error) {return error.Message;}
            float blade=Num(phis,"long",0)/PixelsPerMetre,rapid=Num(character,"rapid",0),damage=Num(character,"damage",0);
            if(blade<=0 || rapid<=0 || damage<=0) return "нет длины клинка, темпа или урона";

            // anim at the key frames of t_attack (share of the swing): 0 → −1/4 at 1/6 → 1 at 1/2 → 0 at the end.
            float[] times={0,1f/6,.5f,1},anim={0,-.25f,1,0};
            var keys=times.Select((time,i)=>mtip==0
                // rot = −90° + (−30° + anim·180°)·storona in y down: 120° − anim·180° from the aim, y up.
                ?new MeleePoseKey {time=time,angle=120-anim[i]*180,reach=1}
                :new MeleePoseKey {time=time,angle=0,reach=Mathf.Max(0,1+anim[i]*ThrustPixels/PixelsPerMetre/blade)}).ToArray();
            float hold=MeleeHoldPixels/PixelsPerMetre;
            var definition=new WeaponDefinition
            {
                id=id,damageType=DamageTypes.FromOriginal((int)Num(character,"tipdam",0)),damage=damage,cooldown=rapid/FramesPerSecond,range=blade,
                meleeContactStart=1f/6,meleeContactEnd=.5f,meleeMotion=keys,meleeAroundHold=true,
                meleeAimOffset=mtip==0?new Vector2(-.8f,-.3f):Vector2.zero,
                recoil=WeaponRecoilProfile.Disabled(),holdingArea=new HoldingArea(hold,hold),stopAtFirstActor=false,stopAtTerrain=true
            };

            string file=Safe(wid)+"-"+grade;
            var view=Prefab(id,file,frames[0],null,0,assets);
            if(view.ReadRig().HasMuzzle) return "у ближнего оружия получилось дуло";
            string assetPath=WeaponFolder+"/"+file+".asset";assets.Add(assetPath);
            var asset=AssetDatabase.LoadAssetAtPath<WeaponAsset>(assetPath);bool fresh=asset==null;
            if(fresh) asset=ScriptableObject.CreateInstance<WeaponAsset>();
            asset.SetDefinition(definition);asset.SetPresentation(view);asset.SetShotAnimation(null);asset.SetReloadAnimation(null);asset.SetCasingProfile(null);
            if(fresh) AssetDatabase.CreateAsset(asset,assetPath);else EditorUtility.SetDirty(asset);
            bool listed=false;
            for(int i=0;i<list.arraySize;i++) listed|=list.GetArrayElementAtIndex(i).objectReferenceValue==asset;
            if(!listed) {list.arraySize++;list.GetArrayElementAtIndex(list.arraySize-1).objectReferenceValue=asset;}
            audio.Add(new JObject {["weapon"]=id,["clip"]=AudioClipSource.ExportedPrefix+swingSound,["kind"]="melee",["minGain"]=.5f,["maxGain"]=1});
            return null;
        }
    }
}
