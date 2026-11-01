# Public-source comparison and implementation decision

Audit date: 2026-10-31. Public-source pin: d57d9760dba0590416eca4c94379ce7653f347b5. Four local reference files were re-fetched from immutable raw GitHub URLs; all bytes match. See public-source/pin.json. No downloaded implementation is in this repository.

Warp already provides Blocks pairing commands with output, runnable parameterized notebooks, reusable workflows and team collaboration. Those capabilities are documented in the [official notebooks guide](https://docs.warp.dev/knowledge-and-collaboration/warp-drive/notebooks), [quickstart](https://docs.warp.dev/quickstart) and [Drive overview](https://docs.warp.dev/knowledge-and-collaboration/warp-drive). Recreating those features alone would add little to this portfolio.

The implemented experiment is a bounded local execution contract: executable/input byte commitments; a sealed literal argument vector; an explicit non-secret environment with owned temporary/profile folders; refusal on observed drift; output commitments; parent-exit/interruption/timeout/overflow distinctions; and an inspectable native baseline/candidate comparison. This is a proposed independent workflow, not proof that every equivalent behavior is absent from Warp stable, preview, source or plugins.

The pinned public README identifies the client as open source. Source-level engineering is therefore the appropriate route before native decompilation. The Windows bootstrap can install MSVC/SDK and auxiliary tools and can authenticate a cloud CLI. Running it would violate the portfolio's workspace-only constraint. It was read but not executed. Installed Warp, private terminal history, accounts and cloud configuration were not accessed.

Decision: deliver an original MIT native .NET application and CLI, plus source, exact packages, real synthetic execution evidence and a live feature page. The result is not a modified Warp binary, a Warp plugin, a PTY or a cloud-sync adapter. The public client could be a future separately validated integration target; no such integration is claimed here.

REA's Windows PTY/native-Warp runtime route was not used for this deliverable. No recovered implementation or decompiler output is presented as original source. The project is useful on its own and its scope is deliberately explicit.
