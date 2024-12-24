using UnityEngine;
using UnityEngine.Events;

namespace UnityPerfetto
{
    public abstract class BenchmarkManager : MonoBehaviour
    {
        public UnityEvent OnManagerStateChanged = new UnityEvent();
        public bool IsThisManagerEnabled = false;
        private UnityAction _JsonWriterChanged;

        void OnApplicationQuit()
        {
            if (JsonWriter.Instance && IsLoggingEnabled && IsThisManagerEnabled)
            {
                EndBenchmark();
            }
        }

        void Awake()
        {
            if (IsThisManagerEnabled)
            {
                StartBenchmark();
            }
        }

        public virtual bool IsLoggingEnabled
        {
            get
            {
                if (JsonWriter.Instance)
                {
                    return JsonWriter.Instance.IsLoggingEnabled;
                } 
                else
                {
                    return false;
                }
            }
        }

        // Ends benchmark for all managers
        public virtual void EndBenchmark()
        {
            UnityEngine.Debug.Log("Benchmark Ended");
            if (IsLoggingEnabled)
            { 
                JsonWriter.Instance.Destroy();
            }
            JsonWriter.Instance.onJsonWriterStateChanged.RemoveListener(_JsonWriterChanged);
            ExtendEnd();
            IsThisManagerEnabled = false;
            InvokeOnManagerStateChanged();
        }

        public virtual void StartBenchmark()
        {
            _JsonWriterChanged = () => {
                if (!IsLoggingEnabled)
                {
                    EndBenchmark();
                }
            };

            JsonWriter.Instance.onJsonWriterStateChanged.AddListener(_JsonWriterChanged);

            if (!IsLoggingEnabled)
            {
                JsonWriter.Instance.Init();
            }

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
