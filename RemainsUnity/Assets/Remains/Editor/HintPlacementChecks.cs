using System;
using Remains.Presentation;
using UnityEngine;
namespace Remains.Editor
{
    public static class HintPlacementChecks
    {
        public static void Run()
        {
            var area=new Rect(-640,-360,1280,720);var size=new Vector2(232,39);
            foreach(var actor in new[]{new Rect(-60,-200,120,180),new Rect(-60,240,120,120),new Rect(-640,-90,130,180),new Rect(510,-90,130,180)})
            {
                if(!HintPlacement.TryPlace(actor.center,size,area,actor,out var point)) throw new InvalidOperationException("Подсказка не нашла свободное место.");
                var placed=new Rect(point-size*.5f,size);
                if(placed.Overlaps(actor) || placed.xMin<area.xMin || placed.xMax>area.xMax || placed.yMin<area.yMin || placed.yMax>area.yMax)
                    throw new InvalidOperationException("Подсказка перекрывает персонажа или вышла за экран.");
            }
            if(HintPlacement.TryPlace(Vector2.zero,size,area,area,out _)) throw new InvalidOperationException("Подсказка показана при отсутствии свободного места.");
        }
    }
}
