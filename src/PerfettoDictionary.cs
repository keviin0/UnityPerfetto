using System;
using System.Collections.Generic;
using System.Reflection;
using Google.Protobuf;
using UnityPerfetto.Protos;

namespace UnityPerfetto
{
    // <summary>
    // Container class to provide nesting of objects to allow for simple JSON/Proto serialization for JsonWriter
    // </summary>
    public class PerfettoDictionary
    {
        const int STARTING_INDENT_LEVEL = 3;
        // Type safety since key should always be a string for it to be properly parsed as a JSON
        private Dictionary<string, object> _entries;
        public string argsName;

        public PerfettoDictionary(string name = "args")
        {
            argsName = name;
            _entries = new Dictionary<string, object>();
        }

        // Indexer to allow setting and getting values via []
        public object this[params string[] keys]
        {
            get
            {
                PerfettoDictionary current = this;
                for (int i = 0; i < keys.Length - 1; i++)
                {
                    if (current._entries.ContainsKey(keys[i]))
                    {
                        if (current._entries[keys[i]] is PerfettoDictionary nested)
                        {
                            current = nested;
                        }
                        else
                        {
                            throw new InvalidOperationException($"Key '{keys[i]}' does not contain a nested dictionary.");
                        }
                    }
                    else
                    {
                        var newContainer = new PerfettoDictionary();
                        current._entries[keys[i]] = newContainer;
                        current = newContainer;
                    }
                }

                string finalKey = keys[keys.Length - 1];
                if (current._entries.ContainsKey(finalKey))
                {
                    return current._entries[finalKey];
                }
                else
                {
                    var newContainer = new PerfettoDictionary();
                    current._entries[finalKey] = newContainer;
                    return newContainer;
                }
            }
            set
            {
                PerfettoDictionary current = this;
                for (int i = 0; i < keys.Length - 1; i++)
                {
                    if (current._entries.ContainsKey(keys[i]))
                    {
                        if (current._entries[keys[i]] is PerfettoDictionary nested)
                        {
                            current = nested;
                        }
                        else
                        {
                            throw new InvalidOperationException($"Key '{keys[i]}' does not contain a nested dictionary.");
                        }
                    }
                    else
                    {
                        var newContainer = new PerfettoDictionary();
                        current._entries[keys[i]] = newContainer;
                        current = newContainer;
                    }
                }

                string finalKey = keys[keys.Length - 1];
                current._entries[finalKey] = value;
            }
        }

        public Dictionary<string, object> GetEntries()
        {
            return _entries;
        }

        public string ToJson(int indentLevel = STARTING_INDENT_LEVEL)
        {
            var tab = new string(' ', indentLevel * STARTING_INDENT_LEVEL);
            var json = "";
            var entries = new List<string>();

            foreach (var entry in _entries)
            {
                if (entry.Value is PerfettoDictionary)
                {
                    // Handle nested container with increased indentation
                    entries.Add($"{tab}\"{entry.Key}\": {{\n{((PerfettoDictionary)entry.Value).ToJson(indentLevel + 1)}\n{tab}}}");
                }
                else
                {
                    // Handle regular key-value pairs with proper indentation
                    entries.Add($"{tab}\"{entry.Key}\": \"{entry.Value}\"");
                }
            }

            json += string.Join(",\n", entries);

            return json;
        }

        public DebugAnnotation ToProto()
        {
            var debugAnnotation = new DebugAnnotation();
            var dictEntries = new List<DebugAnnotation>();

            foreach (var entry in _entries)
            {
                var annotation = new DebugAnnotation
                {
                    Name = entry.Key
                };

                switch (entry.Value)
                {
                    case bool boolValue:
                        annotation.BoolValue = boolValue;
                        break;
                    case ulong uintValue:
                        annotation.UintValue = uintValue;
                        break;
                    case long intValue:
                        annotation.IntValue = intValue;
                        break;
                    case double doubleValue:
                        annotation.DoubleValue = doubleValue;
                        break;
                    case float floatValue:
                        annotation.DoubleValue = (double)floatValue;
                        break;
                    case string stringValue:
                        annotation.StringValue = stringValue;
                        break;
                    case PerfettoDictionary nestedDictionary:
                        annotation.DictEntries.AddRange(nestedDictionary.ToProto().DictEntries);
                        break;
                    default:
                        UnityEngine.Debug.LogError("Unrecognized value type for debug annotation: " + entry.Value.GetType());
                        break;
                }

                dictEntries.Add(annotation);
            }

            debugAnnotation.DictEntries.AddRange(dictEntries);
            return debugAnnotation;
        }
    }
} // namespace UnityPerfetto