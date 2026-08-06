# ADR 0001: Capability-oriented providers and explicit SQLite migrations

- Status: Accepted
- Date: 2026-08-06

## Context

CloudKeeperSN must later support cloud, local, external-drive, and NAS storage without pretending that every provider supports the same operations. Transfer state must survive crashes, and the initial schema is compact and update-oriented.

## Decision

Use a small base `IStorageProvider` for identity/account discovery and separate capability interfaces for browse, read, native export, folder creation, and write sessions. Keep SDK types within provider projects.

Use `Microsoft.Data.Sqlite` with numbered SQL migrations rather than EF Core. Enable WAL and foreign keys. Model source/destination mappings and transfer states explicitly. Keep file content out of the database.

## Consequences

- Application services can ask for only the capabilities they need.
- Read-only Google access cannot accidentally acquire a delete/write method through a broad interface.
- Provider adapters require explicit mapping code but do not leak SDK objects.
- SQL migrations and hydration code are more verbose than an ORM, but persistence transitions remain visible and testable.
- Schema evolution must add a new numbered migration; an applied migration must never be edited in place.

