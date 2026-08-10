# Bubbles.XEvent.MCPServer

This package provides a stdio-based MCP server for reading extended event files emitted by SQL Server.

## Tools

### xel_file_read

Parameters:
 - path to the file. Must be local file path, not https URL.
 - a byte offset in the file to start reading. Default is 0.
 - the maximum number of events to read. Default is 100.

 Output:
  Returns a count of read events and a byte offset of where to read the next set of events, followed by the events encoded in json.

  