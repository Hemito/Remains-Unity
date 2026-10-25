using System;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;
using Remains.Content;

namespace Remains.Editor
{
    public static class ShotArtworkAuthoring
    {
        private const string Folder="Assets/Remains/Resources/Presentation/Shots";
        public static void Run()
        {
            if(System.IO.Directory.Exists(Folder)) throw new InvalidOperationException("Папка графики выстрелов уже существует; миграция отменена.");
            System.IO.Directory.CreateDirectory(Folder);AssetDatabase.Refresh();
            var bullet=ScriptableObject.CreateInstance<ShotArtwork>();bullet.name="bullet";
            bullet.weapons=new[]{"pistol","npc-shot"};bullet.trace=Frame("tracer",new Rect(2,8,92,3));
            bullet.impact=new[]{Frame("impact-1",new Rect(45,0,47,22),true),Frame("impact-2",new Rect(64,5,25,10),true)};
            bullet.speed=90;bullet.length=3;bullet.Validate();AssetDatabase.CreateAsset(bullet,Folder+"/bullet.asset");
            var laser=ScriptableObject.CreateInstance<ShotArtwork>();laser.name="laser";
            laser.weapons=new[]{"energy"};laser.trace=Frame("laser",new Rect(0,1,100,2));
            laser.impact=Array.Empty<Sprite>();laser.beam=true;laser.lifetime=4f/30;
            laser.Validate();AssetDatabase.CreateAsset(laser,Folder+"/laser.asset");AssetDatabase.SaveAssets();
        }
        private static Sprite Frame(string id,Rect region,bool tip=false)
        {
            var texture=GameAssets.Load<Texture2D>("Original/Shots/"+id);
            if(texture==null) throw new InvalidOperationException("Нет исходной графики выстрела: "+id);
            var sprite=Sprite.Create(texture,region,new Vector2(tip?1:.5f,.5f),50);sprite.name=id;
            AssetDatabase.CreateAsset(sprite,Folder+"/"+id+"-sprite.asset");return sprite;
        }
    }

    [CustomEditor(typeof(ShotArtwork))]
    public sealed class ShotArtworkInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("weapons"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty(
                serializedObject.FindProperty("legacyTrace").objectReferenceValue!=null?"legacyTrace":"traceReference"));
            var beam=serializedObject.FindProperty("beam");EditorGUILayout.PropertyField(beam);
            if(beam.boolValue) EditorGUILayout.PropertyField(serializedObject.FindProperty("lifetime"));
            else
            {
                EditorGUILayout.HelpBox("Скорость здесь задаёт только визуальный след мгновенной атаки. Скорость физического снаряда задаётся в определении оружия.",MessageType.Info);
                foreach(string field in new[]{"speed","length",serializedObject.FindProperty("keyedImpacts").boolValue?"impactReferences":"legacyImpact","impactFramesPerSecond"})
                    EditorGUILayout.PropertyField(serializedObject.FindProperty(field));

            }
            serializedObject.ApplyModifiedProperties();
        }
    }
}
