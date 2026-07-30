# Orpheus Audio

Unity-native game-audio runtime with explicit session ownership and a pure C#
policy core.

## Compatibility

| Surface | Status |
| --- | --- |
| Unity | `2022.3.62f2c1` minimum and verified target |
| Windows Editor | Supported |
| Windows Standalone | Supported |
| Android | Experimental |
| iOS, macOS, WebGL, other platforms | Unsupported until verified |

Package `0.3.0` declares Public Contract v1. It remains pre-`1.0`.

## Install

Add the pinned Git dependency to the consuming project's
`Packages/manifest.json`:

Git installation requires Git `2.14.0` or newer and the Git executable on the
system `PATH`.

```json
{
  "dependencies": {
    "com.orpheus.audio": "https://github.com/sputnicyoji/Orpheus-Audio.git#v0.3.0"
  }
}
```

Alternatively, paste the same URL into:

```text
Window > Package Manager > + > Add package from git URL
```

Pin a release tag or full commit SHA. Do not pin production projects to a
moving branch.

For a local sibling clone:

```json
{
  "dependencies": {
    "com.orpheus.audio": "file:../../Orpheus-Audio"
  }
}
```

Unity resolves `file:` from the consuming project's `Packages` directory.

## Start here

1. Import the `Playable One Shot` sample from Package Manager.
2. Select its `PlayableOneShotKeyManifest.asset`.
3. Run `Tools > Orpheus > Generate Typed Keys`.
4. Run `Tools > Orpheus > Validate Audio Profiles`.
5. Open `PlayableOneShot.unity` and enter Play Mode.

For recipe-driven generation, assign an Audio Authoring Profile to the enabled
Validation Profile. Analyze first. Then explicitly accept enrollment before
using the enrolled compile commands. See
[Authoring and Validation](Documentation~/authoring-and-validation.md).

The separate `Minimal Setup` sample is intentionally silent. It demonstrates
session ownership, Listener handoff, persisted gains, and teardown without
production content.

## Documentation

- [Manual](Documentation~/index.md)
- [Getting Started](Documentation~/getting-started.md)
- [Authoring and Validation](Documentation~/authoring-and-validation.md)
- [Runtime Integration](Documentation~/runtime-integration.md)
- [API Reference](Documentation~/api-reference.md)
- [Diagnostics and Troubleshooting](Documentation~/diagnostics-and-troubleshooting.md)
- [Upgrading](Documentation~/upgrading.md)
- [Changelog](CHANGELOG.md)

## Public Contract v1

Supported consumer owners:

- `OrpheusAudioFactory`;
- `OrpheusAudioManager`;
- `OrpheusAudioBridge` and `OrpheusAudioRawBridge`;
- `OrpheusAudioRuntimeHost`;
- `OrpheusAudioEvent`, `OrpheusAudioCatalog`, `OrpheusAudioSettings`, and
  `OrpheusAudioSourceBank`;
- `OrpheusAudioKeyManifest` and `OrpheusAudioValidationProfile`;
- `OrpheusAudioModuleRecipe` and `OrpheusAudioAuthoringProfile`;
- generated `OrpheusAudioKeys`;
- public Core values required by those owners.

The checked-in
[public API baseline](Tests/Baselines/PublicApi.v1.txt),
[serialization ABI baseline](Tests/Baselines/SerializationAbi.v1.txt), and
[generated-key golden](Tests/Baselines/GeneratedKeys.v1.golden.cs.txt) define
the `0.3` compatibility line. Other Core policy and authoring types are package
implementation details.

Compiler modes, orchestration, transaction recovery, and machine reports are
package-internal. The public repository is generated release output, not an
independent authoring source.

## Runtime boundaries

- Host assets remain under the consuming project's `Assets` tree.
- Gameplay requests use generated Audio Keys, never `AudioClip` references.
- The Host explicitly creates, binds, and disposes each Audio Session.
- Orpheus creates no singleton, scans no Scene, and never writes
  `AudioListener.pause`.
- Audio failure degrades to silence and never blocks gameplay.

## License and security

Orpheus Audio is released under the [MIT License](LICENSE.md).
Report vulnerabilities through [SECURITY.md](SECURITY.md).
