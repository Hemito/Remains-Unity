using System;
using System.Linq;
using Remains.Domain;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Runtime
{
    // One explicit observation of the loaded ordinary level, never a source of content.
    internal static class FactoryWorkshopNpcObservation
    {
        public static void Verify(PrototypeRuntime runtime)
        {
            var actors=runtime.Session.World.actors.Where(a=>!a.player && a.actorType=="raider" && a.id.StartsWith("factory:0:original:plant:18:",StringComparison.Ordinal)).ToArray();
            var views=UnityEngine.Object.FindObjectsByType<ActorVisual>(FindObjectsInactive.Include,FindObjectsSortMode.None);
            if(actors.Length!=5 || actors.Any(a=>a.actorType!="raider" || a.inventory.Count!=3 || string.IsNullOrWhiteSpace(a.owner) ||
                WeaponInventory.Selected(a)?.definition!="pistol" || !views.Any(v=>v.ActorId==a.id && v.gameObject.activeInHierarchy)))
                throw new InvalidOperationException("Обычный вход потерял врагов цеха, действующие изображения, владельцев или инвентари.");
            if(actors.SelectMany(a=>a.inventory).Select(i=>i.id).Distinct().Count()!=15)
                throw new InvalidOperationException("Разные тела цеха делят ID предметов.");
        }
        public static void Capture(PrototypeRuntime runtime)
        {
            var camera=Camera.main;var saved=camera.transform.position;
            try
            {
                camera.transform.position=new Vector3(62.4f,5.1f,saved.z);
                NetworkFrameCapture.Save(camera,runtime.Options.Report+".workshop-raiders.png",runtime.Hud.GetComponent<Canvas>());
            }
            finally {camera.transform.position=saved;}
        }
    }
}
