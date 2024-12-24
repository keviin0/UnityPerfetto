using System;
using System.Collections.Generic;
using System.Dynamic;
using UnityEngine;
using Google.Protobuf;
using UnityPerfetto.Protos;

namespace UnityPerfetto
{
    public class TrackGroup
    {
        public int pid;
        public ulong uuid;
        public string name;
        public Dictionary<string, TrackPublisher> trackPublishers = new();

        public TrackGroup(int pid, ulong uuid, string name)
        {
            this.pid = pid;
            this.uuid = uuid;
            this.name = name;
        }
    }

    public abstract class TrackPublisher
    {
        public int pid;
        public ulong parent_uuid;
        public ulong track_uuid;
        public uint trusted_packet_sequence_id;
        public string name;

        protected TrackPublisher(int pid, ulong parent_uuid, ulong track_uuid, string name)
        {
            this.pid = pid;
            this.parent_uuid = parent_uuid;
            this.track_uuid = track_uuid;
            this.name = name;
            this.trusted_packet_sequence_id = PerfettoPublisherFactory.GenerateRandomUInt32();
        }

        public abstract void LogMetadata(ulong track_uuid, string name, int parent_pid = 0, ulong parent_uuid = 0);

        public double GetTimeStamp()
        {
            return ProtoWriter.Instance.GetTimeStamp();
        }
    }

    public class SlicePublisher : TrackPublisher
    {
        public SlicePublisher(int pid, ulong parent_uuid, ulong track_uuid, string name)
            : base(pid, parent_uuid, track_uuid, name) { }

        public void LogStartEvent(string eventName, string categories, double timestamp, PerfettoDictionary args = null)
        {
            ProtoWriter.Instance.LogEvent(track_uuid,
                                          trusted_packet_sequence_id,
                                          eventName, 
                                          categories, 
                                          timestamp, 
                                          TrackEvent.Types.Type.SliceBegin,
                                          args: args);
        }

        public void LogEndEvent(double timestamp, PerfettoDictionary args = null)
        {
            ProtoWriter.Instance.LogEvent(track_uuid,
                                          trusted_packet_sequence_id, 
                                          null, 
                                          null, 
                                          timestamp, 
                                          TrackEvent.Types.Type.SliceEnd,
                                          args: args);
        }

        public override void LogMetadata(ulong track_uuid, string name, int parent_pid, ulong parent_uuid)
        {
            ProtoWriter.Instance.LogPublisherMetadata(ProtoWriter.TrackTypes.Slice,
                                                      track_uuid,
                                                      name,
                                                      parent_pid: parent_pid);
        }
    }

    public class CounterPublisher : TrackPublisher
    {
        public CounterPublisher(int pid, ulong parent_uuid, ulong track_uuid, string name)
            : base(pid, parent_uuid, track_uuid, name) { }

        public void LogCounterEvent(double timestamp, double value, PerfettoDictionary args = null)
        {
            ProtoWriter.Instance.LogEvent(track_uuid,
                                          trusted_packet_sequence_id, 
                                          name, 
                                          null, 
                                          timestamp, 
                                          TrackEvent.Types.Type.Counter, 
                                          value,
                                          args: args);
        }

        public override void LogMetadata(ulong track_uuid, string name, int parent_pid, ulong parent_uuid)
        {
            ProtoWriter.Instance.LogPublisherMetadata(ProtoWriter.TrackTypes.Counter,
                                                      track_uuid,
                                                      name,
                                                      parent_uuid: parent_uuid);
        }
    }

    public class PerfettoPublisherFactory : MonoBehaviour
    {
        private static PerfettoPublisherFactory _instance;
        private int _nextAvailablePid = 1234;
        private readonly Dictionary<string, TrackGroup> _trackGroups = new();

        public static PerfettoPublisherFactory Instance
        {
            get
            {
                if (_instance == null)
                {
                    // Try to find an existing instance
                    _instance = FindAnyObjectByType<PerfettoPublisherFactory>();

                    // If no instance exists, create one
                    if (_instance == null)
                    {
                        var gameObj = new GameObject("PerfettoPublisherFactory");
                        _instance = gameObj.AddComponent<PerfettoPublisherFactory>();
                        DontDestroyOnLoad(gameObj);
                    }
                }

                return _instance;
            }
        }

        public SlicePublisher CreateSlicePublisher(string name, string group = "")
        {
            return RegisterTrackEntity<SlicePublisher>(group + '/' + name);
        }

        public CounterPublisher CreateCounterPublisher(string name, string group = "")
        {
            return RegisterTrackEntity<CounterPublisher>(group + '/' + name);
        }

        private T RegisterTrackEntity<T>(string name) where T : TrackPublisher 
        {
            string[] parts = name.Split('/');
            if (parts.Length != 2)
            {
                throw new ArgumentException("Invalid name format. Use 'Group/Publisher'.");
            }

            string groupName = (parts[0] == "") ? "Global group (Default)" : parts[0];
            string pubName = parts[1];

            if (!_trackGroups.TryGetValue(groupName, out var group))
            {
                group = new TrackGroup(_nextAvailablePid++, GenerateRandomUInt64(), groupName);
                _trackGroups[groupName] = group;
                ProtoWriter.Instance.LogGroupMetadata(group.pid, group.uuid, group.name);
            }

            if (group.trackPublishers.ContainsKey(pubName))
            {
                throw new InvalidOperationException("Track entity already registered.");
            }

            ulong track_uuid = GenerateRandomUInt64();
            T trackPub = (T)Activator.CreateInstance(
                typeof(T),
                group.pid,
                group.uuid,
                track_uuid,
                pubName
            );

            trackPub.LogMetadata(trackPub.track_uuid, pubName, group.pid, group.uuid);

            group.trackPublishers[pubName] = trackPub;

            return trackPub;
        }

        public static ulong GenerateRandomUInt64()
        {
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
            {
                var buffer = new byte[8];
                rng.GetBytes(buffer);
                return BitConverter.ToUInt64(buffer, 0);
            }
        }

        public static uint GenerateRandomUInt32()
        {
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
            {
                var buffer = new byte[4];
                rng.GetBytes(buffer);
                return BitConverter.ToUInt32(buffer, 0);
            }
        }
    }
} // namespace UnityPerfetto