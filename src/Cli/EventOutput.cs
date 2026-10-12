// Copyright (c) David Shiflet. All rights reserved.

using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Serialization;

namespace Bubbles.XEvent.Listen
{
    public interface IEventOutput
    {
        void WriteRow(CommandContextBase context,  RowData data);
    }

    public class JsonEventOutput : IEventOutput
    {
        public void WriteRow(CommandContextBase context, RowData data)
        {
            var row = JsonSerializer.Serialize(data);
            Console.WriteLine(row);
        }
    }

    public class XmlEventOutput : IEventOutput
    {
        private static readonly XmlWriterSettings xmlWriterSettings  = new()
        {
            Indent = false,
            NewLineHandling = NewLineHandling.None,
            OmitXmlDeclaration = true,
            Encoding = Encoding.UTF8,
            
        };
        private static readonly XmlSerializer rowDataSerializer = new(typeof(RowData));

        public void WriteRow(CommandContextBase context, RowData data)
        {
            WriteSerializedRow(rowDataSerializer, data);
        }

        private static void WriteSerializedRow(XmlSerializer serializer, object data)
        {
            var sb = new StringBuilder();
            using var stringWriter = new StringWriter(sb);
            using var xmlWriter = XmlWriter.Create(stringWriter, xmlWriterSettings);
            serializer.Serialize(xmlWriter, data);
            Console.WriteLine(sb.ToString().Replace("\r", "&#xD").Replace("\n", "&#xA"));
        }
    }
}
