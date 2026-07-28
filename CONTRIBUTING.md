# Contributing

Open an issue before a substantial API, asset ABI, or architecture change.
Keep pull requests focused and explain the consumer problem they solve.

## Development environment

- Unity `2022.3.62f2c1`;
- a consumer-owned validation project;
- this repository cloned as a package-root sibling;
- generated Audio Keys committed by the consuming Host.

Reference a local clone from the Host project's `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.orpheus.audio": "file:../../Orpheus-Audio"
  }
}
```

## Required checks

- compile the package in Unity;
- run the package EditMode and PlayMode suites;
- import and validate both package samples;
- preserve `Tests/Baselines` unless the compatibility change is deliberate;
- keep Markdown links and `package.json` valid.

Runtime hot paths must remain allocation-free. Do not add logging, LINQ,
closures, boxing, string formatting, or temporary collections to playback,
ticks, admission, stealing, or Unity lifecycle callbacks.

`Orpheus.Audio.Core` may reference only `System`. Unity objects remain in
`Orpheus.Audio`.

## Security

Do not include credentials, signing material, device identifiers, local paths,
private evidence, or vendor-comparison research. Follow
[SECURITY.md](SECURITY.md) for vulnerability reports.
