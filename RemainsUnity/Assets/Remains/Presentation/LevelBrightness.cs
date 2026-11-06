using UnityEngine;

namespace Remains.Presentation
{
    // How lit a point is, for game rules: the level grid on the processor, not the light on the screen. The grid leaves out
    // the direct light of a lamp, but it follows the same lamps, doors and walls, and one cell costs one array read.
    public static class LevelBrightness
    {
        // Null when this computer has no light of that level.
        public static float? Read(FactoryLevelView view,string level,Vector2 position)
        {
            if(view==null || !view.IsReady || view.LampWorldLevel!=level || view.Light.Fill==null) return null;
            return Read(view.Light.Fill,position);
        }
        public static float Read(LevelGridFill fill,Vector2 position)
        {
            var field=fill.Field;var area=fill.WorldRect;
            int x=Mathf.FloorToInt((position.x-area.x)/field.CellSize),y=Mathf.FloorToInt((position.y-area.y)/field.CellSize);
            if(x<0 || y<0 || x>=field.Width || y>=field.Height) return 0;
            var cell=field.Pixels[y*field.Width+x];
            return Mathf.Max(cell.r,cell.g,cell.b);
        }
    }
}
