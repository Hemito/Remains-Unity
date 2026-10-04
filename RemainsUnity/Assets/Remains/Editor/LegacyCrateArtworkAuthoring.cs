using System;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class LegacyCrateArtworkAuthoring
    {
        public static void Run()
        {
            const string texturePath="Assets/Remains/Resources/Original/Props/crate.png";
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if(texture.width!=320 || texture.height!=320) throw new InvalidOperationException("Ожидался экспорт visbox0 320×320.");
            var sprite=AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Remains/Prefabs/Props/prototype-crate-sprite.asset");
            var replacement=Sprite.Create(texture,new Rect(0,0,320,320),new Vector2(.5f,.5f),400,0,SpriteMeshType.FullRect);
            replacement.name=sprite.name;
            try {EditorUtility.CopySerialized(replacement,sprite);EditorUtility.SetDirty(sprite);AssetDatabase.SaveAssets();}
            finally {UnityEngine.Object.DestroyImmediate(replacement);}
            AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(sprite), ImportAssetOptions.ForceUpdate);
            sprite=AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Remains/Prefabs/Props/prototype-crate-sprite.asset");
            // Unity добавляет плоскому спрайту служебную толщину bounds; размер рисунка задают X/Y.
            if(Vector2.Distance((Vector2)sprite.bounds.size,new Vector2(.8f,.8f))>.0001f)
                throw new InvalidOperationException($"Повторный экспорт изменил размер спрайта ящика: {sprite.bounds.size.ToString("F6")}.");
            HeldPropGlowChecks.RenderAndBuild();
        }
    }
}
