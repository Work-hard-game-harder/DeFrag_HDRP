# Project development rules

- This project is a two-player multiplayer game. Treat multiplayer ownership, authority, spawning, scene transitions, and per-player local presentation as first-class constraints in every implementation and review.
- Preserve the networking teammate's architecture. Do not replace or substantially redesign networking code unless the user explicitly requests it. Prefer narrow interfaces, adapters, and events that can connect to server-authoritative state.
- Gameplay state that affects both players must have an explicit authority and synchronization plan. Camera, screen overlays, input, and other presentation effects must run only for the owning local player.
- Prefer object-oriented components with a single responsibility, serialized configuration, interfaces, and events. Avoid magic numbers, scene-name lookups, global searches, duplicated logic, and other hardcoding where practical.
- Keep network state, gameplay rules, and local presentation separated so each can be tested independently.

