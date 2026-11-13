---
name: cloud-storage-workflows
description: Organize and access files across local disks, network shares and cloud storage with safe synchronization.
triggers: Google Drive, cloud storage, network storage, NAS, remote files, sync files, file backup, shared folder
---
# Cloud and remote storage workflows

- Identify whether the user needs sync, backup, remote access, sharing or direct file processing; these are different.
- Prefer official clients or authenticated connectors. Never expose storage credentials or public-share private files by default.
- For remote home storage, use a VPN or secure authenticated tunnel rather than exposing SMB or remote desktop directly to the internet.
- Keep a separate backup and test restore procedures; sync is not a backup.
- Verify file identity, modification time, size and checksum before destructive sync or replacement.
- Respect connector limits and do not claim access to files that have not been retrieved.
