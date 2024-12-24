using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using UnityEngine;
using UnityEngine.Events;
using Debug = UnityEngine.Debug;

namespace UnityPerfetto
{
    public class JsonWriter : MonoBehaviour
    {
        private static JsonWriter _instance;
        private StreamWriter _logFile;
        private Stopwatch _stopwatch;
        private const string _tab = "   ";
        private const string _doubleTab = "      ";
        private const string _benchmarkFolderPath = "Benchmarks";
        private string _filePath = "";
        private LogEventInfo?[] _unfinishedEvents;
        private bool _first_entry = true;
        private bool _isLoggingEnabled;
        private int _fileNumber = 1;

        private BlockingCollection<LogEventInfo> _logEventBuffer =
            new BlockingCollection<LogEventInfo>(new ConcurrentQueue<LogEventInfo>());

        private Thread _logWriterWorkerThread;

        public const int MICROSECOND_CONVERSION_FACTOR = 1000;

        public bool IsLoggingEnabled
        {
            get { return _isLoggingEnabled; }
        }

        public UnityEvent onJsonWriterStateChanged = new UnityEvent();

        public struct LogEventInfo
        {
            public string name;
            public string category;
            public int pid;
            public int tid;
            public double timestamp;
            public char phase;
            public string args;

            public LogEventInfo(string name, string category, int pid, int tid, double timestamp, char phase,
                string args)
            {
                this.name = name;
                this.category = category;
                this.pid = pid;
                this.tid = tid;
                this.timestamp = timestamp;
                this.phase = phase;
                this.args = args;
            }
        }
        
        public struct MetadataInfo
        {
            public const string processName = "process_name";
            public const string threadName = "thread_name";
            public const string processLabels = "process_labels";
            public const string processSortIndex = "process_sort_index";
            public const string threadSortIndex = "thread_sort_index";
        }

        public static JsonWriter Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new GameObject("JsonWriter").AddComponent<JsonWriter>();
                    DontDestroyOnLoad(_instance.gameObject);
                }

