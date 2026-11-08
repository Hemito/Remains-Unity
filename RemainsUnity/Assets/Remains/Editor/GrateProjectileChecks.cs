using System;
using System.Reflection;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Networking;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Supplied factory inventory: original grate phis=2 blocks actors while passing bullets.
    public static class GrateProjectileChecks
    {
        public static void RunBatch()
        {
            try {Run();EditorApplication.Exit(0);}
            catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void Run()
        {
            Anatomy.Load();WeaponCatalog.Load();ActorTypes.Load();PropCatalog.Load();
            Check("grate",false,true);Check("grate",true,true);
            Check("door2",false,false);Check("door2",true,true);
            DoorGeometryChecks.Run();DoorBarrierChecks.Run();DoorCombatChecks.Run();
            Debug.Log("GrateProjectileChecks: PASS — actual pistol projectile and damage pass grate, solid closed door stops shot, open doors pass; closed grate still blocks actor movement; state save roundtrip; existing door geometry/barrier/melee checks.");
        }
        private static void Check(string definition,bool open,bool passes)
        {
            var root=new GameObject("Grate projectile check");
            try
            {
                var asset=GameAssets.Load<Remains.Presentation.DoorView>("Presentation/Doors/"+definition).Definition;
                var world=new WorldState();
                var observer=new ActorState {id="observer",owner="b",player=true,connected=true,level="hub",x=-5};
                var target=new ActorState {id="target",owner="b",level="hub",x=3,health=100};
                world.actors.Add(observer);world.actors.Add(target);
                world.doors.Add(new DoorState {id="barrier",definition=asset.Id,level="hub",surfaceTexture="floor",
                    x=1.5f,y=0,width=asset.Size.x,height=asset.Size.y,open=open});
                world=StateJson.Copy(world);target=world.Find("target");
                var session=root.AddComponent<CoopSession>();
                typeof(CoopSession).GetProperty("World").SetValue(session,world);
                typeof(CoopSession).GetProperty("LocalActorId").SetValue(session,observer.id);
                var geometry=new SimulationGeometry(_=>new RoomLayout {id="grate-test",left=-10,right=20,bottom=-2,top=10,platforms=Array.Empty<RoomSurface>()});
                geometry.Reconcile(world);
                var room=geometry.Get("hub");
                var moved=ActorTerrainMotion.Move(room,new MovementHull {width=.5f,height=1.8f},new Vector2(0,0),new Vector2(4,0),true);
                Require(open?moved.x>3:moved.x<1.5f,$"{definition} open={open}: actor movement policy changed, x={moved.x}");
                int contacts=0;var damage=new DamageReceiver(new PrototypeDamageRule());
                var controller=new ProjectileController(session,geometry,(attack,contact,owner)=>
                {contacts++;damage.Apply(target,attack,contact.contact);});
                world.projectiles.Add(new ProjectileState {launch=new AttackEvent {id="pistol-through-"+definition,attacker="source",
                    sourceOwner="a",sourcePlayer=true,weapon="pistol",level="hub",originY=.7f,endX=20,endY=.7f},
                    owner="a",generation=1,flightSpeed=90});
                typeof(ProjectileController).GetMethod("Reconcile",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(controller,null);
                controller.Step(.1f);
                Require(contacts==(passes?1:0) && (passes?target.health<100:target.health==100),
                    $"{definition} open={open}: expected passes={passes}, contacts={contacts}, targetHealth={target.health}");
                Debug.Log($"GrateProjectileCase: PASS {definition} open={open}, contacts={contacts}, health={target.health}, actorX={moved.x}.");
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
        }
        private static void Require(bool value,string message)
        {if(!value)throw new InvalidOperationException("grate: "+message);}
    }
}
