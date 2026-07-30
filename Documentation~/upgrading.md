# Upgrading

Orpheus is pre-`1.0`. Pin an immutable release tag or full commit SHA and read
the [Changelog](../CHANGELOG.md) before changing the dependency.

## Compatibility policy

- Package `0.3.x` declares Public Contract v1.
- `0.3.x` permits compatible fixes.
- Public Contract v1 members are not removed or changed incompatibly before
  `1.0.0`.
- Serialized asset field identities and generated-key shape are part of the
  contract.
- Internal policy types are not consumer APIs.

Compatibility is enforced by:

```text
Tests/Baselines/PublicApi.v1.txt
Tests/Baselines/SerializationAbi.v1.txt
Tests/Baselines/GeneratedKeys.v1.golden.cs.txt
```

## Standard upgrade

1. Commit the consuming project.
2. Update the pinned tag or full commit SHA.
3. Wait for Package Manager resolution and script compilation.
4. Read `CHANGELOG.md` for the selected version range.
5. Regenerate typed keys from the active Key Manifest.
6. Run `Tools > Orpheus > Validate Audio Profiles`.
7. Run EditMode, PlayMode, and the imported sample smoke tests.
8. Verify supported target platforms before release.

Do not rewrite package-owned serialized fields through reflection.

## `0.2.2` to `0.3.0`

`0.3.0` adds the public Editor-only `OrpheusAudioModuleRecipe` and
`OrpheusAudioAuthoringProfile` asset contracts. Runtime behavior and existing
serialized Host assets remain compatible. Compiler orchestration, modes,
transactions, and reports remain internal.

To enroll existing manual content:

1. keep existing manual Events untouched;
2. create recipes and a separate generated root;
3. assign the Authoring Profile with an empty enrollment GUID;
4. Analyze and review the proposed Manifest baseline, ownership, and diff;
5. explicitly accept and compile enrollment;
6. review generated output, validate, run build lint, and commit recipes plus
   generated outputs together.

Enrollment has no v1 undo. A Manifest-authored same-ID symbol rename changes
the generated member and Event path without changing the numeric Audio Key or
Event GUID. No old-member alias is emitted.

## `0.2.1` to `0.2.2`

`0.2.2` corrects consumer documentation and immutable documentation links. It
does not change Public Contract v1, serialized asset ABI, generated-key shape,
or runtime behavior.

## `0.2.0` to `0.2.1`

`0.2.1` adds the MIT license, immutable public documentation links, and
consumer documentation. It does not change Public Contract v1 or serialized
asset ABI.

## `0.1.0` to `0.2.0`

`0.1.0` was an initial package scaffold. `0.2.0` is the first declared public
compatibility line.

The upgrade internalized authoring, Catalog lookup, clip selection, and
runtime policy types that were accidental exports. Consumer code must use:

- `OrpheusAudioFactory`;
- `OrpheusAudioManager`;
- typed or raw one-shot Bridges;
- public Host assets;
- generated `OrpheusAudioKeys`;
- public Core values referenced by those owners.

Remove direct uses of other Core policy types. Import the current sample,
compare the Host Composition Root and asset topology, regenerate keys, then
validate.

## Generated keys

Generation writes:

```text
Assets/OrpheusGenerated/Orpheus.Audio.Generated.asmdef
Assets/OrpheusGenerated/OrpheusAudioKeys.g.cs
```

Commit the generated projection with the Host project. Regenerate it whenever
the Manifest changes or after a package upgrade that changes the generator.

## Asset validation

Public Contract v1 freezes the serialized identity of supported Host assets.
A schema or validation failure is not repaired by editing YAML manually.

Use the current package Create Asset menus, sample assets, generator, and
validator. If a future release requires an asset migration, that release must
document the migration before the compatibility line changes.
