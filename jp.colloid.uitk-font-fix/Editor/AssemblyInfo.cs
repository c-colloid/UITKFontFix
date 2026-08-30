using System.Runtime.CompilerServices;

// The package's own EditMode tests exercise internal plumbing (the
// shared settings form, the serialized settings DTO) without widening
// the public API surface.
[assembly: InternalsVisibleTo("Colloid.UitkFontFix.Editor.Tests")]
