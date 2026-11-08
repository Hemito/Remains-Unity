// Small value/data adapter for the linked game movement kernels. No Unity runtime,
// collision algorithm or movement constants are copied into the web application.
namespace UnityEngine
{
 public readonly struct Vector2(float x,float y)
 {
  public readonly float x=x,y=y;
  public static Vector2 up=>new(0,1);public static Vector2 down=>new(0,-1);public static Vector2 right=>new(1,0);
  public static Vector2 operator +(Vector2 a,Vector2 b)=>new(a.x+b.x,a.y+b.y);
  public static Vector2 operator *(Vector2 a,float b)=>new(a.x*b,a.y*b);
 }
 public readonly struct Rect(float x,float y,float width,float height)
 {
  public float xMin=>x;public float xMax=>x+width;public float yMin=>y;public float yMax=>y+height;
  public static Rect MinMaxRect(float x0,float y0,float x1,float y1)=>new(x0,y0,x1-x0,y1-y0);
 }
 public static class Mathf
 {
  public static float Abs(float x)=>MathF.Abs(x);public static float Sign(float x)=>x>=0?1:-1;
  public static float Min(float a,float b)=>MathF.Min(a,b);public static float Max(float a,float b)=>MathF.Max(a,b);
  public static float Clamp(float x,float a,float b)=>Math.Clamp(x,a,b);public static int FloorToInt(float x)=>(int)MathF.Floor(x);
  public static float MoveTowards(float current,float target,float maximum)=>MathF.Abs(target-current)<=maximum?target:current+Sign(target-current)*maximum;
 }
 public sealed class TextAsset {public string text;}
 public static class JsonUtility {public static T FromJson<T>(string json)=>throw new NotSupportedException("Use browser asset initialization.");}
}
namespace Remains.Content
{
 public static class GameAssets {public static T Load<T>(string path)=>throw new NotSupportedException("Use browser asset initialization.");}
}
namespace Remains.Domain
{
 public sealed class MovementHull {public float width,height;}
 public sealed class ActorState {public float vy,jumpSupportRemaining;}
 public sealed class RoomLadder {public float x,bottom,top,movementOffset;public int facing;}
 public sealed class RoomSurface {public float x,y,width,height;public bool oneWay;public int slopeDirection;}
 public sealed class RoomLayout
 {
  public float left,right,bottom,top;
  public RoomSurface[] platforms;
  public RoomLadder[] ladders;
  public RoomSurfaceIndex surfaceIndex;
  // Browser navigation deliberately excludes moving platforms, actors and physical bodies.
  public DynamicFrame dynamicSurfaces;
  public List<RoomSurface> bodySurfaces;
 }
 public sealed class DynamicFrame
 {
  public int Count=>0;
  public readonly struct Entry {public RoomSurface Current=>throw new NotSupportedException();}
  public Entry Pose(int index)=>throw new NotSupportedException();
 }
}
