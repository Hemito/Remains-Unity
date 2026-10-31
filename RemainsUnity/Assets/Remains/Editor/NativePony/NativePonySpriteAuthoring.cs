using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.U2D;
using UnityEngine.U2D.Animation;

namespace Remains.Editor
{
    // Writes the same persistent importer data edited by Unity's native Skinning Editor.
    // No Sprite.OverrideGeometry / transient Sprite.Create is used as the authoring source.
    public static class NativePonySpriteAuthoring
    {
        /// <summary>
        /// World-space input. Root bone positions are actor-world; children are parent-local.
        /// Sprite pivot is bottom-left: put its SpriteRenderer at worldRect.min and keep the
        /// skeleton in the original actor coordinates. Array order is preserved for bone weights.
        /// </summary>
        public static Sprite Import(string pngAssetPath,Rect worldRect,Vector2[] worldVertices,int[] triangles,
            SpriteBone[] actorBones,BoneWeight[] weights,Vector2Int[] edges=null)
        {
            if(!Finite(worldRect.x) || !Finite(worldRect.y) || !Finite(worldRect.width) || !Finite(worldRect.height) ||
                worldRect.width<=0 || worldRect.height<=0 || worldVertices==null || weights==null || worldVertices.Length!=weights.Length)
                throw Error(pngAssetPath,"неверные мировые границы, вершины или веса");
            RequirePath(pngAssetPath);
            AssetDatabase.ImportAsset(pngAssetPath,ImportAssetOptions.ForceSynchronousImport);
            var importer=AssetImporter.GetAtPath(pngAssetPath) as TextureImporter;
            if(importer==null)throw Error(pngAssetPath,"PNG не распознан как TextureImporter");
            importer.GetSourceTextureWidthAndHeight(out int width,out int height);
            if(width>8192 || height>8192)throw Error(pngAssetPath,$"PNG {width}×{height} превышает явный бюджет образца 8192; уменьшите плотность или разделите рисунок");
            float ppu=width/worldRect.width;
            if(Mathf.Abs(height/worldRect.height-ppu)>ppu*.001f)
                throw Error(pngAssetPath,$"соотношение сторон PNG {width}×{height} не совпало с миром {worldRect.size}");
            if(actorBones==null)throw Error(pngAssetPath,"кости отсутствуют");
            var bones=new SpriteBone[actorBones.Length];
            for(int b=0;b<bones.Length;b++)
            {
                var value=actorBones[b];
                value.position=(value.position-(value.parentId<0?(Vector3)worldRect.min:Vector3.zero))*ppu;
                value.length*=ppu;bones[b]=value;
            }
            var vertices=new Vertex2DMetaData[worldVertices.Length];
            for(int i=0;i<vertices.Length;i++)vertices[i]=new Vertex2DMetaData {position=(worldVertices[i]-worldRect.min)*ppu,boneWeight=weights[i]};
            return PrepareSerializedSprite(pngAssetPath,ppu,bones,vertices,triangles,edges);
        }

