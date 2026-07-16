using System.Resources;

// The neutral StringResources.resx holds the Vietnamese strings verbatim, so vi-VN is the neutral
// language: the runtime never loads a satellite assembly for Vietnamese (the default and any unsupported
// Accept-Language fold to the neutral resource), and only en-US is a satellite. See
// planning/localization-subsystem.md (D6).
[assembly: NeutralResourcesLanguage("vi-VN")]
