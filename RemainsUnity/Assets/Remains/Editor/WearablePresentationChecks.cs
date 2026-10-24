using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using Remains.Serialization;
using UnityEngine;
namespace Remains.Editor
{
    public static class WearablePresentationChecks
    {
        private static void Require(bool condition,string detail)
        {if(!condition) throw new InvalidOperationException("Визуальное снаряжение: "+detail);}
        private static ActorState Actor(string name)=>new()
        {
            id="wearable-check:"+name,character=name,anatomy="pony",actorType="pony",player=true,connected=true,level="hub",weapon=null,
            appearance=new CharacterAppearance {version=1,hairStyle=5,eyeStyle=6,secondaryHairEnabled=true,fur=0xABCDEF,hair=0x884411,eyes=0x22FF44},
            inventory=new List<ItemStack> {new() {id="owned-pip",definition="armor.pip",count=1},new() {id="owned-antirad",definition="armor.antirad",count=1}}
        };
        public static void Run()
        {
            WeaponCatalog.Load();PonyVisual.Prepare();
            var baseline=GameAssets.Load<PonyRenderAsset>("Original/Pony/render");string sourceHash=PonyRenderAsset.SourceHash();
            var actor=Actor("первый");var other=Actor("второй");
            var view=(PonyVisual)ActorVisualFactory.Create(actor);var otherView=(PonyVisual)ActorVisualFactory.Create(other);
            var page=UnityEngine.Object.Instantiate(GameAssets.Load<PersonalInventoryView>("Presentation/PersonalInventory"));
            var cards=UnityEngine.Object.Instantiate(GameAssets.Load<CharacterLibraryView>("Presentation/CharacterLibrary"));
            int drawings=0,frameChecks=0,headWitnesses=0;
            try
            {
                view.Bind(actor);view.DrawPose(0);otherView.Bind(other);otherView.DrawPose(0);
                var portrait=new InventoryPortrait(page.Portrait,page.PortraitParts);page.PortraitParts=portrait.Parts;portrait.Refresh(actor);
                string bare=Signature(view),otherBefore=Signature(otherView);
                foreach(string item in new[]{"owned-pip","owned-antirad"})
                {
                    Require(CosmeticEquipment.TryEquip(actor,item,false,out var reason),"Не надет реальный экземпляр: "+reason);
                    actor.pose="idle";CheckPendingBind(view,actor);view.DrawPose(0);portrait.Refresh(actor);cards.Portraits[0].Show(actor,new InventoryArtwork());
                    var plan=view.CurrentDrawPlan;
                    if(item=="owned-pip") CheckPoolProvisioning(actor);
                    Require(plan.HasEquipment && Signature(view)!=bare,"Неподвижный кадр сохранил прежнюю графику после надевания");
                    Require(ReferenceEquals(plan,portrait.CurrentDrawPlan),"Мир и инвентарь используют разные подготовленные планы");
                    CheckPortrait(page.PortraitParts,plan.PortraitFrame,PonyAppearance.Snapshot(actor));
                    CheckPortrait(cards.Portraits[0].Parts,plan.PortraitFrame,PonyAppearance.Snapshot(actor));
                    var asset=WearableRenderCatalog.Assets.Single(value=>plan.VisualIds.Contains(value.VisualId));
                    Require(asset.BaseSourceHash==sourceHash,"Снаряжение изменило/подменило исходный профиль пони");
                    CheckNegative(asset);
                    string json=StateJson.ToJson(actor);var restored=StateJson.FromJson<ActorState>(json);
                    var restoredPlan=PonyRenderSelection.PrepareEquipment(restored);
                    Require(restoredPlan.VisualIds.SequenceEqual(plan.VisualIds),"Сериализация потеряла выбор рисунка");
                    for(int hair=1;hair<=5;hair++) for(int eye=1;eye<=6;eye++) for(int secondary=0;secondary<2;secondary++)
                    {
                        actor.appearance.hairStyle=hair;actor.appearance.eyeStyle=eye;actor.appearance.secondaryHairEnabled=secondary==1;
                        view.Bind(actor);var selected=view.CurrentDrawPlan;
                        foreach(var clip in baseline.Clips)
                        {
                            var frames=selected.Frames(clip.id);var declarations=asset.Clips.Single(value=>value.id==clip.id);
                            for(int f=0;f<frames.Length;f++)
                            {
                                frameChecks++;var parts=frames[f];var hidden=new HashSet<int>(declarations.frames[f].hiddenBasePartIndices);
                                Require(parts.All(part=>part.sourceIndex<0 || !hidden.Contains(part.sourceIndex)),"Показана объявленная скрытая часть: "+clip.id+"/"+f);
                                Require(parts.Length<=PonyRenderSelection.Capacity,"Недостаточна сохранённая вместимость");
                                if(item=="owned-antirad") Require(parts.All(part=>part.sourceIndex<0 || !part.sourcePath.StartsWith("mane/",StringComparison.Ordinal)),"Не скрыта исходная грива");
                            }
                        }
                    }
                    actor.appearance.hairStyle=5;actor.appearance.eyeStyle=6;actor.appearance.secondaryHairEnabled=true;view.Bind(actor);
                    plan=view.CurrentDrawPlan;int structure=view.transform.childCount;
                    foreach(var clip in PonyAnimationData.Library.clips) for(int f=0;f<clip.frames.Length;f++) foreach(int facing in new[]{-1,1})
                    {
                        actor.pose=clip.id;actor.facing=facing;actor.Position=new Vector2(2,3);
                        bool cycleWithoutIntro=!clip.nonLooping && !clip.playIntro && clip.loopStartFrame>0;
                        actor.climbing=clip.id=="climb";actor.vy=actor.climbing?1:0;
                        if(cycleWithoutIntro && f<clip.loopStartFrame)
                        {
                            Require(actor.climbing && f==0,"Нет declared stationary входа для кадра: "+clip.id+"/"+f);
                            actor.vy=0;actor.poseTime=0;
                        }
                        else actor.poseTime=(f-(cycleWithoutIntro?clip.loopStartFrame:0)+.1f)/clip.fps;
                        view.DrawPose(actor.poseTime,true);drawings++;
                        Require(view.DrawnClip==clip.id && view.DrawnFrame==f,$"Проверка не показала запрошенный фактический кадр: requested={clip.id}/{f}, actual={view.DrawnClip}/{view.DrawnFrame}, time={actor.poseTime:R}, fps={clip.fps}, loopStart={clip.loopStartFrame}, playIntro={clip.playIntro}, climbing={actor.climbing}, vy={actor.vy:R}");
                        CheckWorld(view,actor,plan.Frames(clip.id)[f]);
                        if(plan.Frames(clip.id)[f].Any(part=>part.sourcePath.StartsWith("head/",StringComparison.Ordinal) && part.sourceIndex<0)) headWitnesses++;
                    }
                    Require(view.transform.childCount==structure,"Движение перестроило экземпляр пони");
                    Require(Signature(otherView)==otherBefore,"Экипировка изменила другого персонажа");
                    Require(PonyRenderAsset.SourceHash()==sourceHash,"Изменён базовый ресурс пони");
                    Require(CosmeticEquipment.TryEquip(actor,item,true,out reason),"Не снят предмет: "+reason);
                    actor.pose="idle";actor.climbing=false;actor.vy=0;actor.facing=1;actor.Position=Vector2.zero;CheckPendingBind(view,actor);view.DrawPose(0);portrait.Refresh(actor);
                    Require(!view.CurrentDrawPlan.HasEquipment && Signature(view)==bare,"Снятие не вернуло базовый рисунок");
                }
                Require(headWitnesses>0,"Нет реально отрисованного анимированного head replacement");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(view.gameObject);UnityEngine.Object.DestroyImmediate(otherView.gameObject);
                UnityEngine.Object.DestroyImmediate(page.gameObject);UnityEngine.Object.DestroyImmediate(cards.gameObject);
            }
            Debug.Log($"Визуальное снаряжение: PASS — кадров/форм={frameChecks}, фактических рисунков={drawings}, head witnesses={headWitnesses}; мир/инвентарь/карточки, надеть/снять в idle, неизменный source и структура.");
        }
        private static void CheckPendingBind(PonyVisual view,ActorState actor)
        {
            Require(view.ActorId==actor.id,"Проверка pending Bind требует прежний actor ID");
            string clip=view.DrawnClip;int frame=view.DrawnFrame,partCount=view.DrawnPartCount;float time=view.DrawnTime;
            var position=view.transform.position;var scale=view.transform.localScale;
            bool hasBounds=view.TryGetDrawnBounds(out var bounds);
            var block=new MaterialPropertyBlock();
            var actual=view.GetComponentsInChildren<MeshRenderer>(true).Select(renderer=>
            {
                renderer.GetPropertyBlock(block);
                return new {renderer,mesh=renderer.GetComponent<MeshFilter>().sharedMesh,material=renderer.sharedMaterial,
                    renderer.enabled,order=renderer.sortingOrder,color=block.GetColor("_Color"),emission=block.GetFloat("_Emission")};
            }).ToArray();
            view.Bind(actor);
            Require(view.DrawnClip==clip && view.DrawnFrame==frame && view.DrawnTime==time && view.DrawnPartCount==partCount &&
                view.transform.position==position && view.transform.localScale==scale && view.TryGetDrawnBounds(out var pending)==hasBounds && (!hasBounds || pending==bounds),
                "Подготовка надевания/снятия изменила свидетельство прежнего рисунка до DrawPose");
            foreach(var previous in actual)
            {
                var renderer=previous.renderer;renderer.GetPropertyBlock(block);
                Require(renderer.enabled==previous.enabled && renderer.GetComponent<MeshFilter>().sharedMesh==previous.mesh &&
                    renderer.sharedMaterial==previous.material && renderer.sortingOrder==previous.order &&
                    block.GetColor("_Color")==previous.color && block.GetFloat("_Emission")==previous.emission,
                    "Bind изменил фактическую часть до DrawPose: "+renderer.name);
            }
            Require(view.GetComponentsInChildren<MeshRenderer>(true).Except(actual.Select(value=>value.renderer)).All(renderer=>!renderer.enabled),
                "Расширение готового пула показало новую часть до DrawPose");
        }
        // Also used by the separate cold AssetBundle/content-pack check after its
        // registrations. It never changes the frozen registry or authored assets.
        public static void CheckPoolProvisioning(ActorState selected)
        {
            var bare=selected.Copy();bare.cosmeticEquipment=null;bare.pose="idle";
            var equipped=selected.Copy();equipped.pose="idle";
            var selectedPlan=PonyRenderSelection.PrepareEquipment(equipped);
            var barePlan=PonyRenderSelection.PrepareEquipment(bare);
            Require(selectedPlan.HasEquipment && selectedPlan.Capacity>barePlan.Capacity,"Нет настоящего более вместительного compiled plan для проверки provision");
            var view=(PonyVisual)ActorVisualFactory.Create(equipped);
            var broken=(PonyVisual)ActorVisualFactory.Create(equipped);
            var page=UnityEngine.Object.Instantiate(GameAssets.Load<PersonalInventoryView>("Presentation/PersonalInventory"));
            try
            {
                var data=new UnityEditor.SerializedObject(view);var filters=data.FindProperty("filters");var renderers=data.FindProperty("renderers");
                Require(filters!=null && renderers!=null && filters.arraySize>=2 && filters.arraySize==renderers.arraySize,"Нет исходного authored pool для проверки");
                var preserved=new[]{(MeshFilter)filters.GetArrayElementAtIndex(0).objectReferenceValue,(MeshFilter)filters.GetArrayElementAtIndex(1).objectReferenceValue};
                var removed=new List<GameObject>();
                for(int i=2;i<filters.arraySize;i++) removed.Add(((MeshFilter)filters.GetArrayElementAtIndex(i).objectReferenceValue).gameObject);
                filters.arraySize=renderers.arraySize=2;data.ApplyModifiedPropertiesWithoutUndo();
                foreach(var child in removed) UnityEngine.Object.DestroyImmediate(child);
                view.Bind(bare);view.DrawPose(0);
                Require(view.GetComponentsInChildren<MeshFilter>(true).Length==barePlan.Capacity,"Обычный пони раздувается до глобального максимума незадействованных модов");
                view.Bind(equipped);view.DrawPose(0);CheckWorld(view,equipped,selectedPlan.PortraitFrame);
                int worldCount=view.transform.childCount;
                Require(view.GetComponentsInChildren<MeshFilter>(true).Length==selectedPlan.Capacity && preserved.All(value=>value!=null && value.transform.IsChildOf(view.transform)),"Выбранный plan не расширил пул или заменил сохранённые ссылки");
                var bad=new UnityEditor.SerializedObject(broken);bad.FindProperty("filters").GetArrayElementAtIndex(0).objectReferenceValue=null;bad.ApplyModifiedPropertiesWithoutUndo();
                int badCount=broken.transform.childCount;bool rejected=false;
                try {broken.Bind(equipped);}catch(InvalidOperationException){rejected=true;}
                Require(rejected && broken.transform.childCount==badCount,"Повреждённая authored ссылка была молча восстановлена/grown");

                var first=page.PortraitParts[0];var second=page.PortraitParts[1];
                for(int i=2;i<page.PortraitParts.Length;i++) UnityEngine.Object.DestroyImmediate(page.PortraitParts[i].gameObject);
                var portrait=new InventoryPortrait(page.Portrait,new[]{first,second});page.PortraitParts=portrait.Parts;
                Require(ReferenceEquals(portrait.Parts[0],first) && ReferenceEquals(portrait.Parts[1],second) && portrait.Parts.Length>=selectedPlan.Capacity,"Startup provision портрета заменил refs или не вместил plan");
                portrait.Refresh(equipped);CheckPortrait(portrait.Parts,selectedPlan.PortraitFrame,PonyAppearance.Snapshot(equipped));
                int portraitCount=page.Portrait.transform.childCount;
                for(int i=0;i<100;i++) {view.Bind(equipped);view.DrawPose(0);portrait.Refresh(equipped);}
                Require(view.transform.childCount==worldCount && page.Portrait.transform.childCount==portraitCount,"Повторные Bind/Draw/Refresh создают дополнительные nodes");
            }
            finally
            {UnityEngine.Object.DestroyImmediate(view.gameObject);UnityEngine.Object.DestroyImmediate(broken.gameObject);UnityEngine.Object.DestroyImmediate(page.gameObject);}
        }
        private static void CheckPortrait(PortraitPart[] pool,PonyRenderPart[] parts,PonyAppearanceKey appearance)
        {
            Require(pool.Count(part=>part.gameObject.activeSelf)==parts.Length,"Портрет показал другое число частей");
            for(int i=0;i<parts.Length;i++)
            {
                Require(pool[i].mainTexture==parts[i].material.mainTexture && pool[i].color==parts[i].baseColor*PonyAppearance.Tint(parts[i].slot,appearance),"Портрет расходится с world part: "+i);
                for(int p=0;p<4;p++) Require(pool[i].PreparedPoint(p)==parts[i].points[p],"Портрет получил другую регистрацию вершины: "+i+"/"+p);
            }
        }
        private static void CheckWorld(PonyVisual view,ActorState actor,PonyRenderPart[] parts)
        {
            var renderers=view.GetComponentsInChildren<MeshRenderer>(true);var enabled=renderers.Where(value=>value.enabled).OrderBy(value=>value.sortingOrder).ToArray();
            Require(enabled.Length==parts.Length && view.DrawnPartCount==parts.Length,"Фактический renderer не соответствует подготовленному составу");
            bool found=false;Bounds expected=default;var block=new MaterialPropertyBlock();
            for(int i=0;i<parts.Length;i++)
            {
                var renderer=enabled[i];renderer.GetPropertyBlock(block);
                Require(renderer.GetComponent<MeshFilter>().sharedMesh==parts[i].mesh && renderer.sharedMaterial==parts[i].material,"Фактический renderer подменил part/material");
                Require(block.GetColor("_Color")==PonyAppearance.Tint(parts[i].slot,PonyAppearance.Snapshot(actor)),"Фактический tint неверен");
                foreach(var point in parts[i].points)
                {
                    var world=actor.BodyPosition+new Vector2(point.x*actor.facing,point.y);
                    if(!found) {expected=new Bounds(new Vector3(world.x,world.y,0),Vector3.zero);found=true;}else expected.Encapsulate(new Vector3(world.x,world.y,0));
                }
            }
            Require(view.TryGetDrawnBounds(out var actual) && Mathf.Abs(actual.min.x-expected.min.x)<.00002f && Mathf.Abs(actual.max.x-expected.max.x)<.00002f &&
                Mathf.Abs(actual.min.y-expected.min.y)<.00002f && Mathf.Abs(actual.max.y-expected.max.y)<.00002f,"Actual bounds не включают полный рисунок с BodyPosition/facing");
        }
        private static string Signature(PonyVisual view)=>string.Join(";",view.GetComponentsInChildren<MeshRenderer>(true).Where(value=>value.enabled).OrderBy(value=>value.sortingOrder).Select(value=>value.GetComponent<MeshFilter>().sharedMesh.name));
        private static void CheckNegative(WearableRenderAsset asset)
        {
            void Invalid(Action<WearableRenderAsset> corrupt,string label)
            {
                var clone=UnityEngine.Object.Instantiate(asset);
                try {corrupt(clone);try {clone.Validate(PonyAnimationData.Library,PonyRenderAsset.SourceHash(),"negative:"+label);}catch(InvalidOperationException){return;}throw new InvalidOperationException("Принят повреждённый банк: "+label);}
                finally {UnityEngine.Object.DestroyImmediate(clone);}
            }
            Invalid(clone=>clone.Clips[0].frames[0].hiddenBasePartIndices=new[]{int.MaxValue},"hide ID");
            Invalid(clone=>clone.Clips[0].frames=Array.Empty<WearableRenderFrame>(),"missing frames");
            Invalid(clone=>clone.Clips[0].frames[0].baseOrder[0].path=null,"missing source order");
            Invalid(clone=>clone.Clips[0].frames[0].parts[0].slot="armor.head","tint/equipment slot confusion");
            Invalid(clone=>clone.Configure(clone.VisualId,clone.Anatomy,clone.SourceHash,"stale",clone.ContentFingerprint,clone.Clips),"stale base hash");
            Invalid(clone=>clone.Clips[0].frames[0].parts[0].points[0]+=Vector3.right,"portrait/world vertex mismatch");
            Invalid(clone=>clone.Clips[0].frames[0].parts[0].baseColor=Color.clear,"portrait/world color mismatch");
            Invalid(clone=>clone.Clips[0].frames[0].bounds=new Bounds(Vector3.zero,new Vector3(float.NaN,1,0)),"nonfinite frame bounds");
            Invalid(clone=>clone.Clips[0].frames[0].bounds=new Bounds(Vector3.zero,new Vector3(10000,10000,0)),"inflated camera bounds");
            Mesh changed=null;
            try
            {
                Invalid(clone=>
                {
                    var part=clone.Clips[0].frames[0].parts[0];changed=UnityEngine.Object.Instantiate(part.mesh);part.mesh=changed;
                    var uv=changed.uv;uv[0]=Vector2.one;changed.uv=uv;
                },"compiled UV mismatch");
            }
            finally {if(changed!=null) UnityEngine.Object.DestroyImmediate(changed);}
        }
        public static void Render()
        {
            WeaponCatalog.Load();PonyVisual.Prepare();
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,UnityEditor.SceneManagement.NewSceneMode.Single);
            int x=0;var views=new List<PonyVisual>();
            foreach(string item in new[]{"owned-pip","owned-antirad"}) foreach(string pose in new[]{"idle","walk","climb","slopeWalkUp"})
            {
                var actor=Actor("render"+x);CosmeticEquipment.TryEquip(actor,item,false,out _);actor.x=(x%4)*3;actor.y=-(x/4)*3;actor.pose=pose;actor.poseTime=.2f;
                var view=(PonyVisual)ActorVisualFactory.Create(actor);view.Bind(actor);view.DrawPose(actor.poseTime,true);views.Add(view);x++;
            }
            var camera=new GameObject("Визуальное снаряжение — рисунки").AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=3.5f;
            camera.aspect=1920f/1080;
            camera.transform.position=new Vector3(4.5f,-.5f,-10);camera.backgroundColor=new Color(.12f,.14f,.16f);
            foreach(var view in views)
            {
                Require(view.TryGetDrawnBounds(out var bounds),"Нет actual bounds для обзорного кадра: "+view.ActorId);
                foreach(var point in new[]{bounds.min,bounds.max})
                {
                    var viewport=camera.WorldToViewportPoint(point);
                    Require(viewport.z>0 && viewport.x>=0 && viewport.x<=1 && viewport.y>=0 && viewport.y<=1,"Обзорный кадр обрезает снаряжение: actor="+view.ActorId+", viewport="+viewport);
                }
            }
            string directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/tests/wearable-presentation"));Directory.CreateDirectory(directory);
            Runtime.NetworkFrameCapture.Save(camera,Path.Combine(directory,"suits-1920x1080.png"),width:1920,height:1080);
        }
    }
}
