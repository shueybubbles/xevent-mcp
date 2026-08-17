# Overview

Output files from SQL Server extended events offer detailed troubleshooting and diagnostics information. They have variable content depending on which events were included in the session. This MCP server offers tools to enable AI agents to discover and read the data from extended event sessions.

## Road map

### V0

V0 will support a single connection string for working with one SQL server instance

- 0.1.0 Support local XEL files
- 0.2.0 Support URLs for XEL files in the cloud
- 0.3.0 Support reading live target and file target
- 0.4.0 Support ring buffer target

### V1

V1 will enable connections to multiple SQL instances, using named connection strings.

- 1.0 Add a tool to enumerate available connections. Support a yaml or json file that lists named SQL connection strings.
- 1.1 Support MRU and registered server connections from the user's local SQL Server Management Studio installation
