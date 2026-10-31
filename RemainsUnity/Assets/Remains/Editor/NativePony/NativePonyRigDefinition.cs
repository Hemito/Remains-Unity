using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.U2D;

namespace Remains.Editor
{
    // Anatomical authoring controls for the native pilot, independent of the old Flash registration origins.
    public static class NativePonyRigDefinition
    {
        private sealed class Joint
        {
            public readonly string Name;
            public readonly int Parent;
            public readonly Vector2 World;
            public Joint(string name,int parent,float x,float y){Name=name;Parent=parent;World=new Vector2(x,y);}
        }
        private static readonly Joint[] joints=
        {
            new("torso",-1,0,.67f),new("neck",0,.22f,.75f),new("head",1,.28f,1.03f),
            new("tailBase",0,-.20f,.79f),new("tailMiddle",3,-.55f,.66f),new("tailTip",4,-.72f,.28f),
            new("frontNearShoulder",0,.20f,.55f),new("frontNearElbow",6,.19f,.39f),new("frontNearWrist",7,.19f,.26f),new("frontNearHoof",8,.20f,.02f),
            new("frontFarShoulder",0,.26f,.59f),new("frontFarElbow",10,.26f,.43f),new("frontFarWrist",11,.28f,.30f),new("frontFarHoof",12,.28f,.06f),
            new("hindNearHip",0,-.19f,.72f),new("hindNearKnee",14,-.20f,.46f),new("hindNearHock",15,-.29f,.24f),new("hindNearHoof",16,-.30f,-.015f),
            new("hindFarHip",0,-.09f,.73f),new("hindFarKnee",18,-.08f,.47f),new("hindFarHock",19,-.18f,.26f),new("hindFarHoof",20,-.19f,.02f)
        };
        private static readonly Dictionary<string,int[]> chains=new(StringComparer.Ordinal)
        {
            {"frontNear",new[]{6,7,8,9}},{"frontFar",new[]{10,11,12,13}},
            {"hindNear",new[]{14,15,16,17}},{"hindFar",new[]{18,19,20,21}},
            {"tail",new[]{3,4,5}},{"neck",new[]{0,1,2}}
        };

