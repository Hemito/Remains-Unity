using System;
using System.Collections.Generic;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Editor
{
    // Offline path preparation from the SAME fine absorption the GPU light uses. Never runs per rendered frame.
    internal sealed class FactoryWispPath
    {
        internal const int Frames=48;
        internal static float Phase(int frame,int source)=>(float)frame/Frames+(source==0?0:source==1?.29f:.63f);
        private readonly List<Vector2> points=new();
        private readonly float[] cumulative;
        internal IReadOnlyList<Vector2> Points=>points;
        internal FactoryWispPath(LevelLightScene scene,float[] absorption,Rect room,Bounds actors)
        {
            const float cell=.2f;int w=Mathf.FloorToInt(room.width/cell),h=Mathf.FloorToInt(room.height/cell);
            if(w<4 || h<4)throw new InvalidOperationException("Комната слишком мала для виспов: "+room);
            var open=new bool[w*h];var origin=room.position;
            Vector2 At(int i)=>origin+new Vector2((i%w+.5f)*cell,(i/w+.5f)*cell);
            bool Free(Vector2 p)
            {
                foreach(var d in new[]{Vector2.zero,new Vector2(.07f,0),new Vector2(-.07f,0),new Vector2(0,.07f),new Vector2(0,-.07f)})
                {
                    int x=Mathf.FloorToInt((p.x+d.x-scene.WorldRect.x)*scene.Resolution),y=Mathf.FloorToInt((p.y+d.y-scene.WorldRect.y)*scene.Resolution);
                    if(x<0 || y<0 || x>=scene.Width || y>=scene.Height || absorption[y*scene.Width+x]>=.99f)return false;
                }
                return true;
            }
            for(int i=0;i<open.Length;i++)open[i]=Free(At(i));
            int Nearest(Vector2 p)
            {
                int best=-1;float distance=float.PositiveInfinity;
                for(int i=0;i<open.Length;i++)if(open[i] && (At(i)-p).sqrMagnitude<distance){distance=(At(i)-p).sqrMagnitude;best=i;}
                if(best<0)throw new InvalidOperationException("В комнате нет свободного места для источника: "+room);return best;
            }
            float low=Mathf.Clamp(actors.center.y,room.yMin+.8f,room.yMax-.8f);
            var target=new Vector2(room.xMax-1,Mathf.Min(room.yMax-.8f,low+1.4f));
            // Anchor the reachable component to the actual NPC group, not an isolated alcove
            // near the template's left edge that cannot illuminate any of those actors.
            int anchor=Nearest(new Vector2(actors.center.x,low));
            var component=new bool[open.Length];var explore=new Queue<int>();explore.Enqueue(anchor);component[anchor]=true;
            while(explore.Count>0)
            {
                int p=explore.Dequeue(),x=p%w,y=p/w;
                foreach(int n in new[]{x>0?p-1:-1,x+1<w?p+1:-1,y>0?p-w:-1,y+1<h?p+w:-1})
                    if(n>=0 && open[n] && !component[n]){component[n]=true;explore.Enqueue(n);}
            }
            int Endpoint(Vector2 requested)
            {
                int chosen=-1;float best=float.PositiveInfinity;
                for(int i=0;i<component.Length;i++)if(component[i] && (At(i)-requested).sqrMagnitude<best)
                {best=(At(i)-requested).sqrMagnitude;chosen=i;}
                if(chosen<0)throw new InvalidOperationException("Нет связного воздушного пространства возле NPC: "+room);return chosen;
            }
            int start=Endpoint(new Vector2(room.xMin+1,low)),end=Endpoint(target);
            var previous=new int[open.Length];Array.Fill(previous,-1);var queue=new Queue<int>();queue.Enqueue(start);previous[start]=start;
            while(queue.Count>0)
            {
                int p=queue.Dequeue(),x=p%w,y=p/w;
                foreach(int n in new[]{x>0?p-1:-1,x+1<w?p+1:-1,y>0?p-w:-1,y+1<h?p+w:-1})
                    if(n>=0 && open[n] && previous[n]<0){previous[n]=p;queue.Enqueue(n);}
            }
            // The demonstration has no authored destination behind a closed wall. Select its endpoint
            // from the reachable air component, never teleport through another room/closed door.
            int desired=end;float best=float.PositiveInfinity;
            for(int i=0;i<previous.Length;i++)if(previous[i]>=0 && (At(i)-target).sqrMagnitude<best)
            {best=(At(i)-target).sqrMagnitude;end=i;}
            if(previous[end]<0)throw new InvalidOperationException("Пустая связная область маршрута виспа: "+room);
            Debug.Log($"Маршрут виспов: комната {room}, начало={At(start)}, ближайшая цель={At(desired)}, достижимый конец={At(end)}, связных клеток={Array.FindAll(previous,p=>p>=0).Length}.");
            for(int p=end;;p=previous[p]){points.Add(At(p));if(p==start)break;}points.Reverse();
            if(points.Count<8)throw new InvalidOperationException("Слишком короткий проверочный путь виспа: "+room);
            cumulative=new float[points.Count];for(int i=1;i<points.Count;i++)cumulative[i]=cumulative[i-1]+Vector2.Distance(points[i-1],points[i]);
            for(int frame=0;frame<Frames;frame++)for(int source=0;source<3;source++)
                if(!Free(Position(Phase(frame,source))))
                    throw new InvalidOperationException($"Путь виспа пересёк непрозрачную геометрию: комната {room}, кадр{frame}, источник{source}.");
        }
        internal Vector2 Position(float phase)
        {
            float t=(1-Mathf.Cos(phase*Mathf.PI*2))*.5f*cumulative[^1];
            int i=Array.BinarySearch(cumulative,t);if(i>=0)return points[i];i=~i;
            if(i<=0)return points[0];if(i>=points.Count)return points[^1];
            return Vector2.Lerp(points[i-1],points[i],(t-cumulative[i-1])/(cumulative[i]-cumulative[i-1]));
        }
    }
}
