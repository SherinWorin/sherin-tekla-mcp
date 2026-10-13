# Core contracts

This project contains dependency-free, API-neutral contracts for the assistant MVP. It deliberately has no vendor references, external packages, or integration implementation.

Integration is blocked until the user provides verified Tekla Structures 2025 assemblies and official documentation. At that point, add a separate adapter project and map these contracts only to documented APIs; do not add guessed namespaces, class names, members, or DLL versions here.
