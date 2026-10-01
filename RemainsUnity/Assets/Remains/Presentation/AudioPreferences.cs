using System;
using System.IO;
using Newtonsoft.Json;
using Remains.Persistence;

namespace Remains.Presentation
{
    public sealed class AudioLevels
    {
        [JsonProperty(Required=Required.Always)] public float master=1;
        [JsonProperty(Required=Required.Always)] public float music=1;
        [JsonProperty(Required=Required.Always)] public float effects=1;
        public AudioLevels Copy()=>new() {master=master,music=music,effects=effects};
        public void Validate()
        {
            foreach(float value in new[]{master,music,effects})
                if(!float.IsFinite(value) || value<0 || value>1)
                    throw new InvalidDataException("Громкость должна быть числом от 0 до 1.");
        }
    }

    // Local user preferences. No character/world data and no network replication.
    public sealed class AudioPreferences
    {
        private readonly string path;
        private AudioLevels levels;
        public AudioLevels Current=>levels.Copy();
        public event Action Changed;
        public AudioPreferences(string path)
        {
            this.path=path;levels=new AudioLevels();
            if(path==null || !File.Exists(path)) return;
            try
            {
                levels=JsonConvert.DeserializeObject<AudioLevels>(File.ReadAllText(path));
                if(levels==null) throw new InvalidDataException("Пустые настройки звука.");
                levels.Validate();
            }
            catch(Exception error) {throw new InvalidDataException($"Не удалось прочитать настройки звука: {path}. {error.Message}",error);}
        }
        public void Preview(AudioLevels value)
        {value.Validate();levels=value.Copy();Changed?.Invoke();}
        public void Save()
        {
            if(path!=null) AtomicSaveFile.Write(path,JsonConvert.SerializeObject(levels,Formatting.Indented));
        }
    }
}
