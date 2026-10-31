using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remains.Editor
{
    public static class PonyPoseMath
    {
        public static PonyPoseValue[] ClonePose(PonyPoseValue[] values)=>values==null?
            throw PonyPoseDocument.Error("поза отсутствует"):(PonyPoseValue[])values.Clone();

        public static PonyPoseValue[] Rest(PonyPoseDocument document)
        {
            document.ValidateSkeleton();var result=new PonyPoseValue[document.joints.Length];
            for(int i=0;i<result.Length;i++)result[i]=document.joints[i].rest;return result;
        }

        public static Matrix4x4[] Worlds(PonyPoseDocument document,PonyPoseValue[] values)
        {
            document.ValidateValues(values,"расчёт позы");
            var matrices=new Matrix4x4[values.Length];var states=new byte[values.Length];
            for(int i=0;i<values.Length;i++)Resolve(i);return matrices;
            Matrix4x4 Resolve(int i)
            {
                if(states[i]==2)return matrices[i];
                if(states[i]==1)throw PonyPoseDocument.Error("цикл родителей у "+document.joints[i].id);
                states[i]=1;int parent=document.joints[i].parent;
                if(parent<-1 || parent>=values.Length || parent==i)throw PonyPoseDocument.Error("неверный родитель у "+document.joints[i].id);
                matrices[i]=(parent<0?Matrix4x4.identity:Resolve(parent))*values[i].Matrix;
                for(int cell=0;cell<16;cell++)if(!PonyPoseDocument.Finite(matrices[i][cell]))
                    throw PonyPoseDocument.Error("переполнение матрицы сустава "+document.joints[i].id);
                double determinant=(double)matrices[i].m00*matrices[i].m11-(double)matrices[i].m01*matrices[i].m10;
                if(Math.Abs(determinant)<1e-10)throw PonyPoseDocument.Error("вырожденная мировая матрица сустава "+document.joints[i].id);
                states[i]=2;return matrices[i];
            }
        }

        public static Dictionary<string,Matrix4x4> SocketDeltas(PonyPoseDocument document,PonyPoseValue[] values)
        {
            var current=Worlds(document,values);var original=Worlds(document,Rest(document));
            var result=new Dictionary<string,Matrix4x4>(StringComparer.Ordinal);
            for(int i=0;i<current.Length;i++)if(!string.IsNullOrEmpty(document.joints[i].socket))
                result.Add(document.joints[i].socket,current[i]*original[i].inverse);
            return result;
        }

        public static void MoveJoint(PonyPoseDocument document,PonyPoseValue[] values,int index,Vector2 worldPoint)
        {
            RequireIndex(document,index);RequirePoint(worldPoint);var worlds=Worlds(document,values);
            int parent=document.joints[index].parent;
            values[index].position=parent<0?worldPoint:(Vector2)worlds[parent].inverse.MultiplyPoint3x4(worldPoint);
        }

        public static void RotateJoint(PonyPoseDocument document,PonyPoseValue[] values,int index,float worldAngle)
        {
            RequireIndex(document,index);
            if(!PonyPoseDocument.Finite(worldAngle))throw PonyPoseDocument.Error("неконечный угол сустава "+document.joints[index].id);
            var worlds=Worlds(document,values);int parent=document.joints[index].parent;
            // A world-space direction is transformed by the complete parent inverse, including nonuniform scale.
            Vector3 direction=Quaternion.Euler(0,0,worldAngle)*Vector3.right;
            if(parent>=0)direction=worlds[parent].inverse.MultiplyVector(direction);
            float local=Mathf.Atan2(direction.y,direction.x)*Mathf.Rad2Deg;
            values[index].angle+=Mathf.DeltaAngle(values[index].angle,local);
        }

        // Keeps lengths and local offsets. The returned target is the reachable point, not necessarily the requested point.
        // For three foreleg artwork segments the endpoint can be the ankle or an auxiliary hoof joint.
        public static Vector2 SolveTwoBone(PonyPoseDocument document,PonyPoseValue[] values,int root,int middle,int end,Vector2 worldTarget,int bendSign)
        {
            RequireIndex(document,root);RequireIndex(document,middle);RequireIndex(document,end);RequirePoint(worldTarget);
            if(document.joints[middle].parent!=root || document.joints[end].parent!=middle)
                throw PonyPoseDocument.Error("IK требует прямую цепь корень → сустав → конец");
            if(bendSign!=1 && bendSign!=-1)throw PonyPoseDocument.Error("сторона сгиба IK должна быть −1 или 1");
            var worlds=Worlds(document,values);
            for(int i=middle;i>=0;i=document.joints[i].parent)
                if(Mathf.Abs(values[i].scale.x-values[i].scale.y)>.0001f*Mathf.Max(values[i].scale.x,values[i].scale.y))
                    throw PonyPoseDocument.Error("IK требует равномерного масштаба у "+document.joints[i].id+"; вращение и ручное перемещение остаются доступны");
            Vector2 origin=Position(worlds[root]),joint=Position(worlds[middle]),tip=Position(worlds[end]);
            float a=Vector2.Distance(origin,joint),b=Vector2.Distance(joint,tip);
            if(a<.0001f || b<.0001f)throw PonyPoseDocument.Error("IK получил звено нулевой длины");
            Vector2 offset=worldTarget-origin;
            // The folded singularity has infinitely many solutions; preserve the current first-bone direction.
            Vector2 direction=offset.sqrMagnitude>1e-12f?offset.normalized:(joint-origin)/a;
            float distance=Mathf.Clamp(offset.magnitude,Mathf.Abs(a-b)+.000001f,a+b-.000001f);
            Vector2 reached=origin+direction*distance;
            double cosine=((double)a*a+(double)distance*distance-(double)b*b)/(2*(double)a*distance);
            float opening=(float)Math.Acos(Math.Max(-1,Math.Min(1,cosine)));
            float targetAngle=Mathf.Atan2(direction.y,direction.x);
            float upperAngle=targetAngle-bendSign*opening;
            Vector2 knee=origin+new Vector2(Mathf.Cos(upperAngle),Mathf.Sin(upperAngle))*a;
            Vector2 firstOffset=Vector2.Scale(values[root].scale,values[middle].position);
            float rootWorldAngle=upperAngle*Mathf.Rad2Deg-Mathf.Atan2(firstOffset.y,firstOffset.x)*Mathf.Rad2Deg;
            RotateJoint(document,values,root,rootWorldAngle);
            Vector2 lower=reached-knee;Vector2 secondOffset=Vector2.Scale(values[middle].scale,values[end].position);
            float middleWorldAngle=Mathf.Atan2(lower.y,lower.x)*Mathf.Rad2Deg-Mathf.Atan2(secondOffset.y,secondOffset.x)*Mathf.Rad2Deg;
            RotateJoint(document,values,middle,middleWorldAngle);
            return Position(Worlds(document,values)[end]);
        }

        public static Vector2 Position(Matrix4x4 matrix)=>new(matrix.m03,matrix.m13);

        public static bool CanReanchor(PonyPoseDocument document)
        {
            if(document.keys==null || document.keys.Length!=1 || document.keys[0].time!=0)return false;
            for(int i=0;i<document.joints.Length;i++)
            {
                var a=document.keys[0].values[i];var b=document.joints[i].rest;
                if((a.position-b.position).sqrMagnitude>1e-10f || Mathf.Abs(a.angle-b.angle)>.00001f || (a.scale-b.scale).sqrMagnitude>1e-10f)return false;
            }
            return true;
        }

        // Calibrate the control origin before animating. Artwork and every other joint stay put.
        public static void Reanchor(PonyPoseDocument document,int index,Vector2 worldPoint)
        {
            document.Validate();RequireIndex(document,index);RequirePoint(worldPoint);
            if(!CanReanchor(document))throw PonyPoseDocument.Error("точки сгиба настраиваются только в исходной стойке с единственным ключом 0");
            var worlds=Worlds(document,Rest(document));worlds[index].m03=worldPoint.x;worlds[index].m13=worldPoint.y;
            var positions=new Vector2[worlds.Length];
            for(int i=0;i<positions.Length;i++)
            {
                int parent=document.joints[i].parent;
                positions[i]=parent<0?Position(worlds[i]):(Vector2)worlds[parent].inverse.MultiplyPoint3x4(Position(worlds[i]));
                RequirePoint(positions[i]);
            }
            for(int i=0;i<positions.Length;i++)document.joints[i].rest.position=positions[i];
            document.SetKey(0,Rest(document));
        }
        private static void RequireIndex(PonyPoseDocument document,int index)
        {
            if(document==null || document.joints==null || index<0 || index>=document.joints.Length)
                throw PonyPoseDocument.Error("неверный индекс сустава "+index);
        }
        private static void RequirePoint(Vector2 point)
        {
            if(!PonyPoseDocument.Finite(point.x) || !PonyPoseDocument.Finite(point.y))throw PonyPoseDocument.Error("неконечная точка управления");
        }
    }
}