        /// <summary>
        /// Native importer units: vertices and root bones in sprite-rect-local pixels, children
        /// in parent-local pixels. Bones, weights, vertices, edges and indices survive reimport.
        /// </summary>
        public static Sprite PrepareSerializedSprite(string pngAssetPath,float pixelsPerUnit,SpriteBone[] bones,
            Vertex2DMetaData[] vertices,int[] indices,Vector2Int[] edges=null)
        {
            RequirePath(pngAssetPath);
            if(!Finite(pixelsPerUnit) || pixelsPerUnit<=0)throw Error(pngAssetPath,"неверное число пикселей на метр");
            Validate(pngAssetPath,bones,vertices,indices,edges);
            AssetDatabase.ImportAsset(pngAssetPath,ImportAssetOptions.ForceSynchronousImport);
            var importer=AssetImporter.GetAtPath(pngAssetPath) as TextureImporter;
            if(importer==null)throw Error(pngAssetPath,"PNG не распознан как TextureImporter");
            importer.GetSourceTextureWidthAndHeight(out int width,out int height);
            if(width>8192 || height>8192)throw Error(pngAssetPath,$"PNG {width}×{height} превышает явный бюджет образца 8192");
            importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;
            importer.spritePixelsPerUnit=pixelsPerUnit;importer.spritePivot=Vector2.zero;
            importer.mipmapEnabled=false;importer.alphaIsTransparency=true;importer.sRGBTexture=true;
            importer.textureCompression=TextureImporterCompression.Uncompressed;importer.npotScale=TextureImporterNPOTScale.None;
            importer.maxTextureSize=8192;importer.filterMode=FilterMode.Bilinear;importer.wrapMode=TextureWrapMode.Clamp;
            importer.isReadable=true;
            var settings=new TextureImporterSettings();importer.ReadTextureSettings(settings);
            settings.spriteAlignment=(int)SpriteAlignment.Custom;settings.spritePivot=Vector2.zero;
            settings.spriteMeshType=SpriteMeshType.Tight;settings.spriteExtrude=0;importer.SetTextureSettings(settings);
            importer.SaveAndReimport();

            var factory=new SpriteDataProviderFactories();factory.Init();
            var provider=factory.GetSpriteEditorDataProviderFromObject(importer);
            if(provider==null)throw Error(pngAssetPath,"нет Sprite Editor Data Provider");
            provider.InitSpriteEditorDataProvider();
            var rects=provider.GetSpriteRects();
            if(rects.Length!=1)throw Error(pngAssetPath,$"ожидался один спрайт, получено {rects.Length}");
            var rect=rects[0];rect.name=Path.GetFileNameWithoutExtension(pngAssetPath);rect.rect=new Rect(0,0,width,height);
            rect.alignment=SpriteAlignment.Custom;rect.pivot=Vector2.zero;
            rects[0]=rect;
            provider.SetSpriteRects(rects);
            var boneProvider=provider.GetDataProvider<ISpriteBoneDataProvider>();
            var meshProvider=provider.GetDataProvider<ISpriteMeshDataProvider>();
            if(boneProvider==null || meshProvider==null)throw Error(pngAssetPath,"импортёр не поддерживает кости или сетку");
            var storedBones=(SpriteBone[])bones.Clone();
            for(int b=0;b<storedBones.Length;b++)
            {
                var value=storedBones[b];
                // Stable, shared bone identities allow the stock SpriteSkin rebinding workflow.
                if(string.IsNullOrEmpty(value.guid))value.guid=Hash128.Compute("Remains.NativePony/"+value.name).ToString();
                storedBones[b]=value;
            }
            boneProvider.SetBones(rect.spriteID,new List<SpriteBone>(storedBones));
            meshProvider.SetVertices(rect.spriteID,(Vertex2DMetaData[])vertices.Clone());
            meshProvider.SetIndices(rect.spriteID,(int[])indices.Clone());
            meshProvider.SetEdges(rect.spriteID,edges==null?BoundaryEdges(indices):(Vector2Int[])edges.Clone());
            provider.Apply();importer.SaveAndReimport();
            var sprite=AssetDatabase.LoadAssetAtPath<Sprite>(pngAssetPath);
            if(sprite==null)throw Error(pngAssetPath,"после импорта отсутствует Sprite");
            if(sprite.GetBones().Length!=bones.Length || sprite.GetVertexCount()!=vertices.Length || sprite.GetBindPoses().Length!=bones.Length)
                throw Error(pngAssetPath,$"импорт изменил сетку: кости={sprite.GetBones().Length}/{bones.Length}, вершины={sprite.GetVertexCount()}/{vertices.Length}, bindposes={sprite.GetBindPoses().Length}");
            return sprite;
        }

        public static SpriteSkin Bind(SpriteRenderer renderer,Transform rootBone,Transform[] bones)
        {
            if(renderer==null || renderer.sprite==null || rootBone==null || bones==null || Array.Exists(bones,b=>b==null))
                throw Error(renderer?.name??"<объект отсутствует>","не заданы спрайт, корень или кости");
            if(renderer.sprite.GetBones().Length!=bones.Length)throw Error(renderer.name,"число Transform костей не совпало со спрайтом");
            var skin=renderer.gameObject.AddComponent<SpriteSkin>();skin.alwaysUpdate=true;
            skin.SetRootBone(rootBone);var state=skin.SetBoneTransforms(bones);
            if(state!=SpriteSkinState.Ready)throw Error(renderer.name,"SpriteSkin не готов: "+state);
            return skin;
        }

