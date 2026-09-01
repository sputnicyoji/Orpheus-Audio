# Orpheus Audio

Unity-native game-audio runtime with deterministic Editor authoring, explicit
session ownership, and a pure C# policy core.

## Compatibility

| Surface | Status |
| --- | --- |
| Unity `2022.3.62f2c1` | Minimum; Windows Editor and Windows Standalone Supported |
| Unity `6000.3.12f1` | Windows Editor automated compatibility verified |
| Unity 6000 Windows Standalone | Not re-certified by `0.3.4` |
| Android | Experimental |
| iOS, macOS, WebGL, other platforms | Unsupported until verified |

Package `0.3.4` declares Public Contract v1. It remains pre-`1.0`.

## Install

Add the pinned Git dependency to the consuming project's
`Packages/manifest.json`:

Git installation requires Git `2.14.0` or newer and the Git executable on the
system `PATH`.

```json
{
  "dependencies": {
    "com.orpheus.audio": "https://github.com/sputnicyoji/Orpheus-Audio.git#v0.3.4"
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

The separate `Minimal Setup` sample is intentionally silent. It demonstrates
session ownership, Listener handoff, persisted gains, and teardown without
production content.

## M1 authoring automation

M1 provides deterministic recipe-driven generation for Audio Events, the
Catalog, and typed Audio Keys. Runtime playback remains independent from the
Editor compiler.

1. Create Module Recipe assets and one Audio Authoring Profile.
2. Create an empty Catalog at
   `<generatedRoot>/OrpheusAudioCatalog.asset`.
3. Assign the Host Manifest, Recipes, Catalog, and generated root.
4. Assign the Authoring Profile to the enabled Validation Profile and select
   that Validation Profile asset.
5. Run `Tools > Orpheus > Analyze Enrolled Authoring`.
6. Review the report under `Library/Orpheus/AuthoringAnalysis`.
7. Run `Tools > Orpheus > Accept And Compile Enrollment`.
8. Run `Tools > Orpheus > Validate Audio Profiles`.

Enrollment is explicit and sticky. Analyze is read-only. Compile is
transactional. Tracked-orphan deletion requires a second confirmation.
Recipes, Manifest changes, and generated outputs belong in the same Host
commit with the Authoring Profile and enrolled Validation Profile.

Read [Authoring and Validation](Documentation~/authoring-and-validation.md)
before enrolling existing manual content. It defines the serialized recipe
contract, command permissions, batchmode carrier, ownership boundary,
manual-to-enrolled migration, and repair map.

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
