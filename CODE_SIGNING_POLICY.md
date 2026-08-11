# Code Signing Policy

Silly-codex uses Authenticode signing for the Windows companion executable. The project intends to use SignPath Foundation for public open-source releases.

Free code signing provided by [SignPath.io](https://signpath.io/), certificate by [SignPath Foundation](https://signpath.org/).

## Team roles

- Committer and reviewer: [foryourhealth111-pixel](https://github.com/foryourhealth111-pixel)
- Signing approver: [foryourhealth111-pixel](https://github.com/foryourhealth111-pixel)

All members with source or signing access must enable multi-factor authentication for GitHub and SignPath. External contributions require maintainer review before merge. The signing approver reviews every signing request.

Privacy policy: [`PRIVACY.md`](PRIVACY.md).

## Release gate

1. A GitHub-hosted Windows runner builds `InstructionSwitcherCompanion.exe` from the tagged source.
2. The unsigned artifact is uploaded to GitHub Actions.
3. A SignPath signing request is submitted with the configured project and signing policy.
4. A maintainer reviews and approves the request in SignPath.
5. The signed artifact is downloaded and verified with `signtool verify /pa /all /v`.
6. The verified file, `LICENSE`, and `NOTICE` are attached to the GitHub Release; the EXE also receives an artifact attestation.

The repository does not store a private key, certificate password, or signing certificate. A workflow without the required SignPath secrets may run validation jobs; the release-signing job stops before publishing an unsigned executable.

## Required configuration

After SignPath approval, configure these GitHub Actions secrets or protected variables:

- `SIGNPATH_API_TOKEN`
- `SIGNPATH_ORGANIZATION_ID`
- `SIGNPATH_PROJECT_SLUG`
- `SIGNPATH_SIGNING_POLICY_SLUG`

The SignPath GitHub App must have access to this repository, and signing requests must originate from GitHub-hosted runners.

## Verification

Maintainers record the signer, signing request URL, source tag, and SHA-256 of the final signed executable in the release notes. A failed signature verification blocks the release.
