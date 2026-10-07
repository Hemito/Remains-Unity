using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class VectorArtworkReserveAuthoring
    {
        public static void Install()
        {
            var entries=VectorArtworkReserveManifest.Entries();
            VectorArtworkReserveManifest.Require(entries.Length==22,"Ожидались 22 явно выбранных векторных рисунка.");
            // All inputs and existing registrations are checked before any PNG is replaced.
            foreach(var entry in entries)
            {
                VectorArtworkReserveManifest.VerifyStaged(entry);RequireCompatible(entry);
                string current=VectorArtworkReserveManifest.Hash(entry.texturePath);
                VectorArtworkReserveManifest.Require(current==entry.productionBeforeSha256 || current==entry.pngSha256,
                    "Пользовательский рисунок изменён; замена запрещена: "+entry.texturePath);
            }
            foreach(var entry in entries)
                if(VectorArtworkReserveManifest.Hash(entry.texturePath)!=entry.pngSha256)
                    File.Copy(VectorArtworkReserveManifest.RepositoryPath(entry.png),entry.texturePath,true);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            WeaponVectorReserveAuthoring.Install();HudVectorReserveAuthoring.Install();
            VectorArtworkReserveChecks.Run();
        }
        public static void InstallAndBuild()
        {
            Install();PrototypeBuild.BuildTo("Builds/VectorArtworkReserve/Remains.exe");
            // Build normalization is allowed to run first; it must not undo the actual density.
            VectorArtworkReserveChecks.Run();
        }
        public static void VerifyAndRender()
        {
            VectorArtworkReserveChecks.Run();VectorArtworkReserveChecks.Verify();
            CombatHudRender.RenderVectorReserve();WeaponArtworkChecks.RenderVectorReserve();
            VectorArtworkReserveChecks.Verify();
        }
        public static void RequireCompatible(ReserveEntry entry)
        {
            VectorArtworkReserveManifest.Require(AssetDatabase.AssetPathToGUID(entry.texturePath)==entry.textureGuid,
                "Изменён GUID текстуры: "+entry.texturePath);
            foreach(var declaration in entry.sprites)
            {
                var sprite=AssetDatabase.LoadAssetAtPath<Sprite>(declaration.assetPath);
                VectorArtworkReserveManifest.Require(sprite!=null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite,out string guid,out long localId) &&
                    guid==declaration.guid && localId==declaration.localId && sprite.texture!=null &&
                    AssetDatabase.GetAssetPath(sprite.texture)==entry.texturePath,"Изменена сохранённая Sprite-ссылка: "+declaration.assetPath);
                // Single Sprite imports adopt the new canvas on Refresh before
                // authoring can update PPU. Accept only that explicit intermediate
                // registration, with the already verified new pixels and same ID.
                bool importedCanvas=declaration.assetPath==entry.texturePath && VectorArtworkReserveManifest.Hash(entry.texturePath)==entry.pngSha256 &&
                    Matches(sprite,declaration.rect,declaration.pivotPixelsBottomLeft,declaration.priorPixelsPerUnit,declaration.priorBorder);
                VectorArtworkReserveManifest.Require(Matches(sprite,declaration.priorRect,declaration.priorPivotPixelsBottomLeft,declaration.priorPixelsPerUnit,declaration.priorBorder) ||
                    Matches(sprite,declaration.rect,declaration.pivotPixelsBottomLeft,declaration.pixelsPerUnit,declaration.border) || importedCanvas,
                    "Авторская регистрация изменена; замена запрещена: "+declaration.assetPath);
            }
        }
        private static bool Matches(Sprite sprite,float[] rectangle,float[] pivot,float ppu,float[] border)
        {
            return sprite.rect==new Rect(rectangle[0],rectangle[1],rectangle[2],rectangle[3]) && sprite.pixelsPerUnit==ppu &&
                Vector2.Distance(sprite.pivot,new Vector2(pivot[0],pivot[1]))<=.0001f &&
                sprite.border==new Vector4(border[0],border[1],border[2],border[3]);
        }
    }
}
