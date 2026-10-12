# xelisten overview

xelisten is a straightforward CLI for reading and printing SQL Server extended events from XEL files and from session targets, including the live target. It allows filtering of events and fields based on their names, stopping after a time limit or after a certain number of events are read, and output using a variety of formats.

Full SQL connection strings can be provided through an environment variable or a text file. Common connection parameters have direct flags on the CLI. 

## Sample output

Unless one of the named format types (csv/json/xml) is requested, xelisten writes every event as a single line that starts with the event name followed by the timestamp, collected fields, and actions. Each value is printed as `field_name:{value}` and separated from the next entry by a space. `value` is JSON encoded.

`rpc_completed 2026-08-07T20:45:03.2139036Z client_pid:{50252} connection_reset_option:{None} object_name:{sp_executesql}`

Unless the user provides a `--fields` argument, values are sorted alphabetically by name.

## Usage

```shell
xelisten session <sessionname> [--target live|ring|file] [--timeout <count of seconds>]  [--server <path to server>] [--database <db name>] [--user <user id>] [--password <pwd>] [--auth <auth type>] <Common arguments>
```

```shell
xelisten file <path to xel file> <Common arguments>
```

 Individual parameters can be set using environment variables prefixed with `xelisten.`. For example, to set the target name, use environment variable `xelisten.target`.
 One special environment variable is `xelisten.connection-string` which allows for setting a full ADO.net connection string in lieu of passing individual parameters like `server`.

### Reading a session target

`xelisten session <session name> [options]`

xelisten can read events from ring buffer, file, and live targets on a SQL Server extended event session.

#### Required parameters

    Name of the session on the server.

#### Optional parameters
  
  | argument | description |
  |----------| ----------- |
  | --target, -t | name of the target. Must be one of `live`, `ring`, `file`. Defaults to `live`. |
  | --timeout, -T | Max number of seconds to listen to the session. Includes time required to connect to the SQL Server. |

### Reading a XEL file directly

#### Direct file access

`xelisten file <path to xel file> [options]`

#### SQL access

`xelisten file <path to file without .xel extension> --server <server>`

#### Overview

xelisten has 2 file modes - direct file access, and SQL access. If the `--server` parameter is specified, the `file` command will use `sys.fn_xe_file_target_read_file` on the given server to attempt to read the file instead of accessing it directly.

In SQL mode, the `--byte-offset` and `--max-events` parameters have a different meaning than in direct file mode. Because `sys.fn_xe_file_target_read_file` returns non-unique `(file_name, file_offset)` tuples for all events in the same buffer, xelisten treats `byte offset` as `buffer offset`. When passed both `--byte-offset` and `--max-events`, xelisten may print more than the number of events requested. It reads to the end of the buffer where the requested numbered event was retrieved and prints the remaining events in the buffer. Every event printed is given `FileName` and `FileOffset` fields when `--byte-offset` is present. These values in the last event printed can be used as parameters to `xelisten` to resume reading from that point.

For example, if `--max-events` is 8, and the 8th event is read in buffer with `file_offset` value 21100,  xelisten will continue to read events having `file_offset` 21100 until it reaches an event with a different offset or file name, or the end of the file.


#### Required parameters

    Pass the path to the file directly after `xelisten file`

#### Optional parameters

| parameter | description |
| --------- | ----------- |
| --byte-offset, -b | Byte offset in the file to start the read. A `FileOffset` property is appended to every event printed, equal to the last byte offset of the event in the file.  This offset can be passed to subsequent `xelisten file` commands to resume scanning from the last position read. |
| --file-name, -F | Path to the specific XEL file to start reading. Only valid when used with both `--server` and `--byte-offset` |


### Common parameters

These parameters apply to both `session` and `file` commands. All are optional.

  | parameter | description |
  |----------| ----------- |
  | --server, -S | name of the server. Defaults to `localhost` for `session` command. |
  | --database, -D | name of the database. Only required if `server` specifies an Azure SQL Database instance. |
  | --user, -U | user id for the connection. |
  | --password, -P | password for the connection. If a password is needed but not provided, xelisten will attempt to read it from stdin. If stdin is not redirected to xelisten, the user will be prompted to enter the password before continuing. |
  | --auth, -A | Must be one of the [SqlAuthenticationMethod](https://learn.microsoft.com/dotnet/api/microsoft.data.sqlclient.sqlauthenticationmethod?view=sqlclient-dotnet-core-7.0) names. If `--auth` is not provided, xelisten will use SQL Authentication if `--user` is provided, and Integrated security otherwise. |
  | --events, -e | comma-delimited list of event names to include. By default all events are included. |
  | --fields, -f | comma-delimited list of field and action names to include. By default all fields and actions are included. The output will order the fields to match this list. |
  | --max-events, -m | maximum number of events to read. xelisten will exit with return code 0 after reading this number of events. |
  | --output, -o | Output format. Must be one of `xml`, `csv`, `json` |


### CSV format notes

Efficient output in CSV format is somewhat tricky, particularly for XEL file input when field and action names are not provided by the user. `xelisten` has to calculate the headers before it can print events. For `target`, `xelisten` will query the metadata of the session first so it knows which fields and actions are possibly included in the data so it can print the header row.

If the user provides `--fields` and `--csv`, the output column names will match the order of the `--fields` list.

### JSON and XML output notes

Because xelisten is optimized for event streaming, it does not print enclosing collection notation around the output. Each event is a discrete JSON or XML `Event` element.


