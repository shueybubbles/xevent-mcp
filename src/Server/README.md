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
 - an optional name of the SQL connection to use. If provided, the tool attempts to use SQL Server to read the file instead of reading it directly.

 Output:
  Returns a count of read events and a byte offset of where to read the next set of events, followed by the events encoded in json.

 ### xesession_target_read
 
 Parameters:
  - session name
  - target name. Defaults to live target. Can be "live" or "file".
  - maximum number of events to read. Default is 100.
  - maximum number of milliseconds to read data. Default is 10000.
  - continuation token returned from a prior call. Used to continue reading from end of last read.
  - the name of the SQL connection to use for the query

  ### xesession_list_connections

  Parameters:
   None

  Output:
   Returns a list of named SQL connections that can be used for invoking the other tools.

  ## Environment variables

  ### CONNECTION_STRING

  ADO.Net connection string to connect to the server. Defaults to localhost/Windows auth.

  ### SSMS_CONNECTIONS

  Set this variable to `true` to have the MCP server load named connections from a SQL Server Management Studio registered servers XML file instead of using the `CONNECTION_STRING` environment variable.

  ### REGSRVR_FILE

  Set this to the file path of a registered servers XML file. If not set, the default user profile location will be used to look for RegSrvr17.xml. This option can be handy on systems without SSMS installed to which you have copied your registered servers file. If the file identified by this variable is not found, the server defaults to looking for the SSMS copy of the file. If neither is found, no connections will be available.

  ## Open issues

  Currently, the MCP server cannot use Entra Interactive authentication due to limitations of the dotnet tool infrastructure. Tools cannot have dependencies on Winforms or WPF, so it can't provide a window handle to the `SqlAuthenticationProvider` in `Microsoft.Data.SqlClient` which requires a window for broker-based authentication. When using `SSMS_CONNECTIONS`, connections that use `ActiveDirectoryInteractive` authentication will attempt to use `ActiveDirectoryDefault` authentication instead.