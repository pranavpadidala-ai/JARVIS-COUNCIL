---
name: iterative-retrieval
description: Progressive codebase search and context retrieval for implementation, debugging, and repository exploration
triggers: search files, find implementation, codebase context, repository exploration, missing context, trace call flow, understand architecture
---
# Iterative retrieval

Use progressive retrieval instead of loading a repository indiscriminately.

1. Start with a broad search using the user's task terms and likely file names.
2. Read the most relevant implementation, its callers, related types, tests, and configuration.
3. Score relevance mentally: direct implementation first, then callers/contracts, tests, and documentation.
4. Identify missing context explicitly: lifecycle, persistence, permissions, error handling, or compatibility.
5. Refine the search using discovered type names and terminology. Repeat at most three cycles.
6. Stop when the implementation path, contract, tests, and safety boundaries are understood.

Prefer focused file ranges and search results over entire large files. Do not invent files, APIs, or repository conventions. If the available tools cannot inspect a required file, state the limitation and continue only with verified context.
