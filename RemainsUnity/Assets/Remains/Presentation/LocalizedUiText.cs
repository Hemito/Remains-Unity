using Remains.Content;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Presentation
{
    // A label retains its Russian source; switching languages never translates a translated value.
    // Text's dirty event is used for existing UI writers. There is no per-frame scan of the canvas.
    [DisallowMultipleComponent,RequireComponent(typeof(Text))]
    public sealed class LocalizedUiText : MonoBehaviour
    {
        private Text label;
        private string source,lastOutput;
        private bool writing,subscribed;
        public static void Bind(Text text)
        {
            if(text==null || text.GetComponentInParent<InputField>() is {} input && input.textComponent==text)return;
            var binding=text.GetComponent<LocalizedUiText>();
            if(binding==null)binding=text.gameObject.AddComponent<LocalizedUiText>();
            binding.Attach(text);
        }
        public static void BindTree(Transform root)
        {
            if(root==null)return;
            foreach(var text in root.GetComponentsInChildren<Text>(true))Bind(text);
        }
        private void OnEnable()=>Attach(GetComponent<Text>());
        private void Attach(Text value)
        {
            label=value;
            if(!subscribed && isActiveAndEnabled)
            {label.RegisterDirtyVerticesCallback(Capture);GameLocalization.Changed+=Refresh;subscribed=true;}
            Capture();
        }
        private void Capture()
        {
            if(writing || label==null)return;
            if(source==null || label.text!=lastOutput)source=label.text;
            Refresh();
        }
        private void Refresh()
        {
            if(label==null || source==null || writing)return;
            string translated=GameLocalization.TranslateRussian(source,"UI:"+name);
            label.font=LocalizationFonts.ForLanguage(GameLocalization.CurrentLanguage);
            lastOutput=translated;
            if(label.text==translated)return;
            writing=true;
            try {label.text=translated;}
            finally {writing=false;}
        }
        private void OnDisable()=>Release();
        private void OnDestroy()=>Release();
        public void Release()
        {
            if(!subscribed)return;
            if(label!=null)label.UnregisterDirtyVerticesCallback(Capture);
            GameLocalization.Changed-=Refresh;subscribed=false;
        }
    }
}