        private static void Validate(string path,SpriteBone[] bones,Vertex2DMetaData[] vertices,int[] indices,Vector2Int[] edges)
        {
            if(bones==null || bones.Length==0 || vertices==null || vertices.Length<3 || vertices.Length>65535 || indices==null || indices.Length==0 || indices.Length%3!=0)
                throw Error(path,"нужны кости, 3–65535 вершин и треугольники");
            var names=new HashSet<string>(StringComparer.Ordinal);var visits=new byte[bones.Length];
            for(int b=0;b<bones.Length;b++)
            {
                var bone=bones[b];
                if(string.IsNullOrWhiteSpace(bone.name) || !names.Add(bone.name) || bone.parentId<-1 || bone.parentId>=bones.Length || bone.parentId==b ||
                    !Finite(bone.position.x) || !Finite(bone.position.y) || !Finite(bone.position.z) || !Finite(bone.length) || bone.length<0 ||
                    !Finite(bone.rotation.x) || !Finite(bone.rotation.y) || !Finite(bone.rotation.z) || !Finite(bone.rotation.w) ||
                    Mathf.Abs(Quaternion.Dot(bone.rotation,bone.rotation)-1)>.001f)throw Error(path,"повреждена кость "+b+"/"+bone.name);
            }
            for(int b=0;b<bones.Length;b++)Visit(b);
            for(int v=0;v<vertices.Length;v++)
            {
                var vertex=vertices[v];var weight=vertex.boneWeight;
                if(!Finite(vertex.position.x) || !Finite(vertex.position.y))throw Error(path,"неконечная вершина "+v);
                var values=new[]{weight.weight0,weight.weight1,weight.weight2,weight.weight3};
                var bindings=new[]{weight.boneIndex0,weight.boneIndex1,weight.boneIndex2,weight.boneIndex3};
                if(values.Any(w=>!Finite(w) || w<0) || Mathf.Abs(values.Sum()-1)>.0001f)throw Error(path,"неверная сумма весов вершины "+v);
                for(int slot=0;slot<4;slot++)if(values[slot]>0 && (bindings[slot]<0 || bindings[slot]>=bones.Length))throw Error(path,"неизвестная кость вершины "+v);
            }
            if(indices.Any(i=>i<0 || i>=vertices.Length))throw Error(path,"индекс треугольника вне сетки");
            if(edges!=null && edges.Any(e=>e.x<0 || e.y<0 || e.x>=vertices.Length || e.y>=vertices.Length || e.x==e.y))throw Error(path,"ребро вне сетки");
            void Visit(int i){if(visits[i]==2)return;if(visits[i]==1)throw Error(path,"цикл родителей костей");visits[i]=1;if(bones[i].parentId>=0)Visit(bones[i].parentId);visits[i]=2;}
        }
        private static Vector2Int[] BoundaryEdges(int[] triangles)
        {
            var counts=new Dictionary<(int,int),int>();
            for(int t=0;t<triangles.Length;t+=3)for(int corner=0;corner<3;corner++)
            {
                int a=triangles[t+corner],b=triangles[t+(corner+1)%3];var key=a<b?(a,b):(b,a);
                counts.TryGetValue(key,out int count);counts[key]=count+1;
            }
            return counts.Where(pair=>pair.Value==1).Select(pair=>new Vector2Int(pair.Key.Item1,pair.Key.Item2)).ToArray();
        }
        private static void RequirePath(string path)
        {
            if(string.IsNullOrWhiteSpace(path) || !path.StartsWith("Assets/",StringComparison.Ordinal) || path.Contains("..") || !path.EndsWith(".png",StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                throw Error(path,"нужен существующий PNG внутри Assets");
        }
        private static bool Finite(float value)=>!float.IsNaN(value) && !float.IsInfinity(value);
        private static InvalidOperationException Error(string path,string reason)=>new("Новый риг пони: "+path+": "+reason+".");
    }
}
