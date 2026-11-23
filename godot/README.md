# Starwright for Godot 4.7 (C#)

This folder is the C# port. The browser game at the repository root is unchanged and still runs with `./start.sh`.

Open `project.godot` in **Godot 4.7.2 .NET** (the mono build). You need the .NET 8 SDK. The part catalog and the stock rockets are embedded data exported from `js/core`, so the editor does not need Node.

```
dotnet test src/Starwright.Core.Tests
dotnet build Starwright.csproj
```

Pictures in `../media` are used as they are. The title screen reads `../media/shot-centre.jpg`.

The license is the MIT license at the repository root. Copyright (c) 2026 Jerry Smith. See `docs/PORT-PLAN.md`.
