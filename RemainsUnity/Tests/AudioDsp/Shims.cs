using System;
// Data/API boundary only; the production preference/transaction code is linked unchanged.
namespace UnityEngine
{
 public enum AudioSpeakerMode {Stereo,Surround}
 public struct AudioConfiguration {public int dspBufferSize,sampleRate,numRealVoices,numVirtualVoices;public AudioSpeakerMode speakerMode;}
 public enum FindObjectsSortMode {None}
 public class Object
 {
  public static T[] FindObjectsByType<T>(FindObjectsSortMode mode)=>throw new InvalidOperationException("Real source search must not be used in fake checks");
 }
 public sealed class AudioClip : Object {}
 public sealed class AudioSource : Object
 {
  public bool isActiveAndEnabled,isPlaying,loop;public AudioClip clip;public int timeSamples;
  public void Play()=>throw new InvalidOperationException("Real playback must not be used in fake checks");
 }
 public static class Debug
 {
  public static void Log(object message)=>Console.WriteLine(message);
  public static void LogWarning(object message)=>throw new InvalidOperationException("Uninjected warning: "+message);
 }
 public static class AudioSettings
 {
  public static AudioConfiguration GetConfiguration()=>throw new InvalidOperationException("Real device must not be used in fake-device checks");
  public static bool Reset(AudioConfiguration value)=>throw new InvalidOperationException("Real device must not be used in fake-device checks");
 }
}
namespace Remains.Persistence
{
 // AtomicSaveFile already has its own native tests. This check injects all writes; accidental fallback must fail.
 public static class AtomicSaveFile {public static void Write(string path,string json)=>throw new InvalidOperationException("Write adapter was not injected");}
}
