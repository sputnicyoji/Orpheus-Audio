using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Orpheus.Audio.Tests")]
[assembly: InternalsVisibleTo("Orpheus.Audio.Editor")]
[assembly: InternalsVisibleTo("Orpheus.Audio.Editor.Tests")]
#if ORPHEUS_ANDROID_EVIDENCE
[assembly: InternalsVisibleTo("Orpheus.Audio.Verification.Ticket21")]
#endif
