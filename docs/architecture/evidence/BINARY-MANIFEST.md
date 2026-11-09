# Evidence binary manifest (R1-073)

The 12 native DLL binaries that used to live under this directory were
removed from the repository on 2026-10-26 (audit item R1-073, user decision:
"remove binaries, keep JSON/TRX evidence"). The binaries are reproducible
build outputs of the ARM64 stage-7b/7c0 gates — re-run
`scripts/run-arm64-stage-7b-runtime.ps1` and
`scripts/audit-rust-crt-distribution.ps1 -Platforms ARM64` (or the
`ARM64 Runtime Gate` workflow) to regenerate them. The audit value of this
directory lives in the JSON summaries and TRX results, which remain.

For any future comparison against CI artifacts or installed payloads, the
removed binaries are pinned here by SHA-256:

| SHA-256 | Path (as previously committed) | Size |
| --- | --- | --- |
| `970f46e8016ad5b9017d7683c127eee3b091f6ad8f4fd6b527d2ca1056a75b16` | `32644378767/arm64-stage7b-runtime-evidence/native/deskbox_native.dll` | 150,528 |
| `65c52250522a44ed08aac64174d80277ac33cc2ba50bfbe26ccf350f4207ebbe` | `32644378767/arm64-stage7b-runtime-evidence/search/deskbox_search_core.dll` | 161,792 |
| `354841ad49c22e9df4945d2b07827812ebbb001382624c890f6a5e50c85a3e37` | `32644378767/arm64-stage7c0-crt-evidence/arm64/dynamic/native/deskbox_native.dll` | 150,528 |
| `d1e7e8f13ed4e25bdccb8e38c1fe1e97275b5eeb4aa520380cef40faa4e1c41d` | `32644378767/arm64-stage7c0-crt-evidence/arm64/dynamic/search/deskbox_search_core.dll` | 161,792 |
| `47d12841127475da68c6a196c53aa79a0cf5d156881fab73264aa529480e5228` | `32644378767/arm64-stage7c0-crt-evidence/arm64/static/native/deskbox_native.dll` | 246,784 |
| `7e922c31e472df1d6c5bfe5e3248c4331f38467712627a68960f982cbb574fc8` | `32644378767/arm64-stage7c0-crt-evidence/arm64/static/search/deskbox_search_core.dll` | 256,512 |
| `c8fd39a4d18954d272492918ae54ea57bf61e5467d6d484f54ef993f4beb968c` | `32645299871/arm64-stage7b-runtime-evidence/native/deskbox_native.dll` | 246,784 |
| `3c5c456a7a548da0efd6575e911fae5c87788bfff7c2120e5248ae57d4cb5201` | `32645299871/arm64-stage7b-runtime-evidence/search/deskbox_search_core.dll` | 256,512 |
| `008f7d7b8bc322f33f35b659cbd3fde1090f7df638b6f73a22060772a8d9335c` | `32645299871/arm64-stage7c0-crt-evidence/arm64/dynamic/native/deskbox_native.dll` | 150,528 |
| `4abcdc9e7eeb0e8a407cae652d94c18957872ce4a9f2b1ccd2880823dd3c16ee` | `32645299871/arm64-stage7c0-crt-evidence/arm64/dynamic/search/deskbox_search_core.dll` | 161,792 |
| `25a07ca5b1bde40cd30118d5bd9239955c8287fcdeedae0a05942a49e9f1fb29` | `32645299871/arm64-stage7c0-crt-evidence/arm64/static/native/deskbox_native.dll` | 246,784 |
| `ea4ed3254899d44fac0bea65673434446ea3924e25a7c3a3ef9cd2b15304cd91` | `32645299871/arm64-stage7c0-crt-evidence/arm64/static/search/deskbox_search_core.dll` | 256,512 |
