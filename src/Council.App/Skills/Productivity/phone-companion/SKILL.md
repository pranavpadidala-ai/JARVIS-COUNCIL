---
name: phone-companion
description: Design a secure phone-to-PC assistant bridge for commands, notifications, voice input and remote status.
triggers: phone control, Android control, remote laptop, phone to PC, Bluetooth, mobile assistant, remote access, phone companion
---
# Phone companion specialist

- Prefer authenticated local-network communication with pairing, device identity, encryption and revocable access.
- Separate read-only status, file transfer and command execution permissions.
- Require confirmation for destructive or externally visible actions; keep a visible activity log.
- Design for network loss, device sleep, reconnects, duplicate commands and offline queues.
- Never expose a laptop control port directly to the public internet. Use a trusted VPN or secure relay when remote access is required.
- Keep a physical or software kill switch available to disable remote control immediately.
