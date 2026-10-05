using System;
using System.IO;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Persistence;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Editor
{
    public static class PonyAppearanceChecks
    {
        private static void Require(bool condition,string detail)
        {if(!condition) throw new InvalidOperationException("Внешность пони: "+detail);}
        public static void Run()
        {
            WeaponCatalog.Load();Anatomy.Load();PonyVisual.Prepare();
            for(int hair=0;hair<8;hair++)
            {
                var legacy=new ActorState {hair=hair};var key=PonyAppearance.Snapshot(legacy);
                foreach(string slot in new[]{"fur","hair","hairSecondary","eye","magic","none"})
                    Require(PonyAppearance.Tint(slot,key)==PonyAppearance.Tint(slot,hair),"старый цвет изменён: "+hair+"/"+slot);
            }
            var actor=new ActorState {id="appearance-first",pose="idle",appearance=new CharacterAppearance {version=1,hairStyle=5,eyeStyle=6,fur=0xCCBB88,hair=0x551188,secondaryHair=0xEE2288,eyes=0x2288FF,magic=0xFFAA00,secondaryHairEnabled=true}};
            var copy=actor.Copy();copy.appearance.hair=0x00AABB;
            Require(actor.appearance.hair==0x551188 && copy.appearance.hair==0x00AABB,"копии внешности разделяют изменяемое состояние");
            CheckInvalid(new CharacterAppearance());CheckInvalid(new CharacterAppearance {version=2});CheckInvalid(new CharacterAppearance {version=1,hairStyle=6});
            CheckInvalid(new CharacterAppearance {version=1,eyeStyle=0});CheckInvalid(new CharacterAppearance {version=1,fur=-1});CheckInvalid(new CharacterAppearance {version=1,magic=0x1000000});
            var first=(PonyVisual)ActorVisualFactory.Create(actor);copy.id="appearance-second";
            var second=(PonyVisual)ActorVisualFactory.Create(copy);
            var page=UnityEngine.Object.Instantiate(Resources.Load<PersonalInventoryView>("Presentation/PersonalInventory"));
            try
            {
                first.Bind(actor);second.Bind(copy);first.DrawPose(0);second.DrawPose(0);
                string unchanged=Signature(second);int structure=first.transform.childCount;
                var portrait=new InventoryPortrait(page.Portrait,page.PortraitParts);portrait.Refresh(actor);
                string before=Signature(first);var uiBefore=page.PortraitParts.Select(p=>p.color).ToArray();
                actor.appearance.hair=0x44BB22;first.DrawPose(0);portrait.Refresh(actor);
                Require(before!=Signature(first) && !uiBefore.SequenceEqual(page.PortraitParts.Select(p=>p.color)),"неподвижный кадр/портрет не обновил цвет");
                Require(unchanged==Signature(second),"изменение первого персонажа изменило второго");
                var camera=PonyVisual.CameraBounds(actor);
                for(int hair=1;hair<=5;hair++) for(int eyes=1;eyes<=6;eyes++) for(int secondary=0;secondary<2;secondary++)
                {
                    actor.appearance.hairStyle=hair;actor.appearance.eyeStyle=eyes;actor.appearance.secondaryHairEnabled=secondary==1;
                    var key=PonyAppearance.Snapshot(actor);portrait.Refresh(actor);
                    foreach(var clip in PonyAnimationData.Library.clips)
                    {
                        actor.pose=clip.id;first.DrawPose(.14f,true);
                        Require(first.transform.childCount==structure,"во время выбора формы пересоздана структура персонажа");
                        var frames=PonyRenderSelection.Frames(key,clip.id);
                        foreach(var frame in frames)
                        {
                            Require(frame.Length<=PonyRenderSelection.Capacity && frame.Count(p=>p.slot=="eye")==1,"неверная вместимость или число радужек");
                            Require(secondary==1 || frame.All(p=>p.slot!="hairSecondary"),"второй цвет показан при выключенной настройке");
                        }
                    }
                    Require(PonyVisual.CameraBounds(actor)==camera,"смена формы/кадра изменила композицию камеры");
                }
                Require(unchanged==Signature(second),"перебор вариантов изменил другой экземпляр");
                string directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/tests/appearance-persistence",Guid.NewGuid().ToString("N")));
                var store=new SaveStore(directory);actor.character="appearance-persistence";actor.loadoutVersion=1;actor.weaponInventoryVersion=1;
                store.SaveCharacter(actor);var loaded=store.LoadCharacter();
                Require(loaded.id==actor.id && PonyAppearance.Snapshot(loaded).Equals(PonyAppearance.Snapshot(actor)),"локальный профиль потерял внешность или идентичность");
                var world=new WorldState();world.actors.Add(actor);store.SaveWorld(world);
                Require(PonyAppearance.Snapshot(store.LoadWorld().actors.Single()).Equals(PonyAppearance.Snapshot(actor)),"мир потерял внешность");
                Debug.Log($"Внешность пони: PASS — 60 сочетаний × {PonyAnimationData.Library.clips.Sum(c=>c.frames.Length)} кадров, старые цвета, независимые экземпляры, неподвижный кадр/UI, профиль/мир; вместимость={PonyRenderSelection.Capacity}.");
            }
            finally {UnityEngine.Object.DestroyImmediate(first.gameObject);UnityEngine.Object.DestroyImmediate(second.gameObject);UnityEngine.Object.DestroyImmediate(page.gameObject);}
        }
        private static void CheckInvalid(CharacterAppearance value)
        {
            try {PonyAppearance.Snapshot(new ActorState {id="проверка повреждённых данных",appearance=value});}
            catch(InvalidOperationException) {return;}
            throw new InvalidOperationException("Повреждённая внешность принята без ошибки.");
        }
        private static string Signature(PonyVisual visual)
        {
            var block=new MaterialPropertyBlock();
            return string.Join(";",visual.GetComponentsInChildren<MeshRenderer>().Where(r=>r.enabled).Select(r=>
            {r.GetPropertyBlock(block);return r.GetComponent<MeshFilter>().sharedMesh.GetInstanceID()+":"+block.GetColor("_Color");}));
        }
        public static void Render()
        {
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,UnityEditor.SceneManagement.NewSceneMode.Single);
            WeaponCatalog.Load();Anatomy.Load();PonyVisual.Prepare();
            for(int hair=1;hair<=5;hair++) for(int eyes=1;eyes<=6;eyes++)
            {
                var actor=new ActorState {id=$"appearance-{hair}-{eyes}",x=(eyes-1)*2.7f,y=-(hair-1)*2.6f,pose="idle",
                    appearance=new CharacterAppearance {version=1,hairStyle=hair,eyeStyle=eyes,fur=0xBBCCDD,hair=0x8855DD,secondaryHair=0xDD4488,eyes=0x66DD55,magic=0xDD77FF,secondaryHairEnabled=true}};
                var visual=(PonyVisual)ActorVisualFactory.Create(actor);visual.Bind(actor);visual.DrawPose(0);
            }
            var camera=new GameObject("Каталог внешности пони").AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=6.7f;
            camera.transform.position=new Vector3(6.7f,-4.6f,-10);camera.backgroundColor=new Color(.12f,.14f,.16f);
            Runtime.NetworkFrameCapture.Save(camera,Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/tests/pony-appearance-catalog.png")),width:2560,height:1440);
        }
    }
}
