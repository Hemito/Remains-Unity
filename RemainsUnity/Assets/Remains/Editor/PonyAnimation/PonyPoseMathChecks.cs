using System;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Remains.Editor
{
    // Headless editor checks; no scene, imported artwork, prefab or user asset is changed.
    public static class PonyPoseMathChecks
    {
        public static void Run()
        {
            var document=ScriptableObject.CreateInstance<PonyPoseDocument>();PonyPoseDocument restored=null;
            try
            {
                document.joints=new PonyPoseJoint[17];
                for(int i=0;i<17;i++)document.joints[i]=new PonyPoseJoint {id="joint"+i,label="Сустав "+i,socket="socket"+i,parent=i==0?-1:0,
                    rest=new PonyPoseValue {position=i==0?new Vector2(.2f,.3f):new Vector2(.03f*i,.02f),angle=i==0?35:0,scale=Vector2.one}};
                document.joints[1].rest.position=new Vector2(.5f,0);document.joints[2].parent=1;document.joints[2].rest.position=new Vector2(.4f,0);
                var rest=PonyPoseMath.Rest(document);document.SetKey(0,rest);document.SetKey(1,rest);document.Validate();
                var original=PonyPoseMath.Worlds(document,rest);var deltas=PonyPoseMath.SocketDeltas(document,rest);
                foreach(var matrix in deltas.Values)Require(MatrixError(matrix,Matrix4x4.identity)<.00001f,"исходная поза сдвигает графику");

                var moved=PonyPoseMath.ClonePose(rest);PonyPoseMath.MoveJoint(document,moved,1,new Vector2(.7f,.9f));
                Require(Vector2.Distance(PonyPoseMath.Position(PonyPoseMath.Worlds(document,moved)[1]),new Vector2(.7f,.9f))<.00001f,"мировое перемещение потеряно под повёрнутым родителем");
                Require(rest[1].position!=moved[1].position,"редактирование изменило исходный массив");
                var rotated=PonyPoseMath.ClonePose(rest);rotated[0].scale=new Vector2(2,1);
                PonyPoseMath.RotateJoint(document,rotated,1,80);var rotatedWorld=PonyPoseMath.Worlds(document,rotated)[1];
                Require(Mathf.Abs(Mathf.DeltaAngle(Mathf.Atan2(rotatedWorld.m10,rotatedWorld.m00)*Mathf.Rad2Deg,80))<.001f,"мировой поворот под неравномерным родителем потерян");

                Vector2 origin=PonyPoseMath.Position(original[0]),target=origin+new Vector2(.5f,.3f);float[] sides=new float[2];
                for(int n=0;n<2;n++)
                {
                    var pose=PonyPoseMath.ClonePose(rest);Vector2 reached=PonyPoseMath.SolveTwoBone(document,pose,0,1,2,target,n==0?1:-1);
                    var world=PonyPoseMath.Worlds(document,pose);var elbow=PonyPoseMath.Position(world[1]);
                    Require(Vector2.Distance(reached,target)<.0001f,"IK не достиг доступной точки");
                    Require(Mathf.Abs(Vector2.Distance(origin,elbow)-.5f)<.0001f && Mathf.Abs(Vector2.Distance(elbow,reached)-.4f)<.0001f,"IK меняет длину ноги");
                    Vector2 a=elbow-origin,b=target-origin;sides[n]=a.x*b.y-a.y*b.x;
                }
                Require(sides[0]*sides[1]<0,"сторона сгиба IK не меняется");
                var stretched=PonyPoseMath.ClonePose(rest);var far=PonyPoseMath.SolveTwoBone(document,stretched,0,1,2,origin+new Vector2(100,0),1);
                Require(Mathf.Abs(Vector2.Distance(origin,far)-.9f)<.0001f,"недоступная цель растягивает ногу");
                var squashed=PonyPoseMath.ClonePose(rest);squashed[0].scale=new Vector2(2,1);var before=PonyPoseMath.ClonePose(squashed);
                Reject(()=>PonyPoseMath.SolveTwoBone(document,squashed,0,1,2,target,1),"IK принял неравномерный масштаб");
                Require(squashed[0].angle==before[0].angle && squashed[1].angle==before[1].angle,"отклонённый IK повредил позу");

                var first=PonyPoseMath.ClonePose(rest);first[0].angle=170;var last=PonyPoseMath.ClonePose(rest);last[0].angle=-170;last[0].position+=Vector2.right;
                document.SetKey(0,first);document.SetKey(1,last);var half=document.Sample(.5f);
                Require(Mathf.Abs(Mathf.Abs(half[0].angle)-180)<.001f && Mathf.Abs(half[0].position.x-(first[0].position.x+.5f))<.00001f,"интерполяция ключей идёт длинным путём/теряет сдвиг");
                document.SetKey(.1f,first);document.SetKey(.101f,last);Require(document.keys.Length==3,"сетка времени создаёт дубли ключей");
                Require(document.Sample(document.duration)[0].angle==last[0].angle,"точный конец клипа не показывает конечный ключ");
                restored=PonyPoseDocument.FromJson(document.ToJson());
                Require(restored.keys.Length==document.keys.Length && restored.joints.Length==17 && restored.keys[1].values[0].angle==last[0].angle,"JSON потерял ключи/суставы");
                restored.keys[0].values[0].position=Vector2.one*10;Require(document.keys[0].values[0].position!=restored.keys[0].values[0].position,"JSON-копия разделяет изменяемые массивы");
                var missing=JObject.Parse(document.ToJson());missing.Remove("sourceClip");Reject(()=>PonyPoseDocument.FromJson(missing.ToString()),"JSON без исходного рисунка принят");
                document.fps=24;Require(document.SnapTime(.1f)==.1f,"смена FPS делает существующий ключ недоступным");
                document.duration=1.01f;document.SetKey(document.duration,last);Require(document.SnapTime(1.01f)==1.01f,"конечный ключ округляется к сетке FPS");
                document.joints[0].parent=2;Reject(document.Validate,"цикл родителей принят");document.joints[0].parent=-1;
                var invalid=PonyPoseMath.ClonePose(rest);invalid[0].angle=float.NaN;Reject(()=>document.SetKey(.5f,invalid),"NaN принят в ключ");
                Debug.Log("Математика редактора пони: PASS — исходные матрицы, FK, мировые handles, обе стороны IK, длины/дальность, отказ масштаба, ключи/сетка/конец, JSON, цикл и NaN.");
            }
            finally{Object.DestroyImmediate(document);if(restored!=null)Object.DestroyImmediate(restored);}
        }
        private static float MatrixError(Matrix4x4 a,Matrix4x4 b){float max=0;for(int i=0;i<16;i++)max=Mathf.Max(max,Mathf.Abs(a[i]-b[i]));return max;}
        private static void Require(bool result,string reason){if(!result)throw new InvalidOperationException("Проверка редактора пони: "+reason+".");}
        private static void Reject(Action action,string reason){try{action();}catch(InvalidOperationException){return;}throw new InvalidOperationException("Проверка редактора пони: "+reason+".");}
    }
}
