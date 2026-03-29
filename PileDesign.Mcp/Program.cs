using PileDesign.Mcp;

// MCP Server: JSON-RPC 2.0 over stdio
var server = new McpServer();
await server.RunAsync();