                return _instance;
            }
        }

        public void Init()
        {
            string dateTime = DateTime.Now.ToString("yyyy-MM-dd_HH-mm");
            string folderPath = Path.Combine(System.IO.Directory.GetCurrentDirectory(), _benchmarkFolderPath);

            // Folder Path Check
            if (!isFolderPathAvailable(folderPath))
            {
                // Fallback to DataPath
                folderPath = Path.Combine(Application.dataPath, _benchmarkFolderPath);
                if (!isFolderPathAvailable(folderPath))
                {
                    // Fallback to PersistentDataPath
                    folderPath = Path.Combine(Application.persistentDataPath, _benchmarkFolderPath);
                    if (!isFolderPathAvailable(folderPath))
                    {
                        // Init Failed
                        Debug.LogError("JsonWriter cannot find an available folder to save files");
                        return;
                    }
                }
            }
            
            // Folder is good
            // File read/write check
            string fileName = $"benchmark_{dateTime}.json";
            _filePath = Path.Combine(folderPath, fileName);

            if (File.Exists(_filePath))
            {
                fileName = $"benchmark_{dateTime}_{_fileNumber}.json";
                _filePath = Path.Combine(folderPath, fileName);
                _fileNumber++;
            }
            else
            {
                _fileNumber = 1;
            }
            
            if (!isFileReadWriteAvailable(_filePath))
            {
                // Init Failed
                return;
            }

            Debug.Log("JsonWriter log will be saved in: " + _filePath);

            _logFile = new StreamWriter(_filePath);
            _logFile.WriteLine("[");
            _stopwatch = Stopwatch.StartNew();
            _unfinishedEvents = new LogEventInfo?[BenchmarkConstants.NUMBER_OF_TRACKED_OBJECTS];

            _logEventBuffer = new BlockingCollection<LogEventInfo>(new ConcurrentQueue<LogEventInfo>());
            _logWriterWorkerThread = new Thread(LogWriterWorker);
            _logWriterWorkerThread.Start();

            _isLoggingEnabled = true;
            _first_entry = true;
            NotifyJsonWriterStateChange();
        }

        public void Destroy()
        {
            if (_logFile != null)
            {
                // End all unfinished events
                for (int i = 0; i < BenchmarkConstants.NUMBER_OF_TRACKED_OBJECTS; i++)
                {
                    if (_unfinishedEvents[i].HasValue)
                    {
                        var unfinishedEvent = _unfinishedEvents[i].Value;
                        EndDurationEvent(unfinishedEvent.name, unfinishedEvent.category, unfinishedEvent.pid);
                    }
                }

                // Stop the log writer worker thread
                _logEventBuffer.CompleteAdding();
                _logWriterWorkerThread.Join(); // Ensure the thread fully terminates

                // Close and dispose of the file
                _logFile.WriteLine("\n]");
                _logFile.Close();
                _logFile.Dispose(); // Added dispose to properly release resources

                // Nullify the stream writer to prepare for the next Init call
                _logFile = null;
            }

            Debug.Log($"JsonWriter log saved in: {_filePath}\n" + $"<a href=\"{_filePath}\">{_filePath}</a>");

            // Nullify thread-related objects
            _logWriterWorkerThread = null;
            _logEventBuffer = null;

            _isLoggingEnabled = false;
            NotifyJsonWriterStateChange();
        }


        // Returns current stopwatch time at a microsecond granularity
        public Double GetTimeStamp()
        {
            return _stopwatch.Elapsed.TotalMilliseconds * MICROSECOND_CONVERSION_FACTOR;
        }

        private void WriteLogEvent(LogEventInfo logEvent)
        {
            if (_logFile == null) { return; }

            string leadingComma = ",\n";
            if (_first_entry)
            {
                leadingComma = "";
                _first_entry = false;
            }

            var eventStr = $"{leadingComma}{_tab}{{\n" +
                           $"{_doubleTab}\"pid\": {logEvent.pid},\n" +
                           $"{_doubleTab}\"tid\": {logEvent.tid},\n" +
                           $"{_doubleTab}\"name\": \"{logEvent.name}\"";

            if (logEvent.category != "")
            {
                eventStr += $",\n{_doubleTab}\"cat\": \"{logEvent.category}\"";
            }

            if (logEvent.phase != '\0')
            {
                eventStr += $",\n{_doubleTab}\"ph\": \"{logEvent.phase}\"";
            }

            if (logEvent.timestamp >= 0)
            {
                eventStr += $",\n{_doubleTab}\"ts\": {logEvent.timestamp:F0}";
            }

            if (!string.IsNullOrEmpty(logEvent.args))
            {
                eventStr += $",\n{_doubleTab}\"args\": {{\n{logEvent.args}\n{_doubleTab}}}";
            }

            eventStr += $"\n{_tab}}}";

            _logFile.Write(eventStr);
            _logFile.Flush();
        }


        /* Generic function used to log any type of event
         * Required parameters:
         *      string name
         *      string category
         *      int pid
         * Optional parameters:
         *      Double timestamp (Timestamps are represented at microsecond granularity.
         *                          Not used for metadata events. See provided helper
         *                          function GetTimeStamp() when needed.)
         *      char phase (Determines the event type)
         *      string args (Available for any additional parameters required for certain types of events)
         */
        private void LogEvent(string name, string category, int pid, Double timestamp = -1, char phase = '\0',
            string args = "", int? tid = null)
        {
            if (_logFile == null) return;

            var logEvent = new LogEventInfo(name, category, pid, tid ?? 0, timestamp, phase, args);
            _logEventBuffer.Add(logEvent);
        }

        // Used to mark end of benchmark for visual clarity in timeline
        private void LogBenchmarkEndEvent(int pid)
        {
            var timestamp = GetTimeStamp();
            LogEvent("Benchmark Ended", "END", pid, timestamp, 'B');
            LogEvent("Benchmark Ended", "END", pid, timestamp + timestamp / 10, 'E');
        }

        /* Logs an instantaneous counter event that contains a single
         * entry of information. e.g.
              {
         *        "cat": 6
         *    }
         * Not to be confused with void LogMultipleCounterEvents() which retains
         * count information of MULTIPLE items.
         */
        public void LogObjectEvent(PerfettoDictionary container, string name, string category, int pid)
        {
            string args = container.ToJson();
            LogEvent(name, category, pid, GetTimeStamp(), 'C', args);
        }

        /* Logs an instantaneous counter event that contains multiple
         * entries of information. e.g.
              {
         *        "cat": 6
         *        "dog": 2
         *    }
         * Not to be confused with void LogCounterEvent() which only retains
         * count information of a SINGLE item. This is important because the
         * tracing tool's visualization only visualizes the item with the
         * largest count.
         */
        public void LogMultipleCounterEvents(PerfettoDictionary container, string name, string category, int pid)
        {
            string args = container.ToJson();
            LogEvent(name, category, pid, GetTimeStamp(), 'C', args);
        }

        /* Logs a metadata event to establish prerequisite information
         * for clarity when analyzing log in visualization tool
         * by grouping events with a pid into their own dropdown
         */
        public void LogMetadataEvent(string metadata_item, string value, int pid, int tid = 0)
        {
            PerfettoDictionary container = new PerfettoDictionary();
            string key = "";
            switch (metadata_item)
            {
                case "process_name":
                case "thread_name":
                    key = "name";
                    break;
                case "process_labels":
                    key = "labels";
                    break;
                case "process_sort_index":
                case "thread_sort_index":
                    key = "sort_index";
                    break;
                default:
                    key = "invalid_metadata_key";
                    break;
            }

            string args = $"{_doubleTab}{_tab}\"{key}\": \"{value}\"";
            LogEvent(metadata_item, "", pid: pid, tid: tid, phase: 'M', args: args);
        }

        /* Logs the BEGINNING of a duration event. Must be FOLLOWED by a
         * corresponding end duration event using
         *      void EndDurationEvent(string name, string category, int pid)
         * to be displayed in visualization tool
         */
        public void StartDurationEvent(string name, string category, int pid, double? ts = null, int? tid = null, PerfettoDictionary container = null)
        {
            double timestamp = ts ?? GetTimeStamp();
            int thread_id = tid ?? 0;
            string args = container?.ToJson();

            LogEvent(name, category, pid, timestamp, 'B', tid: thread_id, args: args);

            _unfinishedEvents[pid] = new LogEventInfo(name, category, pid, thread_id, 0, '\0', "");
        }

        /* Logs the END of a duration event. Must be PRECEDED by a
         * corresponding beginning duration event using
         *      void StartDurationEvent(string name, string category, int pid)
         * to be displayed in visualization tool
         */
        public void EndDurationEvent(string name, string category, int pid, double? ts = null, int? tid = null)
        {
            if (!_unfinishedEvents[pid].HasValue)
            {
                return;
            }

            _unfinishedEvents[pid] = null;
            double timestamp = ts ?? GetTimeStamp();
            int thread_id = tid ?? 0;

            LogEvent(name, category, pid, timestamp, 'E', tid: thread_id);
        }

        private void LogWriterWorker()
        {
            if (_logFile != null)
            {
                foreach (var logEvent in _logEventBuffer.GetConsumingEnumerable())
                {
                    WriteLogEvent(logEvent);
                }
            }
        }

        private void NotifyJsonWriterStateChange()
        {
            onJsonWriterStateChanged?.Invoke();
        }

        private bool isFolderPathAvailable(string folderPath)
        {
            if(Directory.Exists(folderPath)) return true;
      
            try
            {
                Directory.CreateDirectory(folderPath);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"JsonWriter cannot create directory to save log json files at:{folderPath}\n" +
                                 $"Exception:{exception.Message}");
                return false;
            }

            return true;
        }
        
        private bool isFileReadWriteAvailable(string filePath)
        {
            try
            {
                using (var fileStream = new FileStream(filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite))
                {
                    fileStream.Close();
                }
                File.Delete(filePath);
                return true;
                
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"JsonWriter cannot read/write to file at:{filePath}" +
                                 $"{exception.Message}");
                return false;
            }
        }
    }
}