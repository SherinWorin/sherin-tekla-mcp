# Running tekla_mcp_server with Tekla Structures 2025 and Claude Desktop

Status: **unverified on 2025** (upstream tests 2022 and 2026). Try it on a scratch copy of a model first, in read-only mode.

## 1. Get the code and install (PowerShell, Windows)

```powershell
# PowerShell (Windows)
cd C:\Users\sheri\Sherin_AI
git clone https://github.com/teknovizier/tekla_mcp_server.git
cd tekla_mcp_server
py -3.12 -m venv .venv
.\.venv\Scripts\python -m pip install -r requirements.txt
```

Python 3.11-3.13. It must run on Windows (pythonnet loads the Tekla .NET DLLs), not in WSL.

## 2. Config (`config\` folder)

Copy each `*.sample.json` to the same name without `.sample`:
`settings.json`, `element_types.json`, `semantic_overrides.json`, `base_components.json`, `report_properties.json`.

```powershell
# PowerShell (Windows), inside tekla_mcp_server
Get-ChildItem config\*.sample.json | ForEach-Object { Copy-Item $_ ($_.FullName -replace '\.sample','') }
```

Edit `config\settings.json`:
```json
"tekla_path": "C:\\Program Files\\Tekla Structures\\2025.0\\bin",
"read_only": true,
"embeddings": { "enabled": false }
```
Start with `read_only: true` and embeddings off; turn them on once the connection works.

`element_types.json` maps element types to Tekla classes and numbering prefixes. The sample is precast-concrete oriented (classes 1-18, steel 100-103). Replace it with the classes used on the pod/base-frame models before using `select_elements_by_filter` with `element_type`.

Put project rules as Markdown into `config\context\` (for example a copy of `base-skid-rules.md`). The AI can read them through the `project://context` resource.

The repo also ships Tekla macros in `config\macros\modeling\` (`TeklaMCPPrintDrawings.cs`, `TeklaMCPExportDrawings.cs`, `TeklaMCPSelectModelObjectsFromDrawings.cs`, ...) and attribute files in `config\attributes\`. The print/export/collision tools need these copied into the Tekla macro folder (`XS_MACRO_DIRECTORY`, e.g. `C:\TeklaStructures\2025.0\Environments\common\macros\modeling`) and the model `attributes` folder.

## 3. Connect Claude Desktop

`%APPDATA%\Claude\claude_desktop_config.json`:
```json
{
  "mcpServers": {
    "tekla-mcp": {
      "command": "C:\\Users\\sheri\\Sherin_AI\\tekla_mcp_server\\.venv\\Scripts\\python.exe",
      "args": ["C:\\Users\\sheri\\Sherin_AI\\tekla_mcp_server\\src\\tekla_mcp_server\\mcp_server.py"],
      "env": {
        "TEKLA_MCP_LOG_LEVEL": "INFO",
        "TEKLA_MCP_LOG_FILE_PATH": "C:\\Users\\sheri\\Sherin_AI\\tekla_mcp_server\\mcp_server.log",
        "TEKLA_MCP_CONFIG_DIR": "C:\\Users\\sheri\\Sherin_AI\\tekla_mcp_server\\config"
      }
    }
  }
}
```
Restart Claude Desktop. Open Tekla 2025 with a model first.

## 4. First test (read-only)

1. Ask: "Check the Tekla connection status." -> `tekla://connection_status` should say connected.
2. Select a few base-frame members in Tekla, ask: "Get the properties of the selected elements." -> `get_elements_properties`.
3. Ask: "Get coordinates and bounding boxes of the selection."
4. Check `mcp_server.log`: it lists how many of the 9 DLLs loaded. All 9 must load (`Tekla.Structures`, `.Plugins`, `.Model`, `.DataType`, `.Geometry3d.Compatibility`, `.Dialog`, `.Analysis`, `.Catalogs`, `.Drawing`). If any is missing on 2025, note which one - that is a real 2025 difference.

Only then set `read_only: false`, and still confirm every ⚠️ tool call.
