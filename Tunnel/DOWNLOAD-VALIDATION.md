# Tunnel download validation: 19 October 2026

The one-click launcher was tested operationally by the owner. After correcting the locally entered runtime key, the connected remote MCP tools returned Running, successful database metadata and selected Postman metadata. No SQL changes, procedure executions or Postman application requests were made during that verification.

Chrome nevertheless reported **Dangerous download blocked** for the Actions run **39207338884**, artifact **12166261693**. That warning is unresolved. These observations concern different things: functional connectivity is not software security clearance.

## Exact scanned candidate

- Source head: `29f1817bab0ad6f49c3c42353575085e7b9d5fe9`.
- Outer GitHub artifact SHA256: `e3952df86e536c12a1840f0183ba7efc09c3d49fbfbd8c369c3ca83e76a5b251`, matched GitHub metadata.
- Inner ZIP SHA256: `35860be40532e38ba6ae0c776e1c333efb19e8ee98d87b6beb8629b977caf9d6`, matched SHA256SUMS.txt.
- Defender custom scans of the previous artifact, candidate archive and complete extracted candidate returned exit 0 and **found no threats**, with remediation disabled.
- Defender product `4.18.26090.9`, engine `1.1.26090.9`, signatures `1.461.98.0` updated that day. Protection was not disabled or reconfigured.
- Previous package: 923 files; candidate: 926. Added Start-All.cmd, Tunnel/Start-All.ps1 and the blank launcher.example.ini. Documentation/build metadata and rebuilt project binaries also changed. Upstream tunnel binaries/dependencies matched the previous package. No source-code change to SQL/Postman tools was introduced by the launcher commit.
- Extracted package inventory, credential/path checks, vendor hashes, isolated MCP initialization/tool discovery and Windows PowerShell 5.1 profile setup passed. App/adapter and inspected upstream tunnel executables were unsigned.

No exact trigger for Chrome's classification was established. This is not a confirmed false positive, a multi-engine clearance or a guarantee of safety. Rebuilding changes the artifact: this result does not automatically cover subsequent hashes. Neither repacking nor renaming is an established fix.

Before recommending a colleague rollout, obtain Google review of the exact flagged download and retest with Chrome protection enabled. Publisher signing of owned executables is an improvement to consider, requiring an appropriate certificate; it cannot guarantee classification. No vendor submission or public tunnel release has been made as part of these scans.

[Google download-blocking guidance](https://support.google.com/chrome/answer/6261569?hl=en). Do not instruct recipients to disable protection to make the package downloadable.
