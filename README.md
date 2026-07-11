# LabelForge

LabelForge is a Windows desktop annotation tool for image labeling workflows. It supports LabelMe JSON persistence, YOLO export, class management, manual drawing tools, and AI-assisted labeling with YOLO detection/segmentation.

## Current Features

- Image and dataset folder browsing
- Rectangle, polygon, polyline, point, and ellipse annotation tools
- Label class management with colors and hotkeys
- LabelMe JSON load/save
- YOLO, COCO, Pascal VOC, and LabelMe export paths
- YOLO ONNX auto-label configuration and quick-run buttons
- Central local model folder for downloaded ONNX models

## Requirements

- Windows
- .NET SDK 10.0.300 or newer compatible 10.0 feature band
- Visual Studio with .NET desktop development workload, or the .NET SDK for command-line builds

## Build

```powershell
dotnet restore LabelForge.slnx
dotnet build LabelForge.slnx
```

## Test

```powershell
dotnet test LabelForge.slnx
```

## Run

```powershell
dotnet run --project src/LabelForge.App/LabelForge.App.csproj
```

## AI Models

Model files are intentionally not committed to the repository. The app downloads or reads ONNX models from its local model library:

- app-local `models/` folder when writable
- fallback `%AppData%\LabelForge\models`

Large model files such as `.onnx` and `.onnx.data` are ignored by Git.

## Repository Notes

This repository currently has no license file. Until a license is added, all rights are reserved by default.
