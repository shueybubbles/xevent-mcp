// Copyright (c) David Shiflet. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Bubbles.XEvent.Listen
{
    public class DefaultEventOutput : IEventOutput
    {
        private readonly string delimiter = " ";

        public void WriteRow(CommandContextBase context, RowData data)
        {
            var keys = context.Fields.Length > 0 ? (IEnumerable<string>)context.Fields : data.Fields.Keys.Union(data.Actions.Keys).Order();
            Console.Write($"{data.EventName} {data.TimeStamp.UtcDateTime:O}");
            // Just in case field and action names match, print the field value then the action value
            foreach (var key in keys.Distinct())
            {
                if (data.Fields.ContainsKey(key))
                {
                    WriteValue(key, data.Fields[key]?.ToString() ?? string.Empty);
                }
                if (data.Actions.ContainsKey(key))
                {
                    WriteValue(key, data.Actions[key]?.ToString() ?? string.Empty);
                }                
            }
            if (!string.IsNullOrEmpty(data.FileName))
            {
                WriteValue("FileName", data.FileName);
            }
            if (data.FileOffset != null)
            {
                WriteValue("FileOffset", data.FileOffset);
            }
            Console.WriteLine();
        }

        private void WriteValue(string name, string value)
        {
            Console.Write(delimiter);
            Console.Write($"{name}:{{{JsonSerializer.Serialize(value)}}}");
        }
    }
}
