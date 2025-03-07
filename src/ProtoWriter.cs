using Google.Protobuf;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;
using UnityPerfetto.Protos;
using System.Collections;
using UnityEngine.Events;

using Debug = UnityEngine.Debug;
using Stopwatch = System.Diagnostics.Stopwatch;
using System.Collections.Generic;

namespace UnityPerfetto
{
    public class ProtoWriter : MonoBehaviour
    {
        public class ProtoWorker
        {
            private string _filePath;
            private Thread _thread;
            private bool _busy;
            private Timer _busyTimer;
            private const int BUSY_TIME_THRESHOLD = 300;

            public bool IsBusy => _busy;
            public BlockingCollection<TracePacket> eventQueue;

            public ProtoWorker(string filePath)
            {
                this._filePath = filePath;
                this.eventQueue = new BlockingCollection<TracePacket>();
                this._thread = new Thread(ProtoWriterWorker);
                this._busyTimer = new Timer(ResetBusyFlag, null, Timeout.Infinite, Timeout.Infinite);

                _thread.Start();
            }

            private void ProtoWriterWorker()
            {
                foreach (var tracePacket in eventQueue.GetConsumingEnumerable())
                {
                    _busy = true;
                    _busyTimer.Change(BUSY_TIME_THRESHOLD, Timeout.Infinite);
                    EmitPacket(tracePacket);
                }
            }

            private void EmitPacket(TracePacket tracePacket)
            {
                var trace = new Trace();
                trace.Packet.Add(tracePacket);

                using (var fileStream = new FileStream(_filePath, FileMode.Append, FileAccess.Write, FileShare.None))
                {
                    trace.WriteTo(fileStream);
                    fileStream.Flush();
                }
            }

            private void ResetBusyFlag(object state)
            {
                _busy = false;
            }

            public void Destroy()
            {
                eventQueue.CompleteAdding();
                _thread?.Join();
                eventQueue.Dispose();
                _busyTimer.Dispose();
            }
        }

        private static ProtoWriter _instance;
        private Stopwatch _stopwatch;
        private double _timestamp;
        private int _nextAvailableTid = 5678;
        private const string _benchmarkFolderPath = "Benchmarks";
        private string _entireFolderPath;
        private Dictionary<string, ProtoWorker> protoWorkers = new Dictionary<string, ProtoWorker>();

        public bool IsBusy = false;
        public State currState = State.Disabled;
        public UnityEvent<State> onProtoWriterStateChange = new UnityEvent<State>();

