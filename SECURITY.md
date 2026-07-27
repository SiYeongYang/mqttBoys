# Security

## Connection data

mqttBoys stores broker profiles only in `%APPDATA%\MqttPulse\profiles.json`. This
file is not part of the source repository and is never uploaded by the app.

The local profile file can contain broker usernames, passwords, certificate
paths, and SSH settings. Protect the Windows account that owns the file and do
not copy the profile file into the repository.

## Repository hygiene

Do not commit real broker addresses, credentials, private keys, certificates, or
captured production payloads. The repository ignores common profile, secret,
certificate, and private-key file names as an additional safeguard.

Use reserved example addresses such as `192.0.2.0/24` and example domains in
tests and documentation.

## Reporting

For a suspected vulnerability, use GitHub's private vulnerability reporting
instead of opening a public issue containing credentials or connection details.
