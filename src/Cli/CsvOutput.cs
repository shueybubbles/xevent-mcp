// Copyright (c) David Shiflet. All rights reserved.

using System;
using System.Text;
using CsvHelper.Configuration;

namespace Bubbles.XEvent.Listen
{
    public class CsvOutput : IEventOutput
    {
        private bool headerWritten = false;
        public void WriteRow(CommandContextBase context, RowData data)
        {
            var configuration = new CsvConfiguration(System.Globalization.CultureInfo.InvariantCulture)
            {
                Delimiter = ",",
                Encoding = new UTF8Encoding(false),
                Escape = '"',
                Quote = '"',
                InjectionOptions = InjectionOptions.Escape,
            };
            using var writer = new System.IO.StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
            using var csv = new CsvHelper.CsvWriter(writer, configuration);
            if (!headerWritten)
            {
                // Write the header row
                csv.WriteField("TimeStamp");
                csv.WriteField("EventName");
                
                foreach (var field in context.Fields)
                {
                    csv.WriteField(field);
                }
                if (!string.IsNullOrEmpty(data.FileName))
                {
                    csv.WriteField("FileName");
                }
                if (!string.IsNullOrEmpty(data.FileOffset))
                {
                    csv.WriteField("FileOffset");
                }
                csv.NextRecord();
                headerWritten = true;
            }
            csv.WriteField(data.TimeStamp);
            csv.WriteField(data.EventName);
            foreach (var field in context.Fields)
            {
                if (data.Fields.ContainsKey(field))
                {
                    csv.WriteField(data.Fields[field]);
                }
                else if (data.Actions.ContainsKey(field))
                {
                    csv.WriteField(data.Actions[field]);
                }
                else
                {
                    csv.WriteField(string.Empty);
                }
            }
            if (!string.IsNullOrEmpty(data.FileName))
            {
                csv.WriteField(data.FileName);
            }
            if (!string.IsNullOrEmpty(data.FileOffset))
            {
                csv.WriteField(data.FileOffset);
            }
            csv.NextRecord();
        }
    }
}
