using System;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json;
using Remains.Presentation;
using UnityEngine;
class Device : IAudioConfigurationDevice
{
 public AudioConfiguration Config=Initial();public List<AudioConfiguration> Calls=new();public int ReadCount;
 public Func<AudioConfiguration,bool> OnReset;public Action OnRead;
 public static AudioConfiguration Initial()=>new(){dspBufferSize=512,sampleRate=48000,speakerMode=AudioSpeakerMode.Surround,numRealVoices=31,numVirtualVoices=113};
 public AudioConfiguration Read(){ReadCount++;OnRead?.Invoke();return Config;}
 public bool Reset(AudioConfiguration c){Calls.Add(c);if(OnReset!=null)return OnReset(c);Config=c;return true;}
}
class Program
{
 static int checks;
 static void Check(bool ok,string reason){if(!ok)throw new Exception(reason);checks++;}
 static void Main()
 {
  var device=new Device();string persisted=null;var p=new AudioPreferences(null,device,json=>persisted=json);
  Check(p.Current.dspBufferSize==0 && device.ReadCount==0,"constructor touched device/default");
  p.Preview(new AudioLevels{master=.4f,music=.6f,effects=.8f});Check(device.Calls.Count==0,"volume preview reset device");
  Check(p.TrySaveBuffer(256,out _)&&device.Calls.Count==1,"apply failed");
  var c=device.Calls[0];Check(c.dspBufferSize==256&&c.sampleRate==48000&&c.speakerMode==AudioSpeakerMode.Surround&&c.numRealVoices==31&&c.numVirtualVoices==113,"reset lost existing configuration");
  Check(JsonConvert.DeserializeObject<AudioLevels>(persisted).dspBufferSize==256&&p.Current.master==.4f,"saved choice/volume missing");
  p.Preview(new AudioLevels{master=.3f});Check(p.Current.dspBufferSize==256&&device.Calls.Count==1,"preview cleared DSP choice");
  Check(p.TrySaveBuffer(256,out _)&&device.Calls.Count==1,"unchanged choice reset again");
  Check(!p.TrySaveBuffer(128,out _)&&device.Calls.Count==1,"invalid choice reset device");
  foreach(int samples in new[]{512,1024,256})
  {
   Check(p.TrySaveBuffer(samples,out _)&&device.Config.dspBufferSize==samples&&
    JsonConvert.DeserializeObject<AudioLevels>(persisted).dspBufferSize==samples,"supported choice not applied/persisted");
  }
  var reloadDevice=new Device();string root=Path.Combine(Path.GetTempPath(),"remains-audio-dsp-check-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  try
  {
   string file=Path.Combine(root,"audio.json");File.WriteAllText(file,persisted);
   var reload=new AudioPreferences(file,reloadDevice);Check(reloadDevice.Calls.Count==0,"reading persisted settings reset device");
   Check(reload.ApplySavedBuffer(out _)&&reloadDevice.Config.dspBufferSize==256&&reloadDevice.Calls.Count==1,"startup did not restore selection");
   Check(reload.ApplySavedBuffer(out _)&&reloadDevice.Calls.Count==1,"startup repeated reset without change");
   File.WriteAllText(file,"{\"master\":0.7,\"music\":1,\"effects\":0.5}");
   var legacy=new AudioPreferences(file);Check(legacy.Current.dspBufferSize==0&&legacy.ApplySavedBuffer(out _),"legacy JSON forced a device choice");
   File.WriteAllText(file,"{\"master\":1,\"music\":1,\"effects\":1,\"dspBufferSize\":3}");
   bool refused=false;try{_=new AudioPreferences(file);}catch(InvalidDataException){refused=true;}Check(refused,"invalid persisted DSP accepted");
  }finally{Directory.Delete(root,true);}
  device=new Device();int writes=0;device.OnReset=c=>false;p=new AudioPreferences(null,device,_=>writes++);
  Check(!p.TrySaveBuffer(256,out _)&&writes==0&&device.Calls.Count==1&&device.Config.dspBufferSize==512,"rejection persisted/reset-looped");
  foreach(bool throws in new[]{false,true})
  {
   device=new Device();var d=device;d.OnReset=cfg=>{d.Config=cfg;if(d.Calls.Count==1){d.Config.sampleRate=44100;if(throws)throw new InvalidOperationException("partial reset");return false;}return true;};
   p=new AudioPreferences(null,d,_=>writes++);Check(!p.TrySaveBuffer(256,out _)&&d.Config.dspBufferSize==512&&d.Config.sampleRate==48000&&d.Calls.Count==2,"partial failure rollback failed");
  }
  device=new Device();var mismatch=device;mismatch.OnReset=cfg=>{mismatch.Config=cfg;if(mismatch.Calls.Count==1)mismatch.Config.dspBufferSize=1024;return true;};
  p=new AudioPreferences(null,mismatch,_=>writes++);Check(!p.TrySaveBuffer(256,out _)&&mismatch.Config.dspBufferSize==512&&mismatch.Calls.Count==2,"driver mismatch accepted");
  device=new Device();p=new AudioPreferences(null,device,_=>throw new IOException("disk fixture"));
  Check(!p.TrySaveBuffer(256,out string reason)&&device.Config.dspBufferSize==512&&device.Calls.Count==2&&p.Current.dspBufferSize==0&&reason.Contains("сохранить"),"disk failure not rolled back");
  device=new Device();var broken=device;broken.OnReset=cfg=>{if(broken.Calls.Count==1){broken.Config=cfg;return true;}return false;};
  p=new AudioPreferences(null,broken,_=>throw new IOException("disk fixture"));Check(!p.TrySaveBuffer(256,out reason)&&reason.Contains("восстановить не удалось")&&broken.Calls.Count==2,"rollback failure hidden/retried indefinitely");
  device=new Device();device.OnRead=()=>throw new InvalidOperationException("read failed");p=new AudioPreferences(null,device,_=>writes++);
  Check(!p.TrySaveBuffer(256,out _)&&device.Calls.Count==0,"read failure still reset");
  device=new Device();var readFault=device;readFault.OnRead=()=>{if(readFault.ReadCount==2)throw new InvalidOperationException("read after reset failed");};
  p=new AudioPreferences(null,readFault,_=>writes++);Check(!p.TrySaveBuffer(256,out _)&&readFault.Config.dspBufferSize==512&&readFault.Calls.Count==2,"postreset read failure not rolled back");
  Check(writes==0,"failed applications persisted settings");
  Console.WriteLine("AUDIO_DSP_LOGIC_PASS: "+checks+" checks; real device/Unity latency not exercised.");
  Remains.Editor.AudioDspLoopChecks.Run();
 }
}
