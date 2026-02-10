# VisionPro Automation Suite

This project provides a comprehensive suite for programmatically interacting with Cognex VisionPro `.vpp` files. It includes a C# server to interface directly with the VisionPro API and a Python client to send commands, allowing for powerful automation, tuning, and debugging workflows without needing to open the VisionPro IDE for every adjustment.

This suite was packaged by Claude's VisionPro Expert skill.

## Components

- **`VppDriver/`**: Contains the C# source code for the server application (`VppDriver.exe`).
- **`VppProject/`**: Contains the example VisionPro project file, `test.vpp`.
- **`claude_skill/`**: Contains the complete source for the `visionpro-expert` skill, ready to be used with the Claude Code CLI.

---

## Setup and Installation
### 0.Install Claude code

### 1. Compile the C# Server
- You need **Visual Studio** with .NET Framework development tools installed.
- You must have **Cognex VisionPro** installed on the same machine to ensure all required assembly references are available.
- Open the C# project file (`VppDriver.csproj`) located in the `VppDriver/` directory with Visual Studio. Visual Studio will automatically create a solution file (`.sln`) for you if one doesn't exist.
- Build the project (usually by pressing `F6` or `Ctrl+Shift+B`). This will generate `VppDriver.exe` in the `VppDriver/bin/Debug/` (or `Release/`) folder.

### 2. Python Client
- The client script `vpp_controller.py` has no external dependencies beyond a standard Python 3 installation.

---

## How to Use

### Step 1: Start the VppDriver Server
Open a terminal (like Command Prompt or PowerShell) and run the compiled executable. You must provide two arguments: the command `server` and the full path to your `.vpp` file.

```bash
# Example from your project's root directory
./VppDriver/Debug/VppDriver.exe server "./VppProject/test.vpp"
```
The server will start and print `Server started. Waiting for connections...`. It is now ready to receive commands.

### Step 2: Use the Python Client to Send Commands
Open a **separate** terminal to run the Python client.

The `claude_skill/visionpro-expert/` directory contains the original skill definition. The client script is located at `claude_skill/visionpro-expert/scripts/vpp_controller.py`. If you are a Claude Code user, you can install this skill to integrate all of this functionality directly into your development workflow.

To use the client directly:
```bash
# Make sure the VppDriver server is running in another terminal
python claude_skill/visionpro-expert/scripts/vpp_controller.py list_tools
python claude_skill/visionpro-expert/scripts/vpp_controller.py get CogFixtureTool1 RunParams.UncalibratedOriginX
```

To install the skill for Claude Code, simply copy the `visionpro-expert` directory into your Claude skills folder (e.g., `~/.claude/skills/`).

