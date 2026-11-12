using System;
using System.Collections;
using System.IO;
using Remains.Runtime;
using Unity.Collections;
using UnityEditor;
using UnityEditor.Media;
using UnityEngine;

namespace Remains.Editor
{
    // Editor-only diagnostic capture. The production scene and gameplay remain authoritative.
    [InitializeOnLoad]
    public static class BackgroundGameplayCapture
    {
        private const string Key="Remains.BackgroundGameplayCapture";
        static BackgroundGameplayCapture()
        {
            EditorApplication.playModeStateChanged+=state=>
            {
                if(state!=PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key,false)) return;
                SessionState.SetBool(Key,false);
                // Explicit diagnostic lookup; never used by production scene composition.
                var runtime=UnityEngine.Object.FindFirstObjectByType<PrototypeRuntime>();
                if(runtime==null) {Debug.LogError("Фоновая запись: в игровой сцене нет PrototypeRuntime.");EditorApplication.Exit(1);return;}
                runtime.gameObject.AddComponent<BackgroundGameplayCaptureDriver>().Initialize(runtime);
            };
        }
        public static void RunBatch()
        {
            SessionState.SetBool(Key,true);
            Seed104CpuBaselineChecks.RunBatch();
        }
    }

    public sealed class BackgroundGameplayCaptureDriver : MonoBehaviour
    {
        private const int Fps=30, Width=1280, Height=720, FrameCount=240;
        private const uint VideoBitRate=20000000;
        private PrototypeRuntime runtime;
        private MediaEncoder encoder;
        private Texture2D videoFrame;
        private string output;
        private bool recording,finished;
        private int frames,attacks,oldRate;
        private long samples;
        private double squareSum;
        private float peak;
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;
            output=Seed104CpuBaselineProbe.Argument("--capture-output",null);
            if(string.IsNullOrWhiteSpace(output) || Directory.Exists(output))
                throw new InvalidOperationException("Фоновая запись требует --capture-output с новым каталогом.");
            Directory.CreateDirectory(output);
            StartCoroutine(Capture());
        }
        private IEnumerator Capture()
        {
            double deadline=Time.realtimeSinceStartupAsDouble+480;
            int ready=0;
            while(ready<90)
            {
                if(Time.realtimeSinceStartupAsDouble>deadline) {Fail(new TimeoutException("Фоновая запись: завод не готов за 480 секунд."));yield break;}
                ready=runtime.Session?.LocalActor?.level=="factory:0" && runtime.LevelPresentationReady && !runtime.ReleasingUnusedAssets?ready+1:0;
                yield return null;
            }
            try {Begin();} catch(Exception error) {Fail(error);yield break;}
            while(frames<FrameCount)
            {
                yield return null;
                try {CaptureFrame();} catch(Exception error) {Fail(error);yield break;}
            }
            try
            {
                Close();
                if(samples==0 || peak<.0001f || attacks<3) throw new InvalidOperationException($"Неполная запись: samples={samples}, peak={peak}, attacks={attacks}.");
                File.WriteAllText(Path.Combine(output,"capture.json"),JsonUtility.ToJson(new Report
                {success=true,frames=frames,fps=Fps,width=Width,height=Height,seconds=(float)frames/Fps,
                    audioSamples=samples,audioPeak=peak,audioRms=(float)Math.Sqrt(squareSum/samples),attacks=attacks,
                    level=runtime.Session.LocalActor.level},true));
                Debug.Log($"Фоновая запись завершена: {frames} кадров, {samples} аудиосэмплов, {attacks} атак; {output}.");
                finished=true;EditorApplication.Exit(0);
            }
            catch(Exception error) {Fail(error);}
        }
        private void Begin()
        {
            oldRate=Time.captureFramerate;Time.captureFramerate=Fps;
            var config=AudioSettings.GetConfiguration();
            if(config.speakerMode!=AudioSpeakerMode.Stereo) throw new InvalidOperationException("Пробная запись ожидает стереомикс: "+config.speakerMode);
            encoder=new MediaEncoder(Path.Combine(output,"gameplay.mp4"),
                new VideoTrackEncoderAttributes(new H264EncoderAttributes
                    {gopSize=Fps,numConsecutiveBFrames=2,profile=VideoEncodingProfile.H264High})
                    {frameRate=new MediaRational(Fps),width=Width,height=Height,includeAlpha=false,targetBitRate=VideoBitRate},
                new AudioTrackAttributes {sampleRate=new MediaRational(config.sampleRate),channelCount=2,language="ru"});
            videoFrame=new Texture2D(Width,Height,TextureFormat.RGBA32,false);
            if(!AudioRenderer.Start()) throw new InvalidOperationException("Unity не смог включить запись игрового аудиомикса.");
            recording=true;
            runtime.Audio.SetOfflineCapture(true);
            runtime.Session.AttackReceived+=Attack;
            Debug.Log("Фоновая запись: начало 8 секунд настоящей стрельбы на заводе seed104.");
        }
        private void Attack(Remains.Domain.AttackEvent attack)
        {if(attack.attacker==runtime.Session.LocalActorId) attacks++;}
        private void CaptureFrame()
        {
            // Explicit coroutine capture, not production Update/LateUpdate work.
            string path=Path.Combine(output,"frame.png");
            NetworkFrameCapture.Save(runtime.WorldCamera,path,runtime.Hud.GetComponent<Canvas>(),Width,Height);
            var image=new Texture2D(Width,Height,TextureFormat.RGBA32,false);
            try
            {
                if(!image.LoadImage(File.ReadAllBytes(path))) throw new InvalidOperationException("Не удалось прочитать кадр: "+path);
                // PNG decoding uses ARGB32; MediaEncoder requires an explicit RGBA32 frame.
                videoFrame.SetPixels32(image.GetPixels32());videoFrame.Apply(false);
                if(!encoder.AddFrame(videoFrame)) throw new InvalidOperationException("Кодировщик отклонил кадр "+frames);
                if(frames==0 || frames==FrameCount/2 || frames==FrameCount-1)
                    File.Copy(path,Path.Combine(output,$"preview-{frames:D3}.png"),false);
            }
            finally {Destroy(image);}
            int count=AudioRenderer.GetSampleCountForCaptureFrame()*2;
            using(var buffer=new NativeArray<float>(count,Allocator.Temp))
            {
                if(!AudioRenderer.Render(buffer)) throw new InvalidOperationException("Не удалось получить игровой аудиомикс.");
                for(int i=0;i<count;i++) {float sample=buffer[i];peak=Math.Max(peak,Math.Abs(sample));squareSum+=sample*sample;}
                if(count>0 && !encoder.AddSamples(buffer)) throw new InvalidOperationException("Кодировщик отклонил игровой звук.");
            }
            samples+=count;frames++;
        }
        private void Close()
        {
            if(recording) {runtime.Audio.SetOfflineCapture(false);AudioRenderer.Stop();recording=false;runtime.Session.AttackReceived-=Attack;}
            encoder?.Dispose();encoder=null;Time.captureFramerate=oldRate;
            if(videoFrame!=null) {Destroy(videoFrame);videoFrame=null;}
        }
        private void Fail(Exception error)
        {Debug.LogException(error);Close();finished=true;EditorApplication.Exit(1);}
        private void OnDisable() {if(!finished) Close();}
        [Serializable] private sealed class Report
        {
            public bool success;public int frames,fps,width,height,attacks;
            public float seconds,audioPeak,audioRms;public long audioSamples;public string level;
        }
    }
}
