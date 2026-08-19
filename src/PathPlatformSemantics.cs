namespace Icod.Path;

/// <summary>Identifies the pathname grammar used by a canonical-path operation.</summary>
public enum PathPlatformKind {
	/// <summary>Use POSIX slash-rooted pathname semantics.</summary>
	Posix,
	/// <summary>Use Windows drive, UNC, and backslash pathname semantics.</summary>
	Windows,
}

/// <summary>
/// Describes pathname root, volume, separator, and comparison behavior independently from the host
/// on which a test or command happens to execute.
/// </summary>
public sealed class PathPlatformSemantics {
	private PathPlatformSemantics(
		PathPlatformKind kind,
		char directorySeparator,
		char? alternateDirectorySeparator,
		StringComparison pathComparison
	) {
		this.Kind = kind;
		this.DirectorySeparator = directorySeparator;
		this.AlternateDirectorySeparator = alternateDirectorySeparator;
		this.PathComparison = pathComparison;
		this.PathComparer = StringComparison.OrdinalIgnoreCase == pathComparison
			? StringComparer.OrdinalIgnoreCase
			: StringComparer.Ordinal
		;
	}

	/// <summary>Gets deterministic POSIX pathname semantics.</summary>
	public static PathPlatformSemantics Posix { get; } = new(
		PathPlatformKind.Posix,
		'/',
		null,
		StringComparison.Ordinal
	);

	/// <summary>Gets deterministic Windows pathname semantics.</summary>
	public static PathPlatformSemantics Windows { get; } = new(
		PathPlatformKind.Windows,
		'\\',
		'/',
		StringComparison.OrdinalIgnoreCase
	);

	/// <summary>Gets the pathname semantics of the current host operating system.</summary>
	public static PathPlatformSemantics Host =>
		OperatingSystem.IsWindows()
			? Windows
			: Posix
	;

	/// <summary>Gets the pathname grammar.</summary>
	public PathPlatformKind Kind { get; }

	/// <summary>Gets the canonical directory separator emitted by shared pathname operations.</summary>
	public char DirectorySeparator { get; }

	/// <summary>Gets the accepted alternate separator, or <see langword="null"/> when none exists.</summary>
	public char? AlternateDirectorySeparator { get; }

	/// <summary>Gets the comparison used for pathname roots, volumes, and components.</summary>
	public StringComparison PathComparison { get; }

	/// <summary>Gets the comparer used for pathname roots, volumes, and components.</summary>
	public StringComparer PathComparer { get; }

	/// <summary>Reports whether a character is a directory separator in this pathname grammar.</summary>
	/// <param name="value">The character to inspect.</param>
	/// <returns><see langword="true"/> when the character is an accepted directory separator.</returns>
	public bool IsDirectorySeparator( char value ) =>
		value == this.DirectorySeparator
		|| this.AlternateDirectorySeparator == value
	;
}

/// <summary>Describes the root and volume parsed from an absolute pathname.</summary>
/// <param name="RootPath">The canonical root spelling, including its trailing separator.</param>
/// <param name="VolumeName">The comparison identity of the root volume.</param>
public sealed record PathRootInfo(
	string RootPath,
	string VolumeName
);
