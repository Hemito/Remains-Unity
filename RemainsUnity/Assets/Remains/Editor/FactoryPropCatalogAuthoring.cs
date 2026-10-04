using System;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class FactoryPropCatalogAuthoring
    {
        public static void Register()
        {
            var catalog=EntityAssetCatalog.Load();var serialized=new SerializedObject(catalog);
            var props=serialized.FindProperty("props");
            foreach(string path in new[]{"Assets/Remains/RoomDrafts/woodbox.asset",
                "Assets/Remains/RoomDrafts/Containers/case.asset","Assets/Remains/RoomDrafts/Containers/chest.asset",
                "Assets/Remains/RoomDrafts/Containers/ammobox.asset"})
            {
                var asset=AssetDatabase.LoadAssetAtPath<PropAsset>(path);
                if(asset==null || asset.Prefab==null) throw new InvalidOperationException("Не готов физический объект: "+path);
                string id=asset.CreateDefinition().id;
                bool exists=false;
                for(int i=0;i<props.arraySize;i++)
                {
                    var current=props.GetArrayElementAtIndex(i).objectReferenceValue as PropAsset;
                    if(current==null) throw new InvalidOperationException("Пустая ссылка каталога: "+i);
                    if(current.CreateDefinition().id!=id) continue;
                    if(current!=asset) throw new InvalidOperationException("Тип уже зарегистрирован другим ассетом: "+id);
                    exists=true;
                }
                if(!exists) {int index=props.arraySize;props.InsertArrayElementAtIndex(index);props.GetArrayElementAtIndex(index).objectReferenceValue=asset;}
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();AssetDatabase.SaveAssets();Verify();
            ContractChecks.Run();
        }
        public static void Verify()
        {
            PropCatalog.Load();var artwork=new PropArtwork();
            foreach(string id in new[]{"woodbox","case","chest","ammobox"})
            {
                var data=PropCatalog.Get(id);var view=artwork.CreateView(id,"Проверка "+id,data.width);
                try
                {
                    var state=new PropState {id=id,definition=id,x=3,y=2,width=data.width,height=data.height,anchorHeight=0};
                    view.DrawMotion(state,"local",false,0);
                    var bounds=view.Artwork.bounds;
                    if(Mathf.Abs(bounds.min.y-state.y)>.001f || Mathf.Abs(bounds.center.x-state.x)>.001f ||
                        Mathf.Abs(bounds.size.x-data.width)>.001f || Mathf.Abs(bounds.size.y-data.height)>.001f)
                        throw new InvalidOperationException("Игровая графика не совпала с физическими размерами и опорой: "+id);
                }
                finally {UnityEngine.Object.DestroyImmediate(view.gameObject);}
            }
            var room=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Remains/RoomDrafts/FactoryEntry46.prefab").GetComponent<RoomAuthoring>();
            var layout=RoomPhysicalContent.Read(room,"factory:0");
            foreach(var state in layout.props.Concat(layout.containers.Cast<PropState>()))
            {
                var definition=PropCatalog.Get(state.definition);
                if(state.mass!=definition.mass || state.width!=definition.width || state.height!=definition.height)
                    throw new InvalidOperationException("Расстановка расходится с каталогом: "+state.id);
            }
            Debug.Log("Каталог завода: PASS — четыре типа, игровые префабы, размеры и нижняя опора, вся физическая расстановка разрешается.");
        }
    }
}
