using System;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Ordinary native clips: editable by Unity's Animation window, no custom animation player.
    public static class NativePonyRigClips
    {
        public static AnimationClip CreateIdle(Transform root,Transform[] bones)
        {
            var clip=Clip("Пони — стойка",false);
            BindRest(clip,root,bones);return clip;
        }
        public static AnimationClip CreateWalk(Transform root,Transform[] bones)
        {
            var clip=Clip("Пони — ходьба",true);BindRest(clip,root,bones);
            foreach(var leg in new[]{("frontNear",0f),("frontFar",.5f),("hindNear",.5f),("hindFar",0f)})
            {
                var chain=NativePonyRigDefinition.Chain(leg.Item1);
                for(int joint=0;joint<3;joint++)
                {
                    int bone=chain[joint];int jointIndex=joint;float phase=leg.Item2;
                    Rotation(clip,root,bones[bone],t=>
                    {
                        float cycle=(t+phase)*Mathf.PI*2;
                        float swing=Mathf.Sin(cycle),lift=Mathf.Max(0,Mathf.Cos(cycle));
                        return jointIndex switch {0=>swing*17,1=>-lift*28,2=>lift*20,_=>0};
                    });
                }
            }
            Rotation(clip,root,bones[NativePonyRigDefinition.Index("neck")],t=>Mathf.Sin(t*Mathf.PI*4)*2);
            Rotation(clip,root,bones[NativePonyRigDefinition.Index("tailBase")],t=>Mathf.Sin(t*Mathf.PI*2)*4);
            int torso=NativePonyRigDefinition.Index("torso");float rest=bones[torso].localPosition.y;
            Curve(clip,Path(root,bones[torso]),"m_LocalPosition.y",t=>rest+Mathf.Sin(t*Mathf.PI*4)*.012f);
            return clip;
        }
        public static AnimationClip CreateCurl(Transform root,Transform[] bones)
        {
            var clip=Clip("Пони — сгиб ноги",false);BindRest(clip,root,bones);
            var front=NativePonyRigDefinition.Chain("frontNear");
            Rotation(clip,root,bones[front[0]],t=>Pulse(t)*-30);
            Rotation(clip,root,bones[front[1]],t=>Pulse(t)*100);
            Rotation(clip,root,bones[front[2]],t=>Pulse(t)*-35);
            var hind=NativePonyRigDefinition.Chain("hindNear");
            Rotation(clip,root,bones[hind[0]],t=>Pulse(t)*-18);
            Rotation(clip,root,bones[hind[1]],t=>Pulse(t)*55);
            Rotation(clip,root,bones[hind[2]],t=>Pulse(t)*-35);
            return clip;
        }
        private static float Pulse(float t)=>Mathf.Sin(Mathf.Clamp01(t)*Mathf.PI);
        private static AnimationClip Clip(string name,bool loop)
        {
            var clip=new AnimationClip {name=name,frameRate=30};
            var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=loop;AnimationUtility.SetAnimationClipSettings(clip,settings);return clip;
        }
        private static void BindRest(AnimationClip clip,Transform root,Transform[] bones)
        {
            if(root==null || bones==null || bones.Length!=NativePonyRigDefinition.Bones.Length)
                throw new InvalidOperationException("Риг пони: не заданы все кости для анимации.");
            foreach(var bone in bones)
            {
                string path=Path(root,bone);var position=bone.localPosition;var scale=bone.localScale;float angle=Mathf.DeltaAngle(0,bone.localEulerAngles.z);
                Curve(clip,path,"m_LocalPosition.x",_=>position.x);Curve(clip,path,"m_LocalPosition.y",_=>position.y);Curve(clip,path,"m_LocalPosition.z",_=>position.z);
                Curve(clip,path,"localEulerAnglesRaw.z",_=>angle);
                Curve(clip,path,"m_LocalScale.x",_=>scale.x);Curve(clip,path,"m_LocalScale.y",_=>scale.y);
            }
        }
        private static void Rotation(AnimationClip clip,Transform root,Transform bone,Func<float,float> offset)
        {
            float rest=Mathf.DeltaAngle(0,bone.localEulerAngles.z);Curve(clip,Path(root,bone),"localEulerAnglesRaw.z",t=>rest+offset(t));
        }
        private static string Path(Transform root,Transform bone)
        {
            if(bone==null || !bone.IsChildOf(root))throw new InvalidOperationException("Риг пони: анимируемая кость вне корня.");
            return AnimationUtility.CalculateTransformPath(bone,root);
        }
        private static void Curve(AnimationClip clip,string path,string property,Func<float,float> sample)
        {
            var keys=new Keyframe[31];for(int i=0;i<keys.Length;i++)keys[i]=new Keyframe(i/30f,sample(i/30f));
            var curve=new AnimationCurve(keys);
            for(int i=0;i<keys.Length;i++){AnimationUtility.SetKeyLeftTangentMode(curve,i,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(curve,i,AnimationUtility.TangentMode.Linear);}
            AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(path,typeof(Transform),property),curve);
        }
    }
}
