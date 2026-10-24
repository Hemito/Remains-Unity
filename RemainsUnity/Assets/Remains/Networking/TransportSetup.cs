using Mirror;
using kcp2k;
using UnityEngine;

namespace Remains.Networking
{
    public static class TransportSetup
    {
        public static Transport Create(GameObject root,ushort port,bool stress,out RemainsKcpTransport backlog)
        {
            var kcp=root.AddComponent<RemainsKcpTransport>();kcp.port=port;kcp.statisticsGUI=false;backlog=kcp;
            if(!stress) return kcp;
            var simulationObject=new GameObject("Проверка задержек сети");
            simulationObject.SetActive(false);
            simulationObject.transform.SetParent(root.transform,false);
            var simulation=simulationObject.AddComponent<LatencySimulation>();
            simulation.wrap=kcp;simulation.latency=150;simulation.jitter=.02f;
            simulation.unreliableLoss=10;simulation.unreliableScramble=25;
            simulationObject.SetActive(true);
            Debug.LogWarning("Включён тестовый режим сети: задержка отправки 150 мс, jitter до 20 мс, потери ненадёжных сообщений 10%, перестановка 25%. Локальное соединение хоста обходит транспорт.");
            return simulation;
        }
    }
}
