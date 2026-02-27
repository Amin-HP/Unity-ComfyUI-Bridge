Changelog

All notable changes to this project will be documented in this file.

[0.1.0] - 2025-12-04

Added

Initial release of Unity ComfyUI Bridge.

Core execution engine (ComfyWorkflowExecutor) for connecting Unity to ComfyUI.

Node-based configuration system in the Inspector (ComfyNodes).

Support for Text-to-Image, Image-to-Image, 3D Object, and Audio generation.

Runtime Queue system for managing multiple generation requests.

RenderTexture input support.

Automatic dependency installation (Newtonsoft.Json, glTFast).

[0.1.1] - 2026-02-25

Fixed

Workflow loading on Android (using UnityWebRequest instead of File.ReadAllText).

[0.1.2] - 2026-02-27

Changed

Updated documentation to include setup instructions for the sample workflows, and fixed the issue of example scenes.

Added

Sample workflows for Text-to-Image, Image-to-Image, 3D Object, and Audio generation.

