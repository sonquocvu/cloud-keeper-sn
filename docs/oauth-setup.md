# OAuth setup (for later checkpoints)

Checkpoint 1 does not contain real OAuth implementations. The steps below document the registrations that Checkpoints 2 and 3 will require; do not invent or commit credentials.

## Google Drive registration

1. Create a Google Cloud project owned by the user/team.
2. Enable Google Drive API.
3. Configure the OAuth consent screen.
4. Create an OAuth desktop application/client suitable for authorization-code flow with PKCE.
5. Request only Google Drive read-only access (`drive.readonly`) for the MVP.
6. Put the client ID and loopback redirect URI in an untracked local configuration derived from `.env.example`.

The application must use the system browser. It must not request or store the Google password. “Shared with me” is excluded unless the selected accessible item explicitly belongs to the chosen source.

## Microsoft identity registration

1. Register a public/native client application.
2. Allow personal Microsoft accounts (`consumers`).
3. Configure the loopback redirect URI used by the desktop client.
4. Enable authorization-code flow with PKCE; do not use a client secret in the desktop application.
5. Request the smallest delegated Microsoft Graph scope that supports user-selected OneDrive backup operations. Final scope choice belongs to Checkpoint 3 and must be documented after verification against current Microsoft guidance.

## Token storage and disconnect

Future token caches must be encrypted for the current Windows user using DPAPI, kept outside the repository, and excluded from diagnostics. **Ngắt kết nối tài khoản** must revoke or clear the local cache and update account metadata. Revoked/expired sessions must show a concise Vietnamese reconnect message.

## Integration tests

Live tests will be optional, separated from unit tests, and skipped unless explicit environment configuration is present. Never use a personal production account for automated destructive tests.

