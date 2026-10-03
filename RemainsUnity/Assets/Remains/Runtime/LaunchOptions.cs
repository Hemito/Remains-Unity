using System;

namespace Remains.Runtime
{
    public sealed class LaunchOptions
    {
        public string Role { get; private set; }
        public string Character { get; private set; } = "pony-one";
        public string Address { get; private set; } = "127.0.0.1";
        public string SaveDirectory { get; private set; }
        public string Smoke { get; private set; }
        public string Report { get; private set; }
        public ushort Port { get; private set; } = 7777;
        public int Capacity { get; private set; } = 16;
        public int ProbePeers { get; private set; } = 2;
        public bool NetworkStress { get; private set; }
        public bool Diagnostics { get; private set; }
        public static LaunchOptions Parse()
        {
            var options = new LaunchOptions();
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                switch (args[i])
                {
                    case "--role": options.Role = args[++i]; break;
                    case "--character": options.Character = args[++i]; break;
                    case "--address": options.Address = args[++i]; break;
                    case "--save-dir": options.SaveDirectory = args[++i]; break;
                    case "--port": options.Port = ushort.Parse(args[++i]); break;
                    case "--capacity": options.Capacity=int.Parse(args[++i]);if(options.Capacity<1 || options.Capacity>64) throw new ArgumentOutOfRangeException("capacity","Вместимость должна быть от 1 до 64.");break;
                    case "--smoke": options.Smoke = args[++i]; break;
                    case "--report": options.Report = args[++i]; break;
                    case "--probe-peers": options.ProbePeers=int.Parse(args[++i]); break;
                    case "--network-stress": options.NetworkStress=bool.Parse(args[++i]); break;
                    case "--diagnostics": options.Diagnostics=bool.Parse(args[++i]); break;
                }
            }
            return options;
        }
    }
}
