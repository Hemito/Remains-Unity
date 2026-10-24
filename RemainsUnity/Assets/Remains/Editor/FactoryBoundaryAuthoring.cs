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
                var room=root.GetComponent<RoomAuthoring>();
                int count=Build(room,"FactoryExteriorArtwork");
                FactoryBoundaryChecks.Check(room);
                PrefabUtility.SaveAsPrefabAsset(root,ConnectedFactoryAuthoring.Path);AssetDatabase.SaveAssets();
                Debug.Log($"Завод: сохранено {count} участков внешнего контура; стык модулей не закрыт.");
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
        }
        // Expansion may replace only the previously derived contour. Designer edits require an explicit decision.
        public static void ValidateDerivedContour(RoomAuthoring room)
        {
            FactoryBoundaryChecks.Check(room);
            var group=room.transform.Find(GroupName);var actual=group.GetComponentsInChildren<RoomSurfaceAuthoring>().Select(p=>p.Read(room)).ToArray();
            group.gameObject.SetActive(false);
            RoomSurface[] expected;
            try {expected=RoomBoundaryGaps.Find(room.CreateLayout(false),Thickness,Texture,"normal");}
            finally {group.gameObject.SetActive(true);}
            if(actual.Length!=expected.Length || actual.Any(a=>expected.Count(b=>Same(a,b))!=1))
                throw new InvalidOperationException("Внешний контур завода содержит авторские правки. Расширение не заменяет их автоматически: "+room.name);
            foreach(var marker in group.GetComponentsInChildren<RoomArtworkAuthoring>())
            {
                var artwork=marker.Read(room);var renderer=marker.GetComponent<MeshRenderer>();var material=renderer.sharedMaterial;
                var surface=actual.Single(s=>Mathf.Abs(s.x-artwork.x)+Mathf.Abs(s.y-artwork.y)+Mathf.Abs(s.width-artwork.width)+Mathf.Abs(s.height-artwork.height)<.001f);
                var declared=new RoomArtwork {x=surface.x,y=surface.y,width=surface.width,height=surface.height,texture=surface.texture,order=-4,repeat=true};
                var mesh=marker.GetComponent<MeshFilter>().sharedMesh;
                var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block);
                if(!FactoryLadderLandingChecks.SameArtwork(artwork,declared) || material.shader!=GameAssets.Load<Shader>("Presentation/WorldLit") ||
                    material.mainTexture!=GameAssets.Load<Texture2D>("Original/Room/"+surface.texture) || material.color!=Color.white ||
                    material.mainTextureScale!=Vector2.one || material.mainTextureOffset!=Vector2.zero || renderer.sortingLayerID!=0 ||
                    WorldArtworkMaterial.ReadFloat(renderer,"_Emission")!=0 || WorldArtworkMaterial.ReadFloat(renderer,"_BackgroundLighting")!=0 ||
                    !marker.enabled || !marker.gameObject.activeSelf || !renderer.enabled || !block.isEmpty || material.renderQueue!=3000 ||
                    material.GetColor("_RendererColor")!=Color.white || material.GetVector("_Flip")!=Vector4.one ||
                    mesh.vertexCount!=4 || mesh.colors.Length!=4 || mesh.colors.Any(c=>c!=Color.white) || mesh.uv.Length!=4)
                    throw new InvalidOperationException("Оформление внешнего контура изменено вручную; расширение его не заменяет: "+marker.name);
            }
            ValidateMeshes(room,group,expected);
        }
        private static void ValidateMeshes(RoomAuthoring room,Transform actual,RoomSurface[] surfaces)
        {
            var layout=room.CreateLayout(false);layout.platforms=Array.Empty<RoomSurface>();layout.ladders=Array.Empty<RoomLadder>();
            layout.artwork=surfaces.Select(s=>new RoomArtwork {x=s.x,y=s.y,width=s.width,height=s.height,texture=s.texture,order=-4,repeat=true}).ToArray();
            var expected=RoomVisual.Build(layout,new RoomLightProfile {ambient=Color.white,lights=Array.Empty<RoomLight>()});
            try
            {
                expected.transform.SetParent(room.transform,false);
                foreach(var marker in actual.GetComponentsInChildren<RoomArtworkAuthoring>())
                {
                    var matching=expected.GetComponentsInChildren<RoomArtworkAuthoring>().Single(m=>FactoryLadderLandingChecks.SameArtwork(m.Read(room),marker.Read(room)));
                    var a=marker.GetComponent<MeshFilter>();var b=matching.GetComponent<MeshFilter>();var av=a.sharedMesh.vertices;var bv=b.sharedMesh.vertices;
                    var au=a.sharedMesh.uv;var bu=b.sharedMesh.uv;
                    if(!a.sharedMesh.triangles.SequenceEqual(b.sharedMesh.triangles) || a.sharedMesh.normals.Length!=b.sharedMesh.normals.Length ||
                        a.sharedMesh.tangents.Length!=b.sharedMesh.tangents.Length || av.Where((v,i)=>Vector3.Distance(a.transform.TransformPoint(v),b.transform.TransformPoint(bv[i]))>.001f).Any() ||
                        au.Where((v,i)=>Vector2.Distance(v,bu[i])>.001f).Any())
                        throw new InvalidOperationException("Меш/UV внешнего контура изменены вручную; расширение не заменяет их: "+marker.name);
                }
            }
            finally {UnityEngine.Object.DestroyImmediate(expected);}
        }
        public static int Build(RoomAuthoring room,string artworkFolder)
        {
            if(room.transform.Find(GroupName)!=null) throw new InvalidOperationException("Предыдущий контур ещё присутствует: "+room.name);
            var source=room.CreateLayout(false);var surfaces=RoomBoundaryGaps.Find(source,Thickness,Texture,"normal");
            if(surfaces.Length==0) throw new InvalidOperationException("Внешний контур не имеет разрывов.");
            return BuildSurfaces(room,surfaces,GroupName,artworkFolder);
        }
        public static int BuildSurfaces(RoomAuthoring room,RoomSurface[] surfaces,string groupName,string artworkFolder)
        {
            if(room.transform.Find(groupName)!=null) throw new InvalidOperationException("Группа поверхностей уже сохранена: "+groupName);
            var source=room.CreateLayout(false);
            var layout=new RoomLayout {id="factory-exterior",left=source.left,right=source.right,bottom=source.bottom,top=source.top,
                platforms=Array.Empty<RoomSurface>(),ladders=Array.Empty<RoomLadder>(),
                artwork=surfaces.Select(s=>new RoomArtwork {x=s.x,y=s.y,width=s.width,height=s.height,texture=s.texture,order=-4,repeat=true}).ToArray()};
            var group=RoomVisual.Build(layout,new RoomLightProfile {ambient=Color.white,lights=Array.Empty<RoomLight>()});group.name=groupName;
            group.transform.SetParent(room.transform,false);
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
            RoomArtworkAssets.Save(group,"Assets/Remains/RoomDrafts",artworkFolder);return surfaces.Length;
        }
        private static bool Same(RoomSurface a,RoomSurface b)=>
            Mathf.Abs(a.x-b.x)+Mathf.Abs(a.y-b.y)+Mathf.Abs(a.width-b.width)+Mathf.Abs(a.height-b.height)<.001f &&
            a.texture==b.texture && a.movementSound==b.movementSound && a.oneWay==b.oneWay &&
            a.shotsPassThrough==b.shotsPassThrough && a.slopeDirection==b.slopeDirection;
    }
}
