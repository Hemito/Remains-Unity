using System;
using System.Linq;
using Remains.Domain;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Runtime
{
    // Observe the loaded gameplay views; the extra photograph changes only the camera during Render.
    internal static class FactoryBigBoxObservation
    {
        public static void Verify(PrototypeRuntime runtime)
        {
            var boxes=runtime.Session.World.props.Where(p=>p.level=="factory:0" && p.definition=="bigbox" &&
                p.id.StartsWith("factory:0:original:plant:18:",StringComparison.Ordinal)).ToArray();
            if(boxes.Length!=2 || boxes.Any(p=>p.mass!=175 || p.width!=2 || p.height!=1.6f || p.anchorHeight!=0 || p.held))
                throw new InvalidOperationException($"Цех18 потерял два исходных больших ящика или их физические параметры: count={boxes.Length}, ids={string.Join(",",boxes.Select(p=>p.id))}.");
            var visuals=UnityEngine.Object.FindFirstObjectByType<InteractionVisuals>();
            foreach(var box in boxes)
            {
                var view=visuals.FindProp(box.id);
                if(view==null || !view.gameObject.activeInHierarchy || view.Depth?.Category!=WorldVisualCategory.OrdinaryObject)
                    throw new InvalidOperationException("Нет действующего игрового изображения большого ящика: "+box.id);
                var bounds=view.Artwork.bounds;
                if(Mathf.Abs(bounds.size.x-box.width)>.001f || Mathf.Abs(bounds.size.y-box.height)>.001f ||
                    Vector2.Distance(bounds.center,box.Center)>.02f)
                    throw new InvalidOperationException("Игровое изображение расходится с физикой большого ящика: "+box.id);
            }
        }
        public static void Capture(PrototypeRuntime runtime)
        {
            var camera=Camera.main;var saved=camera.transform.position;
            try
            {
                camera.transform.position=new Vector3(53.6f,4.2f,saved.z);
                NetworkFrameCapture.Save(camera,runtime.Options.Report+".workshop-bigboxes.png",runtime.Hud.GetComponent<Canvas>());
            }
            finally {camera.transform.position=saved;}
        }
    }
}
