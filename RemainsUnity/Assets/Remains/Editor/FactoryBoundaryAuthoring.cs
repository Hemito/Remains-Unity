using System;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class FactoryBoundaryAuthoring
    {
        public const string GroupName="Внешний контур текущего завода";
        public const string Texture="Factory18/26_tConcrete";
        public const float Thickness=.8f;
        public static void SealAndBuild()
        {Seal();RoomAuthoringScenes.Verify();PrototypeBuild.Build();}
        public static void Seal()
        {
            RoomAuthoringScenes.ValidateSavedOverrides();
            var root=PrefabUtility.LoadPrefabContents(ConnectedFactoryAuthoring.Path);
            try
            {
                if(root.transform.Find(GroupName)!=null) throw new InvalidOperationException("Контур уже сохранён; авторские правки не заменены.");
                var room=root.GetComponent<RoomAuthoring>();var source=room.CreateLayout();
                var surfaces=RoomBoundaryGaps.Find(source,Thickness,Texture,"normal");
                if(surfaces.Length==0) throw new InvalidOperationException("Внешний контур не имеет разрывов.");
                var layout=new RoomLayout {id="factory-exterior",left=source.left,right=source.right,bottom=source.bottom,top=source.top,
                    platforms=Array.Empty<RoomSurface>(),ladders=Array.Empty<RoomLadder>(),
                    artwork=surfaces.Select(s=>new RoomArtwork {x=s.x,y=s.y,width=s.width,height=s.height,texture=s.texture,order=-4,repeat=true}).ToArray()};
                var group=RoomVisual.Build(layout,new RoomLightProfile {ambient=Color.white,lights=Array.Empty<RoomLight>()});group.name=GroupName;
                group.transform.SetParent(root.transform,false);
                var artwork=group.GetComponentsInChildren<RoomArtworkAuthoring>();
                if(artwork.Length!=surfaces.Length) throw new InvalidOperationException("Контур потерял оформление.");
                for(int i=0;i<surfaces.Length;i++)
                {
                    var s=surfaces[i];var node=new GameObject("Участок внешней стены "+(i+1));node.transform.SetParent(group.transform,false);
                    node.AddComponent<RoomSurfaceAuthoring>().Configure(s);
                    var art=artwork[i];var mesh=art.GetComponent<MeshFilter>().sharedMesh;
                    mesh.vertices=mesh.vertices.Select(v=>new Vector3(v.x/s.width,v.y/s.height,v.z)).ToArray();mesh.RecalculateBounds();
                    art.transform.SetParent(node.transform,false);art.transform.localPosition=Vector3.zero;art.transform.localScale=Vector3.one;
                    art.gameObject.name="Оформление поверхности";
                }
                RoomArtworkAssets.Save(group,"Assets/Remains/RoomDrafts","FactoryExteriorArtwork");
                FactoryBoundaryChecks.Check(room);
                PrefabUtility.SaveAsPrefabAsset(root,ConnectedFactoryAuthoring.Path);AssetDatabase.SaveAssets();
                Debug.Log($"Завод: сохранено {surfaces.Length} участков внешнего контура; стык модулей не закрыт.");
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
        }
    }
}
