using UnityEngine;
using UnityEngine.Events;

namespace UnityPerfetto
{
    public abstract class PerfettoTraceManager : MonoBehaviour
    {
        public UnityEvent OnManagerStateChanged = new UnityEvent();
        public bool IsThisManagerEnabled = false;
        private UnityAction<ProtoWriter.State> _ProtoWriterChanged;

        void OnApplicationQuit()
        {
            if (ProtoWriter.Instance.currState == ProtoWriter.State.Enabled && IsThisManagerEnabled)
            {
                EndAllTraces();
            }
        }

        void Awake()
        {
            if (IsThisManagerEnabled)
            {
                StartTrace();
            }
        }

        public void EndAllTraces()
        {
            ProtoWriter.Instance.Destroy();
        }

        public virtual void EndTrace()
        {
            if (!IsThisManagerEnabled)
            {
                return;
            }

            IsThisManagerEnabled = false;

            ExtendEnd();
            InvokeOnManagerStateChanged();

            ProtoWriter.Instance.onProtoWriterStateChange.RemoveListener(_ProtoWriterChanged);
        }

        public virtual void StartTrace()
        {
            _ProtoWriterChanged = (ProtoWriter.State state) => {
                if (state == ProtoWriter.State.Disabled)
                {
                    EndTrace();
                }
            };

            if (ProtoWriter.Instance.currState == ProtoWriter.State.Disabled)
            {
                ProtoWriter.Instance.Init();
            }

            ProtoWriter.Instance.onProtoWriterStateChange.AddListener(_ProtoWriterChanged);

            ExtendInit();

            IsThisManagerEnabled = true;
            InvokeOnManagerStateChanged();
        }

        private void InvokeOnManagerStateChanged()
        {
            if (OnManagerStateChanged != null)
            {
                OnManagerStateChanged.Invoke();
            }
        }

        // Register delegates to callbacks
        protected abstract void ExtendInit();

        // Unregister delegates to callbacks
        protected abstract void ExtendEnd();
    }
}
