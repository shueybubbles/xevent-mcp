# Overview

Output files from SQL Server extended events offer detailed troubleshooting and diagnostics information. They have variable content depending on which events were included in the session. This MCP server offers tools to enable AI agents to discover and read the data from extended event sessions.

## Road map

### V1

Initial versions will work purely with files.

### V2+

Over time, the server will support connections to SQL Server to read events from remote targets accessible via TSQL. For SQL Server Management Studio users, the MCP server will read stored local connection strings so GitHub Copilot in SSMS can use it without any extra configuration needed by the user.