using System;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class LampBreakEffectAuthoring
    {
        public static void Generate()
        {
            const string folder="Assets/Remains/Resources/Presentation/Effects";
            const string path=folder+"/lamp-break.prefab";
            if(AssetDatabase.LoadAssetAtPath<LampBreakEffectView>(path)!=null) return;
            var shader=Shader.Find("Sprites/Default")??throw new InvalidOperationException("Нет шейдера частиц Sprites/Default.");
            var material=new Material(shader){name="Осколки лампы"};
            AssetDatabase.CreateAsset(material,folder+"/lamp-break.mat");
            var root=new GameObject("Разбитая лампа");
            try
            {
                var particles=root.AddComponent<ParticleSystem>();particles.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
                var main=particles.main;main.loop=false;main.playOnAwake=false;main.duration=.1f;
                main.startLifetime=new ParticleSystem.MinMaxCurve(.2f,.5f);main.startSpeed=new ParticleSystem.MinMaxCurve(.7f,2f);
                main.startSize=new ParticleSystem.MinMaxCurve(.015f,.035f);main.startColor=new Color(.8f,.85f,.9f,.8f);
                main.gravityModifier=1;main.simulationSpace=ParticleSystemSimulationSpace.World;main.stopAction=ParticleSystemStopAction.Destroy;
                var emission=particles.emission;emission.rateOverTime=0;emission.SetBursts(new[]{new ParticleSystem.Burst(0,12)});
                var shape=particles.shape;shape.shapeType=ParticleSystemShapeType.Circle;shape.radius=.04f;
                var renderer=particles.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=material;
                var view=root.AddComponent<LampBreakEffectView>();view.Particles=particles;
                view.Sound=Resources.Load<AudioClip>("Original/Audio/hit-glass")??throw new InvalidOperationException("Нет звука Original/Audio/hit-glass.");
                view.Validate();PrefabUtility.SaveAsPrefabAsset(root,path);AssetDatabase.SaveAssets();
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
        }
    }
}
