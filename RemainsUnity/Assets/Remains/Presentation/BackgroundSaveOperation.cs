using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Remains.Presentation
{
    // Unity objects are touched only before/after await on the Unity context.
    // Work functions must use file/state services, never Unity asset/UI APIs.
    public sealed class BackgroundSaveOperation : MonoBehaviour
    {
        private readonly CancellationTokenSource cancellation=new();
        public bool Busy {get;private set;}
        public async void Run<T>(Func<CancellationToken,T> work,Action<T> completed,Action<Exception> failed)
        {
            if(Busy) throw new InvalidOperationException("Операция сохранения уже выполняется.");
            Busy=true;
            try
            {
                var result=await Task.Run(()=>work(cancellation.Token),cancellation.Token);
                if(this!=null) completed(result);
            }
            catch(OperationCanceledException) when(cancellation.IsCancellationRequested) { }
            catch(Exception error)
            {
                if(this!=null) failed(error);
                else Debug.LogError("Фоновая операция сохранения завершилась с ошибкой после закрытия окна: "+error);
            }
            finally {if(this!=null) Busy=false;}
        }
        private void OnDestroy()=>cancellation.Cancel();
    }
}
