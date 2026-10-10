// Copyright (c) David Shiflet. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace Bubbles.XEvent.Listen
{
    /// <summary>
    /// Represents a row of data in the output of the Extended Events listener.
    /// </summary>
    [XmlRoot(ElementName = "Event")]
    public class RowData
    {
        public RowData()
        {
            EventName = string.Empty;
            Fields = new Dictionary<string, object>();
            Actions = new Dictionary<string, object>();
        }

        public RowData(DateTimeOffset timeStamp, string eventName, IReadOnlyDictionary<string, object> fields, IReadOnlyDictionary<string, object> actions)
        {
            TimeStamp = timeStamp;
            EventName = eventName;
            Fields = fields ?? new Dictionary<string, object>();
            Actions = actions ?? new Dictionary<string, object>();
        }

        public DateTimeOffset TimeStamp { get; set; }

        public string EventName { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? FileName { get; set; } = null;

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? FileOffset { get; set; } = null;

        [XmlIgnore]
        public IReadOnlyDictionary<string, object> Fields { get; set; }

        [XmlIgnore]
        public IReadOnlyDictionary<string, object> Actions { get; set; }

        [JsonIgnore]
        [XmlArray("Fields")]
        [XmlArrayItem("Field")]
        public List<RowDataValue> XmlFields
        {
            get => [.. Fields.Select(x => new RowDataValue(x.Key, x.Value?.ToString() ?? string.Empty))];
            set => Fields = value?.ToDictionary(x => x.Key, x => (object)x.Value) ?? new Dictionary<string, object>();
        }

        [JsonIgnore]
        [XmlArray("Actions")]
        [XmlArrayItem("Action")]
        public List<RowDataValue> XmlActions
        {
            get => [.. Actions.Select(x => new RowDataValue(x.Key, x.Value?.ToString() ?? string.Empty))];
            set => Actions = value?.ToDictionary(x => x.Key, x => (object)x.Value) ?? new Dictionary<string, object>();
        }
    }

    public class RowDataValue
    {
        public RowDataValue()
        {
            Key = string.Empty;
            Value = string.Empty;
        }

        public RowDataValue(string key, string value)
        {
            Key = key;
            Value = value;
        }

        [XmlAttribute]
        public string Key { get; set; }

        [XmlText]
        public string Value { get; set; }
    }

    public class ByteOffsetRow
    {
        public ByteOffsetRow()
        {
        }

        public ByteOffsetRow(long byteOffset)
        {
            ByteOffset = byteOffset;
        }

        public long ByteOffset { get; set; }
    }
}
