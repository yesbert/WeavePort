# Design

Use existing multilingual native adapter fixtures with explicit interpreter paths in a Python runner. Windows exercises stdio; Linux exercises stdio and Unix sockets. Keep current package-release validation distinct from source CI evidence. Do not claim benchmarks or installer qualification.

CodeQL scans C#, JavaScript/TypeScript, Python and Actions using build mode none. C# generated-code coverage is limited in this mode; compilation is verified by CI. Follow https://github.com/github/codeql-action.

A reusable deployment workflow runs only for main pushes after documentation, macOS and native checks pass. It downloads that run's tested site artifact, uses a main-only environment and a dedicated SSH key restricted to a static archive receiver. No PR workflow receives deployment secrets. The receiver validates archive paths/types/size, retains releases and atomically selects the current release; an older CI run cannot replace a newer one. Avoid workflow_run privilege escalation and copying Stratara's credentials. Environment branch policy follows https://docs.github.com/en/actions/reference/workflows-and-actions/deployments-and-environments.
