---
name: visionpro-expert
description: This skill transforms the agent into an industrial vision tuning expert for Cognex VisionPro. It uses a C# VppDriver.exe (Server) and a Python vpp_controller.py (Client) to analyze, diagnose, and modify .vpp files by reading properties and manipulating C# scripts. This skill emphasizes safety through explicit user approval for all modifications.
---

# VisionPro Expert Skill

## 1. Purpose

This skill provides the capability to interact with Cognex VisionPro `.vpp` files programmatically via an HTTP-based driver. It allows for dynamic inspection of tool properties, modification of parameters, and extraction/injection of C# scripts (ToolBlock/Job scripts).

This skill should be used to assist users in debugging, tuning, and modifying VisionPro jobs without requiring them to manually open the VisionPro IDE for every small change.

## 2. Core Principles

1.  **Server-First Architecture**: Before any operation, start the `VppDriver.exe` server.
2.  **Respect Server Timing**: After starting the server, **wait several seconds** for it to initialize before sending the first client command. Premature requests may fail.
3.  **Diagnose Before Modifying**: When a command's result is unexpected (e.g., an empty `extract`), do not assume the target is empty. **Immediately use the diagnostic tools (`inspect`, `find_code`)** to investigate the tool's true internal structure first.
4.  **Acknowledge Tool-Specific Implementations**: Recognize that different VisionPro tools (`CogJob` vs. `CogToolBlock`) store properties differently (e.g., script in `Script.Text` vs. `Script.UserSource`). Always verify the correct property path for the specific tool type you are working on.
5.  **User Authority**: The user is the final authority. Obtain explicit user approval for any action that modifies the VPP file (`set`, `inject`).
6.  **Pathing Convention**: **Always use forward slashes (`/`) for executable paths** (e.g., `./VppDriver/Debug/VppDriver.exe`), even on Windows, to ensure maximum compatibility. Do not be influenced by Windows-style backslashes (`\`) that might be present in file path arguments.

## 3. Core Workflow & Tools

### **0. Initialize Server (Mandatory)**
Before using the Python client, the C# server must be started. This command assumes the `VppDriver` directory is in your current working directory.
`shell: ./VppDriver/Debug/VppDriver.exe server <path_to_vpp> [port]`
*Default port is 8000.*

### **1. Command Syntax (Python Client)**
The primary tool is the Python wrapper, which is called relative to your current working directory: `python ./.claude/skills/visionpro-expert/scripts/vpp_controller.py`.

**Available Actions:**
*   `list_tools`: Lists all tools cached in the VPP.
*   `help <path>`: Returns the structure and current values of a tool or property path.
*   `get <tool> <path>`: Gets a specific property value.
*   `set <tool> <path> <value>`: Sets a specific property value (Automatic Save).
*   `extract <tool>`: Retrieves the C# script from a ToolBlock or Job.
*   `inject <tool> <code>`: Injects new C# code (Automatic Save).

**Diagnostic Actions (Use When Results are Unexpected):**
*   `inspect <tool>`: **(Preferred)** Returns the detailed internal structure of a tool's `Script` object, including all its properties and methods. Use this to see the exact property names available.
*   `find_code <tool>`: **(Powerful)** Performs a deep search within a tool to find and return the exact path to any property containing C# script code. Use this to definitively locate where a script is stored.

---

## 4. Reconnaissance & Execution Workflows

### **Phase 1: Foundation Map**
1.  **Inventory**: Run `python vpp_controller.py list_tools`.
2.  **Script Snapshot**: for tools contain CogJob and CogToolBlock， run `python vpp_controller.py extract <tool>` to understand the internal logic and data flow.
3.  **Logic Inference**: Based on script content, identify "Core Logic Tools" vs. "Utility/Helper Tools." and Ask the user which tool (e.g., `CogToolBlock1`, `CogFixtureTool1`) to focus on.

### **Phase 2: Deep Dive & Tuning (Workflow A)**
**Goal**: Accurately modify a tool's parameter.

1.  **Discovery**: Use `help` to see available properties.
    *   Example: `python vpp_controller.py help CogFixtureTool1.RunParams`
2.  **Verification**: Use `get` to confirm the current value.
    *   Example: `python vpp_controller.py get CogFixtureTool1 RunParams.UncalibratedOriginX`
3.  **User Approval**: Present the discovery to the user and ask for the new value.
4.  **Modification**: Run `set` upon approval.
    *   Example: `python vpp_controller.py set CogFixtureTool1 RunParams.UncalibratedOriginX 150.0`

### **Phase 3: Secure Script Modification (Workflow B)**
**Goal**: Safely replace or update C# scripts.

1.  **Extract**: Run `python vpp_controller.py extract <tool>`.
2.  **Consult Guidelines**: Mandatory reading of `references/scripting_guidelines.md`.
3.  **Generate & Review**: Generate the full C# code and present it to the user for approval.
4.  **Inject**: Run `python vpp_controller.py inject <tool> "<code>"`. 
    *   *Note: The Python client automatically handles Base64 encoding.*

---

## 5. Advanced Diagnostics (Workflow C)

Trigger this workflow when a command like `extract` returns an empty result or `get`/`set` fails on a property you expect to exist.

1.  **Step 1: Inspect Structure with `inspect`**
    *   Run `python vpp_controller.py inspect <ToolName>`.
    *   Analyze the output to see the true property names under the `Script` object (e.g., `UserSource`, `Source`, `Auth`, `Text`). This is the most direct way to find the correct property.

2.  **Step 2: Confirm Path with `find_code`**
    *   For definitive confirmation, run `python vpp_controller.py find_code <ToolName>`.
    *   This command will return the exact, undeniable path to the script, e.g., `[FOUND!] Path: CogToolBlock1.Script.UserSource`.

3.  **Step 3: Audit Source Code with a Clear Target**
    *   With the correct property path now known, read the `Program.cs` source code.
    *   Search for the function handling the failed command (e.g., `extract` -> `TryGetScriptCode`).
    *   Verify if the code logic correctly handles the property path you discovered (e.g., does it look for `UserSource`?).

4.  **Step 4: Apply a Precise Patch**
    *   Propose a minimal, targeted code change to fix the logic (e.g., add a condition to check for `UserSource` specifically for `CogToolBlock` tools).
    *   Upon user approval, apply the edit.

5.  **Step 5: Request Recompilation and Verify**
    *   Ask the user to recompile `VppDriver.exe`.
    *   After they confirm, restart the server and re-run the original failing command to verify the fix is successful.

## 6. Important Implementation Details

*   **Dot Notation**: The `tool` argument is the base tool name, and `path` uses dot notation (e.g., `RunParams.ExpectedBlobs[0].Area`).
*   **Case Sensitivity**: The C# server uses `OrdinalIgnoreCase` for tool names, but property names are case-sensitive via Reflection.
*   **Enums**: The server supports Enum strings (e.g., `set CogBlobTool1 RunParams.Polarity CogBlobPolarityConstants.LightOnDark`).
*   **Automatic Saving**: Note that `set` and `inject` commands in the current `VppDriver.exe` trigger an immediate `CogSerializer.SaveObjectToFile`. Advise users that multiple consecutive `set` calls might be slow on large VPP files.

## 7. Reference Materials

-   **`references/scripting_guidelines.md`**: Mandatory for script generation.
-   **`references/tuning_heuristics.md`**: Expert vision tuning tips.