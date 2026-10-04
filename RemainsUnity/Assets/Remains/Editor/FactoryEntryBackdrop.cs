using System;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class FactoryEntryBackdrop
    {
        private const string Draft="Assets/Remains/RoomDrafts/FactoryEntry46.prefab";
        private const string Texture="Assets/Remains/Resources/Original/Room/Factory46/backwall.jpg";
        public static void Import()
        {
            AssetDatabase.Refresh();
            var importer=(TextureImporter)AssetImporter.GetAtPath(Texture);
            if(importer==null) throw new InvalidOperationException("Отсутствует исходное изображение задника завода: "+Texture);
            importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.npotScale=TextureImporterNPOTScale.None;importer.maxTextureSize=8192;
            importer.mipmapEnabled=false;importer.wrapMode=TextureWrapMode.Repeat;importer.SaveAndReimport();
            var root=PrefabUtility.LoadPrefabContents(Draft);
            try
            {
                var room=root.GetComponent<RoomAuthoring>();var layout=room.CreateLayout();
                if(layout.artwork.Any(a=>a.texture=="Factory46/backwall"))
                    throw new InvalidOperationException("Задник уже установлен; авторские изменения не заменены.");
                var description=new RoomLayout {id="factory-entry-backdrop",left=layout.left,right=layout.right,
                    bottom=layout.bottom,top=layout.top,platforms=Array.Empty<RoomSurface>(),ladders=Array.Empty<RoomLadder>(),
                    artwork=new[]{new RoomArtwork {texture="Factory46/backwall",x=layout.left,y=layout.bottom,
                        width=layout.right-layout.left,height=layout.top-layout.bottom,order=-40,repeat=true,backgroundLighting=true}}};
                var group=RoomVisual.Build(description,new RoomLightProfile {ambient=Color.white,lights=Array.Empty<RoomLight>()});
                group.name="Общий задник завода";
                RoomArtworkAssets.Save(group,"Assets/Remains/RoomDrafts","FactoryEntry46Backdrop");
                group.transform.SetParent(root.transform,false);
                var actual=room.CreateLayout();var background=actual.artwork.Single(a=>a.texture=="Factory46/backwall");
                if(actual.artwork.Length!=layout.artwork.Length+1 || background.order>=layout.artwork.Min(a=>a.order) ||
                    !background.backgroundLighting || background.width!=layout.right-layout.left || background.height!=layout.top-layout.bottom)
                    throw new InvalidOperationException("Задник завода: неверные границы, освещение или порядок слоёв.");
                PrefabUtility.SaveAsPrefabAsset(root,Draft);AssetDatabase.SaveAssets();
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            FactoryEntryActorsAuthoring.Verify();
            Debug.Log("Задник входа завода: PASS — явный ресурс tBackWall, геометрия и содержимое сохранены.");
        }
    }
}
