using System;
using Mirror;
using Remains.Diagnostics;

namespace Remains.Networking
{
    // Wraps network message handlers in FrameHitchLog sections ("Сеть: <сообщение>" on the server, "Сеть клиента: …"
    // on the client), so a slow frame names the message that cost it.
    public static class MessageTiming
    {
        private static class Names<T>
        {
            public static readonly string Server="Сеть: "+typeof(T).Name;
            public static readonly string Client="Сеть клиента: "+typeof(T).Name;
        }
        public static Action<NetworkConnectionToClient,T> Server<T>(Action<NetworkConnectionToClient,T> handler)
            =>(connection,message)=> {using(FrameHitchLog.Section(Names<T>.Server)) handler(connection,message);};
        public static Action<NetworkConnectionToClient,T,int> Server<T>(Action<NetworkConnectionToClient,T,int> handler)
            =>(connection,message,channel)=> {using(FrameHitchLog.Section(Names<T>.Server)) handler(connection,message,channel);};
        public static Action<T> Client<T>(Action<T> handler)
            =>message=> {using(FrameHitchLog.Section(Names<T>.Client)) handler(message);};
        public static Action<T,int> Client<T>(Action<T,int> handler)
            =>(message,channel)=> {using(FrameHitchLog.Section(Names<T>.Client)) handler(message,channel);};
    }
}
