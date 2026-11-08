using System.Diagnostics;
using Remains.RoomFormat;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using UnityEngine;

namespace Remains.RoomEditor;

public sealed class MovementProfile
{
 public MovementSettings Settings {get;private set;}
 public MovementHull Hull {get;private set;}
 public static MovementProfile Read(string movement,string anatomy)
 {
  var j=JsonText.Parse(movement,"movement.json");
  float N(string name)=>(float)(j.Get(name)??throw new FormatException("Missing movement setting: "+name)).AsDouble(name);
  var settings=new MovementSettings{
   walkSpeed=N("walkSpeed"),runSpeed=N("runSpeed"),climbSpeed=N("climbSpeed"),jumpSpeed=N("jumpSpeed"),dropSpeed=N("dropSpeed"),doubleTapSeconds=N("doubleTapSeconds"),
   groundAcceleration=N("groundAcceleration"),groundBraking=N("groundBraking"),airAcceleration=N("airAcceleration"),airBraking=N("airBraking"),climbDownMultiplier=N("climbDownMultiplier"),
   actorGravity=N("actorGravity"),jumpSupportSeconds=N("jumpSupportSeconds"),crouchSpeed=N("crouchSpeed"),crouchSolidSeconds=N("crouchSolidSeconds"),crouchPlatformSeconds=N("crouchPlatformSeconds"),sprintPreparationSeconds=N("sprintPreparationSeconds"),stepHeight=N("stepHeight")};
  settings.Validate();
  var entries=JsonText.Parse(anatomy,"anatomy.json").Get("anatomies").Items;
  var pony=entries.Single(a=>a.Get("id").AsString("id")=="pony").Get("movement");
  var hull=new MovementHull{width=(float)pony.Get("width").AsDouble("width"),height=(float)pony.Get("height").AsDouble("height")};
  if(hull.width<=0||hull.height<=0)throw new FormatException("Invalid pony movement hull");
  return new MovementProfile{Settings=settings,Hull=hull};
 }
}
public sealed class ReachabilityResult
{
 public HashSet<(int X,int Y)> Reached {get;}=new();
 public HashSet<(int X,int Y)> Candidates {get;}=new();
 public int Explored;
 public int StartX,StartY;
 public double Milliseconds;
 public bool Truncated;
}
public static class Reachability
{
 // Finds positive static-room routes using the actual game collision, stepping,
 // acceleration and sustained-jump kernels. An unexplored cell is NOT proof of
 // impossibility: the bounded input search has no crouch, destruction or moving props.
 public static async Task<ReachabilityResult> Analyze(RoomFile source,RoomDefinitionCatalog definitions,MovementProfile profile,
  int startX,int startY,CancellationToken cancellation=default)
 {
  if((long)source.Width*source.Height>20_000)throw new InvalidOperationException("Для поиска маршрутов выберите комнату или карту до 20000 клеток; редактирование и сохранение больших комнат доступны.");
  var watch=Stopwatch.StartNew();var result=new ReachabilityResult();
  var geometry=RoomGeometryBuilder.Build(source,definitions);
  var room=new RoomLayout{left=0,bottom=0,right=(float)geometry.Width,top=(float)geometry.Height,platforms=geometry.Surfaces.Select(s=>new RoomSurface{x=(float)s.X,y=(float)s.Y,width=(float)s.Width,height=(float)s.Height,oneWay=s.OneWay,slopeDirection=s.Slope}).ToArray()};
  room.ladders=geometry.Ladders.Select(l=>new RoomLadder{x=(float)l.X,bottom=(float)l.Bottom,top=(float)l.Top,movementOffset=(float)l.MovementOffset,facing=l.Facing}).ToArray();
  RoomSurfaces.Prepare(room);var hull=profile.Hull;var settings=profile.Settings;float cell=(float)source.Cell;
  var positions=new List<Vector2>();
  int sampled=0;bool preprocessingBudget=false;
  foreach(var surface in room.platforms)
  {
   int left=Math.Max(0,(int)MathF.Floor((surface.x-hull.width*.5f)/cell-.5f)+1);
   int right=Math.Min(source.Width-1,(int)MathF.Ceiling((surface.x+surface.width+hull.width*.5f)/cell-.5f)-1);
   for(int x=left;x<=right;x++)
   {
    if(sampled++%128==0)
    {
     await Task.Delay(1,cancellation);
     if(watch.Elapsed.TotalSeconds>=12){preprocessingBudget=true;break;}
    }
    float axis=(x+.5f)*cell;
    var point=new Vector2(axis,RoomSupport.Top(surface,axis,hull.width));
    if(ActorTerrainMotion.IsClear(room,hull,point)&&ActorTerrainMotion.Supported(room,hull,point))
    {positions.Add(point);result.Candidates.Add(Cell(point));}
   }
   if(preprocessingBudget)break;
  }
  if(preprocessingBudget){result.Truncated=true;result.Milliseconds=watch.Elapsed.TotalMilliseconds;return result;}
  if(positions.Count==0)throw new InvalidOperationException("Нет свободной опоры для стоящего персонажа.");
  var start=positions.OrderBy(p=>MathF.Abs(p.x-(startX+.5f)*cell)+MathF.Abs(p.y-startY*cell)).First();
  var queue=new Queue<(Vector2 position,float vx)>();var keys=new HashSet<(int,int)>();
  (int,int) Key(Vector2 p)=>((int)MathF.Round(p.x/(cell*.25f)),(int)MathF.Round(p.y/(cell*.25f)));
  (int,int) Cell(Vector2 p)=>((int)MathF.Floor(p.x/cell),Math.Clamp((int)MathF.Floor((p.y+.001f)/cell),0,source.Height-1));
  void Reach(Vector2 p,float vx){result.Reached.Add(Cell(p));if(keys.Add(Key(p)))queue.Enqueue((p,vx));}
  (result.StartX,result.StartY)=Cell(start);Reach(start,0);
  const float dt=1f/60;
  while(queue.Count>0)
  {
   cancellation.ThrowIfCancellationRequested();
   if(result.Explored++>=1600||watch.Elapsed.TotalSeconds>=12){result.Truncated=true;break;}
   var initial=queue.Dequeue();
   foreach(int direction in new[]{-1,0,1})foreach(bool jump in new[]{false,true})foreach(bool sprint in new[]{false,true})
   {
    var p=initial.position;float vx=initial.vx;var actor=new ActorState();
    for(int frame=0;frame<150;frame++)
    {
     bool grounded=ActorTerrainMotion.Supported(room,hull,p);
     vx=HorizontalMotion.Step(vx,direction,grounded,sprint,dt,settings,false,sprint?Math.Clamp(frame*dt-settings.sprintPreparationSeconds,0,dt):dt);
     float dy=VerticalMotion.Step(actor,jump&&frame==0,true,dt,settings);
     var delta=new Vector2(vx*dt,dy);var next=ActorTerrainMotion.Move(room,hull,p,delta,false);
     if(grounded&&!(jump&&frame==0)&&ActorStepMotion.TryMove(room,hull,p,delta,next,settings.stepHeight,out var step))next=step;
     if(MathF.Abs(next.x-p.x-delta.x)>.0001f)vx=0;
     if(MathF.Abs(next.y-p.y-delta.y)>.0001f){actor.vy=0;actor.jumpSupportRemaining=0;}
     if(!ActorTerrainMotion.IsClear(room,hull,next)||next.y<0||next.y+hull.height>room.top)break;
     bool supported=ActorTerrainMotion.Supported(room,hull,next);
     if(supported && MathF.Abs(actor.vy)<.0001f)Reach(next,vx);
     if(MathF.Abs(next.x-p.x)<.0001f&&MathF.Abs(next.y-p.y)<.0001f&&frame>2)break;
     p=next;
    }
   }
   // Only upward climbing: it does not require pose-dependent descending release.
   var selected=LadderMotion.Select(room.ladders,initial.position.x,initial.position.y,false,null);
   if(LadderMotion.Attach(selected,false,false,false,false,1,initial.position.y))
   {
    var p=initial.position;
    for(int frame=0;frame<1000;frame++)
    {
     var velocity=LadderMotion.Velocity(selected,p.x,1,dt,settings.walkSpeed,settings.climbSpeed,settings.climbDownMultiplier);
     var next=ActorTerrainMotion.Move(room,hull,p,velocity*dt,true);
     next=new Vector2(next.x,Math.Clamp(next.y,selected.bottom,selected.top));
     if(!ActorTerrainMotion.IsClear(room,hull,next)||!LadderMotion.InReach(selected,next.x,next.y))break;
     // A zero-input held frame stops vertical motion before a supported side exit.
     var still=LadderMotion.Velocity(selected,next.x,0,dt,settings.walkSpeed,settings.climbSpeed,settings.climbDownMultiplier);
     var settled=ActorTerrainMotion.Move(room,hull,next,still*dt,true);
     if(ActorTerrainMotion.IsClear(room,hull,settled)&&ActorTerrainMotion.Supported(room,hull,settled))Reach(settled,still.x);
     if(MathF.Abs(next.y-p.y)<.0001f||next.y>=selected.top-.001f)break;
     p=next;
    }
   }
   if(result.Explored%4==0)await Task.Delay(1,cancellation); // return control to browser: cancel and paint remain responsive
  }
  result.Milliseconds=watch.Elapsed.TotalMilliseconds;return result;
 }
}
