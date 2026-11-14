# Thinking Space portable session v1 fixtures

These files exercise the approved `thinking-space-session` format version 1. JSON import creates a new local session identity while preserving canonical idea, relationship, conversation, and request identifiers. Linked cloud synchronization is a separate operation and must use an owner-scoped ID mapping plus an expected revision.

Unity's `JsonUtility` ignores unknown JSON properties. Consequently, `v1-unknown-field.json` demonstrates a known lossy behavior and is not a valid lossless import fixture. The strict website adapter rejects that file. Unsupported versions are also rejected without rewriting their source bytes.

The original canonical JSON must be retained whenever unknown information cannot be represented. Future portable fields require an approved new format version rather than an undocumented v1 addition.
