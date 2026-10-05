using System;
using UnityEngine;
namespace Remains.Content
{
    [CreateAssetMenu(menuName="Remains/Атлас персонажа",fileName="ActorAtlas")]
    public sealed class AtlasSpriteAsset : ScriptableObject
    {
        [SerializeField,InspectorName("Текстура атласа")] private Texture2D texture;
        [SerializeField,InspectorName("Описание кадров")] private AtlasActorArtwork definition;
        [SerializeField,HideInInspector] private Sprite[] frames;
        [SerializeField,HideInInspector] private string bakedDefinition;
        public Texture2D Texture=>texture;
        private void OnValidate()
        {
            if(definition?.clips==null) return;
            foreach(var clip in definition.clips)
                if(clip!=null) clip.loopStartFrame=clip.loop?Mathf.Clamp(clip.loopStartFrame,0,Mathf.Max(0,clip.count-1)):0;
        }
        public AtlasActorArtwork CreateDefinition()=>JsonUtility.FromJson<AtlasActorArtwork>(JsonUtility.ToJson(definition));
        public void Configure(Texture2D source,AtlasActorArtwork data) {texture=source;definition=data;}
        public void SetFrames(Sprite[] value) {frames=(Sprite[])value.Clone();bakedDefinition=JsonUtility.ToJson(definition);}
        public Sprite[] Frames(string pose)
        {
            // Normalize absent optional fields in an older bake; changed socket data still invalidates it.
            string normalizedBake=string.IsNullOrEmpty(bakedDefinition)?null:JsonUtility.ToJson(JsonUtility.FromJson<AtlasActorArtwork>(bakedDefinition));
            if(texture==null || frames==null || normalizedBake!=JsonUtility.ToJson(definition))
                throw new InvalidOperationException($"Атлас {name}: кадры не собраны или устарели. Нажмите «Пересобрать кадры» в Inspector.");
            int offset=0;
            foreach(var clip in definition.clips)
            {
                if(clip.pose==pose)
                {
                    if(offset+clip.count>frames.Length) throw new InvalidOperationException($"Атлас {name}: не хватает кадров {pose}.");
                    var result=new Sprite[clip.count];Array.Copy(frames,offset,result,0,clip.count);
                    foreach(var sprite in result)
                        if(sprite==null || sprite.texture!=texture) throw new InvalidOperationException($"Атлас {name}: повреждён кадр {pose}.");
                    return result;
                }
                offset+=clip.count;
            }
            throw new InvalidOperationException($"Атлас {name}: нет позы {pose}.");
        }
    }
}
