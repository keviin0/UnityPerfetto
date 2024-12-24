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
        private static ProtoWriter _instance;
        private string _filePath = "";
        private Stopwatch _stopwatch;
        private BlockingCollection<TracePacket> _eventQueue = new BlockingCollection<TracePacket>();
        private Thread _protoWriterWorkerThread;
        private int _nextAvailableTid = 5678;
        private const string _benchmarkFolderPath = "Benchmarks";

        public State currState = State.Disabled;
        public UnityEvent<State> onProtoWriterStateChange = new UnityEvent<State>();

        public static ProtoWriter Instance
        {
            get
            {
                if (_instance == null)
                {
                    // Try to find an existing instance
                    _instance = FindAnyObjectByType<ProtoWriter>();

                    // If no instance exists, create one
                    if (_instance == null)
                    {
                        var gameObj = new GameObject("ProtoWriter");
                        _instance = gameObj.AddComponent<ProtoWriter>();
                        DontDestroyOnLoad(gameObj);
                    }
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

        public void Init()
        {
            string folderPath = Path.Combine(Application.persistentDataPath, _benchmarkFolderPath);
            Directory.CreateDirectory(folderPath);

            string fileName = $"benchmark_{System.DateTime.Now:yyyy-MM-dd_HH-mm}";
            _filePath = Path.Combine(folderPath, fileName + ".pb");

            _stopwatch = Stopwatch.StartNew();

            _protoWriterWorkerThread = new Thread(ProtoWriterWorker);
            _protoWriterWorkerThread.Start();

            ChangeState(State.Enabled);

            Debug.Log($"[INFO] ProtoWriter initialized. Log will be saved to {_filePath}");
        }

        public void Destroy()
        {
            _eventQueue.CompleteAdding();
            _protoWriterWorkerThread?.Join();
            _eventQueue.Dispose();

            ChangeState(State.Disabled);

            Debug.Log($"[INFO] ProtoWriter wrote to {_filePath}. To view, open trace in ui.perfetto.dev.");
            StopAllCoroutines();
        }

        public void LogGroupMetadata(int pid, ulong uuid, string name)
        {
            var processDescriptor = new ProcessDescriptor()
            {
                Pid = pid,
                ProcessName = name,
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

            _eventQueue.Add(tracePacket);
        }

        public void LogPublisherMetadata(TrackTypes type, ulong track_uuid, string name, int parent_pid = 0, ulong parent_uuid = 0)
        {
            var trackDescriptor = new TrackDescriptor
            {
                Uuid = track_uuid,
            };

            switch (type)
            {
                case TrackTypes.Slice:
                    {
                        trackDescriptor.Thread = new ThreadDescriptor()
                        {
                            Pid = parent_pid,
                            Tid = _nextAvailableTid,
                            ThreadName = name,
                        };
                        _nextAvailableTid++;
                        break;
                    }
                case TrackTypes.Counter:
                    {
                        trackDescriptor.Counter = new CounterDescriptor();
                        trackDescriptor.ParentUuid = parent_uuid;
                        trackDescriptor.Name = name;
                        break;
                    }
            }
            
            var tracePacket = new TracePacket
            {
                TrackDescriptor = trackDescriptor
            };

            _eventQueue.Add(tracePacket);
        }

        public void LogEvent(ulong track_uuid, 
                             uint trusted_packet_sequence_id,
                             string name, 
                             string categories, 
                             double timestamp, 
                             TrackEvent.Types.Type eventType, 
                             double? value = null,
                             PerfettoDictionary args = null)
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
                protoArgs.Name = "args";
                trackEvent.DebugAnnotations.Add(protoArgs);
            }

            var tracePacket = new TracePacket
            {
                Timestamp = (ulong)timestamp,
                TrackEvent = trackEvent,
                TrustedPacketSequenceId = trusted_packet_sequence_id
            };

            _eventQueue.Add(tracePacket);
        }

        private void ProtoWriterWorker()
        {
            foreach (var tracePacket in _eventQueue.GetConsumingEnumerable())
            {
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

        public double GetTimeStamp()
        {
            // Perfetto base time unit is measured in nanoseconds
            return _stopwatch.Elapsed.TotalMilliseconds * 1000;
        }

        private void ChangeState(State state)
        {
            currState = state; 
            onProtoWriterStateChange.Invoke(currState);
        }
    }
}
