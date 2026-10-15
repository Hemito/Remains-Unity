using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Remains.Persistence
{
    // SHA-256 of UTF-8 text fed in pieces (strings, ranges, single chars), hex lowercase. The result equals SHA-256
    // over the UTF-8 bytes of the concatenated text, so a snapshot spliced from parts can be fingerprinted without
    // building the whole string. On Windows the hash is the system CNG implementation (bcrypt.dll, hardware SHA where
    // available): Mono's managed SHA256 took ~17 ms per 1.25 MB factory snapshot on the game thread.
    // One instance per thread (Current); not thread-safe.
    public sealed class TextSha256 : SnapshotPartCache.ITextSink
    {
        private const int BufferBytes=65536;
        private readonly byte[] buffer=new byte[BufferBytes];
        private readonly Encoder encoder=new UTF8Encoding(false).GetEncoder();
        private readonly Hash hash=Hash.Create();
        private int used;

        [ThreadStatic] private static TextSha256 current;
        // A reset hasher of this thread.
        public static TextSha256 Begin() {var hasher=current??=new TextSha256();hasher.Reset();return hasher;}

        private void Reset() {used=0;encoder.Reset();hash.Reset();}

        public void Append(string text)=>Append(text,0,text.Length);
        public void Append(string text,int start,int count)
        {
            while(count>0)
            {
                // At most 3 bytes per UTF-16 unit, plus a carried surrogate.
                int room=(BufferBytes-used-4)/3;
                if(room<=0) {Drain();continue;}
                int chars=Math.Min(room,count);
                used+=encoder.GetBytes(text.AsSpan(start,chars),buffer.AsSpan(used),false);
                start+=chars;count-=chars;
            }
        }
        public void Append(char value)
        {
            if(BufferBytes-used<8) Drain();
            Span<char> one=stackalloc char[1];one[0]=value;
            used+=encoder.GetBytes(one,buffer.AsSpan(used),false);
        }

        public string Finish()
        {
            used+=encoder.GetBytes(ReadOnlySpan<char>.Empty,buffer.AsSpan(used),true);
            Drain();
            var digest=hash.Finish();
            var hex=new char[64];
            const string digits="0123456789abcdef";
            for(int i=0;i<32;i++) {hex[i*2]=digits[digest[i]>>4];hex[i*2+1]=digits[digest[i]&15];}
            return new string(hex);
        }

        private void Drain() {if(used>0) hash.Add(buffer,used);used=0;}

        private abstract class Hash
        {
            public abstract void Reset();
            public abstract void Add(byte[] data,int count);
            public abstract byte[] Finish();   // the digest; valid until the next use
            public static Hash Create()
            {
                if(RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return new CngHash();
                UnityEngine.Debug.LogWarning("SHA-256 снимков: системная реализация есть только на Windows; используется управляемая (медленнее, ~17 мс на снимок завода). Платформа: "+RuntimeInformation.OSDescription);
                return new ManagedHash();
            }
        }

        private sealed class ManagedHash : Hash
        {
            private readonly SHA256 sha=SHA256.Create();
            public override void Reset()=>sha.Initialize();
            public override void Add(byte[] data,int count)=>sha.TransformBlock(data,0,count,null,0);
            public override byte[] Finish() {sha.TransformFinalBlock(buffer0,0,0);var digest=sha.Hash;sha.Initialize();return digest;}
            private static readonly byte[] buffer0=new byte[0];
        }

        // Windows CNG: a reusable hash object (BCRYPT_HASH_REUSABLE_FLAG); finishing resets it. Handles live as long
        // as their thread's hasher (process lifetime for the game and save threads).
        private sealed class CngHash : Hash
        {
            private const uint ReusableFlag=0x20;
            private static readonly IntPtr algorithm=OpenAlgorithm();
            private readonly IntPtr handle;
            private readonly byte[] digest=new byte[32];
            private bool dirty;
            public CngHash() {Check(BCryptCreateHash(algorithm,out handle,IntPtr.Zero,0,IntPtr.Zero,0,ReusableFlag),"BCryptCreateHash");}
            private static IntPtr OpenAlgorithm()
            {
                Check(BCryptOpenAlgorithmProvider(out var value,"SHA256",null,ReusableFlag),"BCryptOpenAlgorithmProvider");
                return value;
            }
            public override void Reset() {if(dirty) Finish();}
            public override void Add(byte[] data,int count) {dirty=true;Check(BCryptHashData(handle,data,count,0),"BCryptHashData");}
            public override byte[] Finish()
            {
                Check(BCryptFinishHash(handle,digest,digest.Length,0),"BCryptFinishHash");
                dirty=false;return digest;
            }
            private static void Check(int status,string call)
            {
                if(status!=0) throw new CryptographicException($"SHA-256 (Windows CNG): {call} вернул 0x{status:X8}.");
            }
            [DllImport("bcrypt.dll",CharSet=CharSet.Unicode)]
            private static extern int BCryptOpenAlgorithmProvider(out IntPtr algorithm,string algorithmId,string implementation,uint flags);
            [DllImport("bcrypt.dll")]
            private static extern int BCryptCreateHash(IntPtr algorithm,out IntPtr hash,IntPtr hashObject,int hashObjectSize,IntPtr secret,int secretSize,uint flags);
            [DllImport("bcrypt.dll")]
            private static extern int BCryptHashData(IntPtr hash,byte[] input,int inputSize,uint flags);
            [DllImport("bcrypt.dll")]
            private static extern int BCryptFinishHash(IntPtr hash,byte[] output,int outputSize,uint flags);
        }
    }
}
