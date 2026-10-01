using System;
using UnityEngine;
using UnityEngine.UI;
namespace Remains.Presentation
{
    public static class OriginalButtonStyle
    {
        private static Sprite sprite;
        public static void Apply(Button button)
        {
            if(sprite==null)
            {
                var texture=Resources.Load<Texture2D>("Original/Menu/button-glow");
                if(texture==null) throw new InvalidOperationException("Нет оригинальной графики кнопки.");
                sprite=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f),100,0,SpriteMeshType.FullRect,new Vector4(25,5,25,5));
            }
            var image=button.GetComponent<Image>();image.sprite=sprite;image.type=Image.Type.Sliced;image.color=Color.white;button.targetGraphic=image;
            var colors=button.colors;colors.normalColor=new Color(.20f,.25f,.23f,.85f);colors.highlightedColor=new Color(.55f,.85f,.70f,.95f);
            colors.selectedColor=colors.highlightedColor;colors.pressedColor=Color.white;colors.disabledColor=new Color(.12f,.12f,.12f,.4f);colors.fadeDuration=.12f;button.colors=colors;
        }
    }
}
