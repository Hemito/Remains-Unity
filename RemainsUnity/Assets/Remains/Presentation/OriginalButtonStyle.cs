using System;
using UnityEngine;
using UnityEngine.UI;
using Remains.Content;
namespace Remains.Presentation
{
    public static class OriginalButtonStyle
    {
        private static Sprite sprite;
        public static void SetSelected(Button button,bool selected)
        {
            var colors=button.colors;
            colors.normalColor=selected?new Color(.45f,.8f,.55f,1):new Color(.20f,.25f,.23f,.85f);
            if(button.colors.normalColor!=colors.normalColor) button.colors=colors;
        }
        public static void Apply(Button button)
        {
            if(sprite==null)
            {
                // The saved Sprite is the one density/PPU/border source for both
                // authored and dynamically created buttons, including old1×.
                sprite=GameAssets.Load<Sprite>("Presentation/MainMenuArtwork/button-glow");
                if(sprite==null) throw new InvalidOperationException("Нет сохранённой оригинальной графики кнопки: Presentation/MainMenuArtwork/button-glow.");
            }
            var image=button.GetComponent<Image>();image.sprite=sprite;image.type=Image.Type.Sliced;image.color=Color.white;button.targetGraphic=image;
            var colors=button.colors;colors.normalColor=new Color(.20f,.25f,.23f,.85f);colors.highlightedColor=new Color(.55f,.85f,.70f,.95f);
            colors.selectedColor=colors.highlightedColor;colors.pressedColor=Color.white;colors.disabledColor=new Color(.12f,.12f,.12f,.4f);colors.fadeDuration=.12f;button.colors=colors;
        }
    }
}
