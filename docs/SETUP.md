# Setup (Windows, Tekla Structures 2025, Claude Desktop)

You need: Windows 10/11 x64, Tekla Structures 2025, Claude Desktop. To build it yourself: Visual Studio 2022 (".NET desktop development" workload). No PowerShell is needed.

## 1. Get the server

**Option A - download the build (no Visual Studio needed)**

1. Open https://github.com/SherinWorin/sherin-tekla-mcp/actions
2. Click the latest green **CI** run, scroll to **Artifacts**, download `sherin-tekla-mcp-tekla2025-net48`.
3. Unzip to, for example, `C:\Users\sheri\Sherin_AI\sherin-tekla-mcp-build\`

**Option B - build in Visual Studio**

1. Visual Studio -> *Clone a repository* -> `https://github.com/SherinWorin/sherin-tekla-mcp.git` -> folder `C:\Users\sheri\Sherin_AI\sherin-tekla-mcp`
2. Open `SherinTeklaMcp.sln`, choose **Release**, then **Build -> Build Solution**.
3. The server is at
   `C:\Users\sheri\Sherin_AI\sherin-tekla-mcp\src\SherinTeklaMcp.Server\bin\Release\net48\SherinTeklaMcp.Server.exe`

Or from a command line (run in **Command Prompt (Windows)** or the Visual Studio *Developer Command Prompt*):

```bat
:: Command Prompt (Windows)
cd C:\Users\sheri\Sherin_AI
git clone https://github.com/SherinWorin/sherin-tekla-mcp.git
cd sherin-tekla-mcp
dotnet build SherinTeklaMcp.sln -c Release
```

The Tekla DLLs are **not** copied into the build. At start-up the server loads them from your installed Tekla 2025 (`...\Tekla Structures\2025.0\bin`). This is deliberate - see [API_NOTES.md](API_NOTES.md).

## 2. Settings

Edit `config\settings.json` **next to the exe** (comments are allowed):

| Setting | Default | Meaning |
|---|---|---|
| `ReadOnly` | `true` | Every model-changing tool is refused. Start like this. |
| `AllowDelete` | `false` | Delete stays off even when ReadOnly is false. |
| `HighRiskObjectCount` | `20` | More objects than this in one change = HIGH risk. |
| `MaxObjectsPerWrite` / `PerRead` / `ToScan` | 200 / 1000 / 50000 | Hard limits per call. |
| `ConfirmTokenMinutes` | `10` | How long a preview can be applied. |
| `MinMemberLengthMm` | `10` | Shorter members are refused/flagged. |
| `TeklaBinDir` | empty | Only if auto-detect fails. Auto-detect checks the running Tekla, `C:\Program Files\Tekla Structures\2025.0\bin` and `C:\TeklaStructures\2025.0\bin` (your install). |
| `LogDirectory`, `ExportDirectory` | empty | Default `%LOCALAPPDATA%\SherinTeklaMcp\logs` and `\exports` |
| `RulesDirectory` | empty | Default `config\rules` next to the exe |

Settings are read once: **restart Claude Desktop after a change.**

## 3. Add it to Claude Desktop

Claude Desktop -> **Settings -> Developer -> Edit Config** opens `claude_desktop_config.json`. Add the `sherin-tekla` entry inside `"mcpServers"` (keep any servers you already have, separated by commas):

```json
{
  "mcpServers": {
    "sherin-tekla": {
      "command": "C:\\Users\\sheri\\Sherin_AI\\sherin-tekla-mcp\\src\\SherinTeklaMcp.Server\\bin\\Release\\net48\\SherinTeklaMcp.Server.exe"
    }
  }
}
```

(Use the unzip folder instead if you downloaded the build.) Save, then fully quit and reopen Claude Desktop.

> You already have the teknovizier `tekla-mcp` server. Both can run together, but the AI then sees two similar tool sets. While testing this one, turn the other off in Claude Desktop's connector list.

## 4. First test (read-only)

1. Start Tekla Structures 2025 and open a **copy** of a model.
2. Ask Claude: *"Check the Tekla connection."* -> `tekla_get_connection_info` should show `connected: true`, your model name, the Tekla bin folder and `Model.dll` version 2025.x.
3. Select a few members in Tekla and ask: *"What is selected?"*
4. *"Total running length and weight per profile of the selection, save as CSV."*
5. *"Check the selection for errors and highlight problems."*

## 5. Allow model changes

Set `"ReadOnly": false`, restart Claude Desktop, then try: *"Create an IPE200 S355JR beam from 0,0,3000 to 6000,0,3000."* Claude should show you a **preview** first and only create the beam after you say yes. Check the log in `%LOCALAPPDATA%\SherinTeklaMcp\logs`.

## Try it without Tekla

Set the environment variable `SHERIN_TEKLA_MCP_MOCK=1` for the server to use a built-in sample frame (9 columns, 12 beams):

```json
"sherin-tekla": {
  "command": "C:\\...\\SherinTeklaMcp.Server.exe",
  "env": { "SHERIN_TEKLA_MCP_MOCK": "1" }
}
```

## Troubleshooting

| Problem | Fix |
|---|---|
| `Tekla Structures 2025 'bin' folder not found` | Start Tekla 2025 first, or set `TeklaBinDir`. |
| `This server is built for Tekla 2025, but the Tekla found ... is version 2024/2026` | Point `TeklaBinDir` to the 2025 `bin` folder. |
| `Not connected` / `stopped answering` | Tekla must be running with a model open. If Tekla was restarted, restart Claude Desktop. |
| Server does not appear in Claude | Check the exe path in the JSON (double backslashes), then look at the Claude Desktop MCP log (Settings -> Developer). |
| `READ-ONLY mode` | Expected until you set `"ReadOnly": false`. |
