using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class OriginalTrainingTargetsAuthoring
    {
        public static void Import()
        {
            const string prefabPath="Assets/Remains/RoomDrafts/OriginalTrainingRoom.prefab";
            const string texturePath="Assets/Remains/Resources/Original/Training/armored-dummy.png";
            const string spritePath="Assets/Remains/RoomDrafts/ArmoredTrainingSprite.asset";
            const string materialPath="Assets/Remains/RoomDrafts/ArmoredTrainingMaterial.mat";
            var research=OriginalTrainingRoomSource.Research;
            var source=OriginalTrainingRoomSource.Read();
            var targets=source.Elements("obj").Where(o=>(string)o.Attribute("id")=="training").ToArray();
            if(targets.Length!=5 || targets.Count(t=>(string)t.Attribute("tr")=="1")!=2)
                throw new InvalidOperationException("В исходном стрельбище ожидаются три обычные и две броневые мишени.");
            var root=PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                if(root.GetComponentsInChildren<RoomActorPlacement>(true).Length!=0 || File.Exists(spritePath) || File.Exists(texturePath))
                    throw new InvalidOperationException("Мишени уже импортированы; повторный импорт не должен стирать ручные правки.");
                File.Copy(Path.Combine(research,"training-armored-4x/DefineSprite_3265_visualTrainArmor/1.png"),texturePath);
                AssetDatabase.ImportAsset(texturePath);
                var importer=(TextureImporter)AssetImporter.GetAtPath(texturePath);
                importer.textureCompression=TextureImporterCompression.Uncompressed;importer.npotScale=TextureImporterNPOTScale.None;
                importer.mipmapEnabled=false;importer.alphaIsTransparency=true;importer.maxTextureSize=4096;importer.SaveAndReimport();
                var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                if(texture.width!=251 || texture.height!=354) throw new InvalidOperationException("Неверный размер прямого экспорта visualTrainArmor 4×.");
                var armored=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,0),200,0,SpriteMeshType.FullRect);
                AssetDatabase.CreateAsset(armored,spritePath);
                var material=WorldArtworkMaterial.Create(texture);AssetDatabase.CreateAsset(material,materialPath);
                var ordinary=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Remains/Prefabs/Actors/training-dummy.prefab").GetComponent<SpriteRenderer>();
                var group=new GameObject("Мишени — расстановка, игровая интеграция ещё не подключена");group.transform.SetParent(root.transform,false);
                foreach(var target in targets)
                {
                    bool armor=(string)target.Attribute("tr")=="1";
                    var part=new GameObject(armor?"Броневая мишень":"Обычная мишень");part.transform.SetParent(group.transform,false);
                    part.transform.localPosition=OriginalTrainingRoomSource.Position(source,target);
                    var view=part.AddComponent<SpriteRenderer>();view.sprite=armor?armored:ordinary.sprite;
                    view.sharedMaterial=armor?material:ordinary.sharedMaterial;view.sortingOrder=20;
                    part.AddComponent<RoomActorPlacement>().Configure("training:original:"+(string)target.Attribute("code"),
                        armor?"training-armored-dummy":"training-dummy",(int)target.Attribute("turn"),target.Attribute("fix")!=null,view);
                }
                var room=root.GetComponent<RoomAuthoring>();
                var placements=root.GetComponentsInChildren<RoomActorPlacement>();
                if(placements.Select(p=>p.Read(room).id).Distinct().Count()!=5 || placements.Count(p=>p.FixedPlacement)!=3)
                    throw new InvalidOperationException("Расстановка потеряла ID или свойства закреплённых мишеней.");
                root.name="Оригинальное стрельбище — геометрия и расстановка мишеней";
                PrefabUtility.SaveAsPrefabAsset(root,prefabPath);AssetDatabase.SaveAssets();
                Debug.Log("Стрельбище: пять уникальных мишеней, две броневые, три закреплённые. Пока только расстановка и предпросмотр; игровая интеграция и защита броневой мишени не подключены.");
            }
            finally {root.GetComponent<RoomLighting>()?.Release();PrefabUtility.UnloadPrefabContents(root);}
            RenderSaved();
        }
        public static void RenderSaved()
        {
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,UnityEditor.SceneManagement.NewSceneMode.Single);
            var root=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Remains/RoomDrafts/OriginalTrainingRoom.prefab"));
            try
            {
                var room=root.GetComponent<RoomAuthoring>();
                var targets=root.GetComponentsInChildren<RoomActorPlacement>();
                var placements=targets.Select(p=>p.Read(room)).ToArray();
                if(placements.Length!=5 || placements.Select(p=>p.id).Distinct().Count()!=5 ||
                    placements.Count(p=>p.type=="training-armored-dummy")!=2 || targets.Count(p=>p.FixedPlacement)!=3)
                    throw new InvalidOperationException("Сохранённый префаб потерял мишени или их свойства.");
                var layout=room.CreateLayout();
                var authoredLight=root.GetComponent<RoomLightingAuthoring>();
                if(authoredLight==null) Debug.LogWarning("Черновик стрельбища без авторского света: рендер выполняется с явно белым проверочным освещением.");
                var profile=authoredLight!=null?authoredLight.CreateProfile(room):new RoomLightProfile {ambient=Color.white,lights=Array.Empty<RoomLight>()};
                root.AddComponent<RoomLighting>().Initialize(layout,profile);
                OriginalTrainingRoomAuthoring.Render(root,layout);
                Debug.Log("Проверена сохранённая расстановка пяти мишеней и выполнен рендер 3840×2100.");
            }
            finally {root.GetComponent<RoomLighting>()?.Release();UnityEngine.Object.DestroyImmediate(root);}
        }
    }
}
