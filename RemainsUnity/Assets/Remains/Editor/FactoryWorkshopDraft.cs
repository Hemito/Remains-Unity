using System;
using System.IO;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class FactoryWorkshopDraft
    {
        public const string Path="Assets/Remains/RoomDrafts/FactoryWorkshop18.prefab";
        [Serializable] private sealed class Source
        {public int sourceIndex;public string sourceId,sourceHash;public RoomSourceRecord[] records;public Lamp[] lights;}
        [Serializable] private sealed class Lamp {public string recordId,kind;public float x,top;}
        public static void Import()
        {
            if(AssetDatabase.LoadMainAssetAtPath(Path)!=null) throw new InvalidOperationException("Цех уже импортирован; авторские правки не заменены.");
            AssetDatabase.Refresh();
            foreach(string file in Directory.GetFiles("Assets/Remains/Resources/Original/Room/Factory18"))
            {
                if(AssetImporter.GetAtPath(file) is not TextureImporter texture) continue;
                texture.maxTextureSize=8192;texture.npotScale=TextureImporterNPOTScale.None;texture.textureCompression=TextureImporterCompression.Uncompressed;
                texture.mipmapEnabled=false;texture.alphaIsTransparency=true;texture.SaveAndReimport();
            }
            T Read<T>(string name)=>JsonUtility.FromJson<T>(File.ReadAllText(System.IO.Path.Combine(OriginalTrainingRoomSource.Research,name)));
            var layout=Read<RoomLayout>("factory-18-geometry.json");var source=Read<Source>("factory-18-objects.json");
            if(source.sourceIndex!=18 || source.records.Length!=24 || source.lights.Length!=2) throw new InvalidOperationException("Неожиданный источник цеха.");
            var root=new GameObject("Цех1 · исходный шаблон 18");
            try
            {
                var room=RoomDraftGeometry.Create(root,layout);
                var geometry=Artwork(root,layout,"Основные текстуры","FactoryWorkshop18Artwork");
                var ladderImages=geometry.GetComponentsInChildren<MeshRenderer>().Where(r=>r.name=="Пристенная лестница").ToArray();
                var ladders=root.GetComponentsInChildren<RoomLadderAuthoring>();
                if(ladders.Length!=6 || ladderImages.Length!=ladders.Length) throw new InvalidOperationException("Не найдены изображения шести лестниц цеха.");
                for(int i=0;i<ladders.Length;i++) ladderImages[i].transform.SetParent(ladders[i].transform,true);
                Artwork(root,Read<RoomLayout>("factory-18-decor.json"),"Исходный декор","FactoryWorkshop18Decoration");
                Artwork(root,new RoomLayout {id="factory-18-backdrop",left=0,right=38.4f,bottom=0,top=20,platforms=Array.Empty<RoomSurface>(),ladders=Array.Empty<RoomLadder>(),
                    artwork=new[]{new RoomArtwork {x=0,y=0,width=38.4f,height=20,texture="Factory46/backwall",order=-40,repeat=true,backgroundLighting=true}}},
                    "Общий задник завода","FactoryWorkshop18Backdrop");
                room.UseAuthoredArtwork();
                InstallLights(root,room,source.lights);
                root.AddComponent<RoomDoorContent>().Configure("base:factory-workshop-18:doors-v1");
                root.AddComponent<RoomPhysicalContent>().Configure("base:factory-workshop-18:physical-v1");
                foreach(var record in source.records)
                {
                    if(record.kind=="case" || record.kind=="ammobox")
                    {
                        var definition=AssetDatabase.LoadAssetAtPath<PropAsset>("Assets/Remains/RoomDrafts/Containers/"+record.kind+".asset");
                        var loot=AssetDatabase.LoadAssetAtPath<ContainerLootAsset>("Assets/Remains/RoomDrafts/Containers/"+record.kind+"-loot.asset");
                        if(definition==null || loot==null) throw new InvalidOperationException("Нет определения/таблицы для контейнера: "+record.recordId);
                        FactoryContainerAuthoring.Place(root,definition,record.code,(int)record.sourceX,(int)record.sourceY,1,18).SetLoot(loot);
                        record.installed=true;record.reason="Общий переносимый контейнер; черновая таблица текущего набора предметов.";
                    }
                    else if(record.kind=="door1" || record.kind=="door2")
                    {
                        FactoryDoorAuthoring.Place(root,record.kind,record.code,(int)record.sourceX,(int)record.sourceY,record.kind=="door1"?"door:wood":"floor",18);
                        record.installed=true;record.reason="Интерактивная дверь через E, отдельное состояние в мире.";
                    }
                    else if(record.kind=="player")
                    {record.installed=true;record.reason="Сохранена стартовая метка шаблона; второй игрок не создаётся. В связном уровне вход берётся из входной комнаты.";}
                    else record.reason=record.kind switch {
                        "enl1" or "enl2"=>"Нужны исходные типы врагов, их ресурсы и независимый ИИ; замена рейдером запрещена.",
                        "platform2" or "knop3"=>"Нужна общая движущаяся платформа и связанные кнопки; исходные параметры сохранены как данные.",
                        "bigbox"=>"Нужно отдельное определение большого ящика, ресурс и масса.",
                        "vendor"=>"Торговля отложена пользователем; исходная запись сохранена.",
                        "instr2"=>"Настенный стенд для инструментов: нужны исходная графика, закрепление без физической опоры и общий контейнер; взаимодействие пока не подключено.",
                        _=>throw new InvalidOperationException("Неизвестный тип цеха: "+record.recordId+", type="+record.kind)};
                }
                var report=ScriptableObject.CreateInstance<RoomImportReportAsset>();report.Configure(source.sourceId,source.sourceHash,source.records);
                AssetDatabase.CreateAsset(report,"Assets/Remains/RoomDrafts/FactoryWorkshop18Import.asset");root.AddComponent<RoomImportAudit>().Configure(report);
                FactoryLadderLandingAuthoring.InstallOn(root,18);
                var actual=room.CreateLayout();var physical=RoomPhysicalContent.Read(room,"factory:0");var doors=RoomDoorContent.Read(room,"factory:0");
                if(actual.platforms.Length!=68 || actual.ladders.Length!=6 || actual.artwork.Length!=165 ||
                    actual.platforms.Count(s=>s.slopeDirection!=0)!=4 || physical.containers.Length!=6 || physical.loot.Length!=6 || doors.doors.Length!=3)
                    throw new InvalidOperationException("Импорт цеха потерял геометрию, оформление или объекты.");
                PrefabUtility.SaveAsPrefabAsset(root,Path);AssetDatabase.SaveAssets();report.WarnIncomplete(root);
                Debug.Log("Цех18: сохранены 68 поверхностей, 6 лестниц, 165 слоёв оформления, четыре явные верхние посадки, 2 светильника, 6 контейнеров и 3 двери.");
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
            ContractChecks.Run();
        }
        private static GameObject Artwork(GameObject root,RoomLayout layout,string name,string folder)
        {
            RoomTextureSampling.ConfigureRepeatingArtwork(layout);
            var group=RoomVisual.Build(layout,new RoomLightProfile {ambient=Color.white,lights=Array.Empty<RoomLight>()});group.name=name;
            foreach(var art in group.GetComponentsInChildren<RoomArtworkAuthoring>())
                if(art.Read(root.GetComponent<RoomAuthoring>()).order<-10) art.GetComponent<MeshRenderer>().sharedMaterial.SetFloat("_BackgroundLighting",1);
            RoomArtworkAssets.Save(group,"Assets/Remains/RoomDrafts",folder);group.transform.SetParent(root.transform,false);return group;
        }
        private static void InstallLights(GameObject root,RoomAuthoring room,Lamp[] lamps)
        {
            var referenceRoom=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Remains/RoomDrafts/FactoryEntry46.prefab").GetComponent<RoomAuthoring>();
            var reference=referenceRoom.GetComponent<RoomLightingAuthoring>().CreateProfile(referenceRoom);
            var light=reference.lights.Single(l=>l.mask=="Original/Room/Factory46/light-mask-light4-1");
            var fixture=referenceRoom.CreateLayout().artwork.Single(a=>a.texture=="Factory46/decor-light4-1");
            var lighting=root.AddComponent<RoomLightingAuthoring>();lighting.SetAmbient(reference.ambient);lighting.SetBackgroundAmbientScale(reference.backgroundAmbientScale);
            foreach(var lamp in lamps)
            {
                if(lamp.kind!="light4") throw new InvalidOperationException("Не поддержан светильник цеха: "+lamp.recordId);
                var art=root.GetComponentsInChildren<RoomArtworkAuthoring>().Single(a=>
                {var bounds=a.Read(room);return bounds.texture=="Factory18/decor-light4-1" && Mathf.Abs(bounds.x-lamp.x)<.001f && Mathf.Abs(bounds.y+bounds.height-lamp.top)<.001f;});
                var bounds=art.Read(room);var node=new GameObject("Светильник · "+lamp.recordId);node.transform.SetParent(root.transform,false);
                node.AddComponent<RoomLightAuthoring>().Configure(new RoomLight {id=lamp.recordId,x=bounds.x+bounds.width/2,y=bounds.y+bounds.height/2,
                    radius=light.radius,intensity=light.intensity,color=light.color,mask=light.mask,maskX=bounds.x+light.maskX-fixture.x,
                    maskY=bounds.y+light.maskY-fixture.y,maskWidth=light.maskWidth,maskHeight=light.maskHeight},light.maskTexture);
                art.transform.SetParent(node.transform,true);
            }
        }
    }
}
