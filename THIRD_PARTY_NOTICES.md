# Third-party notices

The following projects are used by GitHub Actions or release infrastructure. They are CI dependencies and are not included in the installed plugin or the Windows companion executable.

| Component | Pinned revision | License | Upstream |
| --- | --- | --- | --- |
| `actions/checkout` | `11d5960a326750d5838078e36cf38b85af677262` | MIT | https://github.com/actions/checkout |
| `actions/setup-node` | `49933ea5288caeca8642d1e84afbd3f7d6820020` | MIT | https://github.com/actions/setup-node |
| `actions/upload-artifact` | `ea165f8d65b6e75b540449e92b4886f43607fa02` | MIT | https://github.com/actions/upload-artifact |
| `actions/attest-build-provenance` | `96b4a1ef7235a096b17240c259729fdd70c83d45` | MIT | https://github.com/actions/attest-build-provenance |
| Gitleaks CLI | `8.30.1` | MIT | https://github.com/gitleaks/gitleaks |
| SignPath signing-request action | `b9d91eadd323de506c0c81cf0c7fe7438f3360fd` | See upstream repository terms | https://github.com/SignPath/github-action-submit-signing-request |

The SignPath action runs only inside the GitHub-hosted release workflow and is not copied into a plugin installation or release asset. The installed plugin itself uses Node.js built-in modules and .NET Framework system libraries.
