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
        public AtlasActorArtwork CreateDefinition()=>JsonUtility.FromJson<AtlasActorArtwork>(JsonUtility.ToJson(definition));
        public void Configure(Texture2D source,AtlasActorArtwork data) {texture=source;definition=data;}
        public void SetFrames(Sprite[] value) {frames=(Sprite[])value.Clone();bakedDefinition=JsonUtility.ToJson(definition);}
        public Sprite[] Frames(string pose)
        {
            if(texture==null || frames==null || bakedDefinition!=JsonUtility.ToJson(definition))
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