        public static ProtoWriter Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<ProtoWriter>() ?? new GameObject("ProtoWriter").AddComponent<ProtoWriter>();
                    DontDestroyOnLoad(_instance.gameObject);
                }
                return _instance;
            }
        }

        public enum State
        {
            Enabled,
            Disabled
        }

        public enum TrackTypes
        {
            Slice,
            Counter
        }

        private void Update()
        {
            foreach (ProtoWorker worker in protoWorkers.Values)
            {
                if (worker.IsBusy)
                {
                    IsBusy = true;
                    return;
                }
            }

            IsBusy = false;
        }

        public void Init()
        {
            _entireFolderPath = Path.Combine(Application.persistentDataPath, _benchmarkFolderPath);
            Directory.CreateDirectory(_entireFolderPath);
            _stopwatch = Stopwatch.StartNew();
            ChangeState(State.Enabled);
        }

        public void Destroy()
        {
            foreach (var worker in protoWorkers.Values)
            {
                worker.Destroy();
            }
            protoWorkers.Clear();

            Debug.Log($"[INFO] ProtoWriter wrote to {_entireFolderPath}. To view, open trace in ui.perfetto.dev.");
            ChangeState(State.Disabled);
        }

        public void RegisterFile(string filename)
        {
            if (protoWorkers.ContainsKey(filename)) { return; }

            string newFilename = $"{filename}_{System.DateTime.Now:yyyy-MM-dd_HH-mm}.pb";
            string filePath = Path.Combine(_entireFolderPath, newFilename);

            protoWorkers[filename] = new ProtoWorker(filePath);
        }

        private ProtoWorker GetProtoWorker(string filename)
        {
            if (!protoWorkers.ContainsKey(filename))
            {
                RegisterFile(filename);
            }

            protoWorkers.TryGetValue(filename, out ProtoWorker worker);
            return worker;
        }

        public void LogGroupMetadata(string filename, int pid, ulong uuid, string name)
        {
            var worker = GetProtoWorker(filename);
            if (worker != null)
            {
                var processDescriptor = new ProcessDescriptor
                {
                    Pid = pid,
                    ProcessName = name
                };

                var trackDescriptor = new TrackDescriptor
                {
                    Uuid = uuid,
                    Process = processDescriptor
                };

                var tracePacket = new TracePacket
                {
                    TrackDescriptor = trackDescriptor
                };

                worker.eventQueue.Add(tracePacket);
            }
        }

        public void LogPublisherMetadata(string filename, TrackTypes type, ulong track_uuid, string name, int parent_pid = 0, ulong parent_uuid = 0)
        {
            var worker = GetProtoWorker(filename);
            if (worker != null)
            {
                var trackDescriptor = new TrackDescriptor
                {
                    Uuid = track_uuid
                };

                switch (type)
                {
                    case TrackTypes.Slice:
                        trackDescriptor.Thread = new ThreadDescriptor
                        {
                            Pid = parent_pid,
                            Tid = _nextAvailableTid++,
                            ThreadName = name
                        };
                        break;
                    case TrackTypes.Counter:
                        trackDescriptor.Counter = new CounterDescriptor();
                        trackDescriptor.ParentUuid = parent_uuid;
                        trackDescriptor.Name = name;
                        break;
                }

                var tracePacket = new TracePacket
                {
                    TrackDescriptor = trackDescriptor
                };

                worker.eventQueue.Add(tracePacket);
            }
        }

        public void LogEvent(string filename, ulong track_uuid, uint trusted_packet_sequence_id, string name, string categories, double timestamp, TrackEvent.Types.Type eventType, double? value = null, PerfettoDictionary args = null)
        {
            var worker = GetProtoWorker(filename);
            if (worker != null)
            {
                var trackEvent = new TrackEvent
                {
                    Type = eventType,
                    TrackUuid = track_uuid,
                };

                switch (eventType)
                {
                    case TrackEvent.Types.Type.SliceEnd:
                        {
                            break;
                        }
                    case TrackEvent.Types.Type.SliceBegin:
                        {
                            trackEvent.Name = name;
                            foreach (var category in categories.Split(','))
                            {
                                // Repeated fields are read only and can only be modified with 'Add', 'Remove', and 'Clear'
                                // Assuming categories are comma-separated
                                trackEvent.Categories.Add(category.Trim());
                            }
                            break;
                        }
                    case TrackEvent.Types.Type.Counter:
                        {
                            trackEvent.DoubleCounterValue = (double)value;
                            break;
                        }
                    default:
                        {
                            throw new System.Exception("Invalid TrackEvent Type");
                        }
                }

                if (args != null)
                {
                    var protoArgs = args.ToProto();
                    protoArgs.Name = args.argsName;
                    trackEvent.DebugAnnotations.Add(protoArgs);
                }

                var tracePacket = new TracePacket
                {
                    Timestamp = (ulong)timestamp,
                    TrackEvent = trackEvent,
                    TrustedPacketSequenceId = trusted_packet_sequence_id
                };

                worker.eventQueue.Add(tracePacket);
            }
        }

        public double GetTimeStamp()
        {
            return _timestamp; 
        }

        public void UpdateTimeStamp()
        {
            _timestamp = _stopwatch.Elapsed.TotalMilliseconds * 1000000; // Convert to nanoseconds for Perfetto
        }

        private void ChangeState(State state)
        {
            currState = state;
            onProtoWriterStateChange.Invoke(currState);
        }
    }
}
