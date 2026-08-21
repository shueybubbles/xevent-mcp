# Bubbles.XEvent.MCPServer

This package provides a stdio-based MCP server for reading extended event files emitted by SQL Server.

## Tools

### xel_file_read

Parameters:
 - path to the file. Can be a local file or an http(s) url.
 - a byte offset in the file to start reading. Default is 0.
 - the maximum number of events to read. Default is 100.
 - a comma-delimited list of event names to filter on. Default is all events.
 - a comma-delimited list of field and action names to filter on. Default is all fields and actions.
 - a boolean indicating whether to use SQL Server to read the file.

 Output:
  Returns a count of read events and a byte offset of where to read the next set of events, followed by the events encoded in json.

 ### xesession_target_read
 
 Parameters:
  - session name
  - target name. Defaults to live target. Can be "live" or "file".
  - maximum number of events to read. Default is 100.
  - maximum number of milliseconds to read data. Default is 10000.
  - continuation token returned from a prior call. Used to continue reading from end of last read.
  
  ## Environment variables

  ### CONNECTION_STRING

  ADO.Net connection string to connect to the server. Defaults to localhost/Windows auth.