        public static Vector2[] WorldPositions
        {
            get {var result=new Vector2[joints.Length];for(int i=0;i<result.Length;i++)result[i]=joints[i].World;return result;}
        }
        public static SpriteBone[] Bones
        {
            get
            {
                var angles=WorldAngles();var result=new SpriteBone[joints.Length];
                for(int i=0;i<result.Length;i++)
                {
                    var joint=joints[i];int child=Array.FindIndex(joints,j=>j.Parent==i);
                    float parentAngle=joint.Parent<0?0:angles[joint.Parent];
                    Vector2 local=joint.Parent<0?joint.World:(Vector2)(Quaternion.Euler(0,0,-parentAngle)*(joint.World-joints[joint.Parent].World));
                    result[i]=new SpriteBone {name=joint.Name,guid=Guid(joint.Name),parentId=joint.Parent,position=local,
                        rotation=Quaternion.Euler(0,0,angles[i]-parentAngle),length=child<0?.04f:Vector2.Distance(joint.World,joints[child].World)};
                }
                return result;
            }
        }
        public static int Index(string name)
        {
            int index=Array.FindIndex(joints,j=>j.Name==name);
            if(index<0)throw new InvalidOperationException("Риг пони: неизвестная кость "+name+".");return index;
        }
        public static int[] Chain(string group)
        {
            if(!chains.TryGetValue(group,out var chain))throw new InvalidOperationException("Риг пони: неизвестная цепочка "+group+".");
            return (int[])chain.Clone();
        }
        public static Transform[] BuildTransforms(Transform root)
        {
            if(root==null)throw new ArgumentNullException(nameof(root));
            var bones=Bones;var result=new Transform[bones.Length];
            for(int i=0;i<bones.Length;i++)
            {
                var bone=bones[i];var node=new GameObject(bone.name).transform;
                node.SetParent(bone.parentId<0?root:result[bone.parentId],false);node.localPosition=bone.position;node.localRotation=bone.rotation;node.localScale=Vector3.one;result[i]=node;
            }
            return result;
        }
        public static BoneWeight[] Weights(string group,Vector2[] worldVertices)
        {
            if(worldVertices==null)throw new ArgumentNullException(nameof(worldVertices));
            int rigid=group switch {"torso"=>0,"head" or "mane"=>2,_=>-1};
            if(rigid<0 && !chains.ContainsKey(group))throw new InvalidOperationException("Риг пони: нет правила весов для "+group+".");
            var result=new BoneWeight[worldVertices.Length];
            for(int v=0;v<result.Length;v++)
            {
                var point=worldVertices[v];
                if(!float.IsFinite(point.x) || !float.IsFinite(point.y))throw new InvalidOperationException("Риг пони: неконечная вершина весов "+v+".");
                result[v]=rigid>=0?Single(rigid):ChainWeight(group,point);
            }
            return result;
        }
        private static BoneWeight ChainWeight(string group,Vector2 point)
        {
            var chain=chains[group];var lengths=new float[chain.Length-1];var cumulative=new float[chain.Length];
            float best=float.PositiveInfinity,arc=0;int segment=0;
            for(int i=0;i<lengths.Length;i++)
            {
                Vector2 start=joints[chain[i]].World,end=joints[chain[i+1]].World,delta=end-start;
                lengths[i]=delta.magnitude;cumulative[i+1]=cumulative[i]+lengths[i];
                float t=Mathf.Clamp01(Vector2.Dot(point-start,delta)/delta.sqrMagnitude);
                float distance=(point-(start+delta*t)).sqrMagnitude;
                if(distance<best){best=distance;arc=cumulative[i]+lengths[i]*t;segment=i;}
            }
            // The rest geometry remains rigid between joint zones; smooth, overlapping influences near each joint
            // keep neighbouring vertices connected when the native SpriteSkin deforms the sprite.
            float radius=group=="tail"?.13f:group=="neck"?.075f:group.StartsWith("hind",StringComparison.Ordinal)?.065f:.045f;
            // Limb hoof tips are IK effectors, not deforming segments. Their orientation must not twist the foot.
            int influenceCount=group.StartsWith("front",StringComparison.Ordinal) || group.StartsWith("hind",StringComparison.Ordinal)?chain.Length-1:chain.Length;
            for(int i=1;i<influenceCount;i++)
            {
                float width=Mathf.Min(radius,Mathf.Min(lengths[i-1]*.45f,i<lengths.Length?lengths[i]*.45f:radius));
                if(arc<cumulative[i]-width || arc>cumulative[i]+width)continue;
                float t=Mathf.SmoothStep(0,1,(arc-cumulative[i]+width)/(2*width));
                return new BoneWeight {boneIndex0=chain[i-1],weight0=1-t,boneIndex1=chain[i],weight1=t};
            }
            return Single(chain[segment]);
        }
        private static BoneWeight Single(int index)=>new() {boneIndex0=index,weight0=1};
        private static float[] WorldAngles()
        {
            var result=new float[joints.Length];
            for(int i=0;i<result.Length;i++)
            {
                int child=i==0 || i==2?-1:Array.FindIndex(joints,j=>j.Parent==i);
                if(child>=0){var direction=joints[child].World-joints[i].World;result[i]=Mathf.Atan2(direction.y,direction.x)*Mathf.Rad2Deg;}
                else result[i]=i==0 || i==2?0:result[joints[i].Parent];
            }
            return result;
        }
        private static string Guid(string id)
        {
            using var hash=SHA256.Create();var bytes=hash.ComputeHash(Encoding.UTF8.GetBytes("remains-native-pony-v1/"+id));
            var text=new StringBuilder(32);for(int i=0;i<16;i++)text.Append(bytes[i].ToString("x2"));return text.ToString();
        }
    }
}
