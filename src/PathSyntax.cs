namespace Icod.Path;

/// <summary>
/// Decomposes pathname text according to an explicit platform grammar without
/// normalizing components or observing the filesystem.
/// </summary>
public static class PathSyntaxParser {
	/// <summary>
	/// Parses pathname root structure and ordered components.
	/// </summary>
	/// <param name="path">The pathname text to decompose.</param>
	/// <param name="semantics">The pathname grammar to apply.</param>
	/// <returns>The decomposed pathname syntax.</returns>
	/// <exception cref="ArgumentException">
	/// <paramref name="path"/> is empty, contains a NUL character, or has a
	/// malformed Windows root.
	/// </exception>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="path"/> or <paramref name="semantics"/> is
	/// <see langword="null"/>.
	/// </exception>
	public static PathSyntaxParts Parse(
		string path,
		PathPlatformSemantics semantics
	) {
		ArgumentNullException.ThrowIfNull(
			path
		);
		ArgumentNullException.ThrowIfNull(
			semantics
		);
		if ( 0 == path.Length ) {
			throw new ArgumentException(
				"pathname is empty",
				nameof( path )
			);
		}
		if ( 0 <= path.IndexOf( '\0' ) ) {
			throw new ArgumentException(
				"pathname contains a NUL character",
				nameof( path )
			);
		}

		return PathPlatformKind.Windows == semantics.Kind
			? ParseWindows(
				path,
				semantics
			)
			: ParsePosix(
				path,
				semantics
			)
		;
	}

	private static PathSyntaxParts ParsePosix(
		string path,
		PathPlatformSemantics semantics
	) {
		var isAbsolute = semantics.IsDirectorySeparator(
			path[ 0 ]
		);
		var components = SplitComponents(
			path.AsSpan(
				isAbsolute
					? 1
					: 0
			),
			semantics
		);
		return new PathSyntaxParts(
			path,
			PathPlatformKind.Posix,
			isAbsolute
				? "/"
				: string.Empty,
			isAbsolute
				? "/"
				: string.Empty,
			components,
			isAbsolute,
			isDriveRelative: false,
			isCurrentVolumeRooted: false
		);
	}

	private static PathSyntaxParts ParseWindows(
		string path,
		PathPlatformSemantics semantics
	) {
		var canonical = null == semantics.AlternateDirectorySeparator
			? path
			: path.Replace(
				semantics.AlternateDirectorySeparator.Value,
				semantics.DirectorySeparator
			)
		;
		var root = PathLexicalNormalizer.ParseWindowsRoot(
			canonical
		);
		if ( root.IsInvalid ) {
			throw new ArgumentException(
				"pathname contains a malformed Windows root",
				nameof( path )
			);
		}

		var components = SplitComponents(
			canonical.AsSpan(
				root.ContentStart
			),
			semantics
		);
		var rootPath = root.IsCurrentVolumeRooted
			? semantics.DirectorySeparator.ToString()
			: (
				root.IsDriveRelative
					? root.VolumeName
					: root.RootPath
			)
		;
		return new PathSyntaxParts(
			path,
			PathPlatformKind.Windows,
			rootPath,
			root.VolumeName,
			components,
			root.IsAbsolute,
			root.IsDriveRelative,
			root.IsCurrentVolumeRooted
		);
	}

	private static IReadOnlyList<string> SplitComponents(
		ReadOnlySpan<char> content,
		PathPlatformSemantics semantics
	) {
		var components = new List<string>();
		var start = 0;
		for (
			var index = 0;
			index <= content.Length;
			index++
		) {
			if (
				index < content.Length
				&& !semantics.IsDirectorySeparator(
					content[ index ]
				)
			) {
				continue;
			}

			var component = content[
				start..index
			].ToString();
			start = index + 1;
			if ( 0 == component.Length ) {
				continue;
			}
			components.Add(
				component
			);
		}
		return Array.AsReadOnly(
			components.ToArray()
		);
	}
}

/// <summary>
/// Represents the structural decomposition of pathname text without component
/// normalization or filesystem observation.
/// </summary>
public sealed class PathSyntaxParts {
	internal PathSyntaxParts(
		string originalPath,
		PathPlatformKind platformKind,
		string rootPath,
		string volumeName,
		IReadOnlyList<string> components,
		bool isAbsolute,
		bool isDriveRelative,
		bool isCurrentVolumeRooted
	) {
		this.OriginalPath = originalPath;
		this.PlatformKind = platformKind;
		this.RootPath = rootPath;
		this.VolumeName = volumeName;
		this.Components = components;
		this.IsAbsolute = isAbsolute;
		this.IsDriveRelative = isDriveRelative;
		this.IsCurrentVolumeRooted = isCurrentVolumeRooted;
	}

	/// <summary>Gets the pathname text supplied by the caller.</summary>
	public string OriginalPath {
		get;
	}

	/// <summary>Gets the pathname grammar used for decomposition.</summary>
	public PathPlatformKind PlatformKind {
		get;
	}

	/// <summary>
	/// Gets the canonical root spelling, an empty string for an unrooted path,
	/// the drive designator for a drive-relative Windows path, or one separator
	/// for a current-volume-rooted Windows path.
	/// </summary>
	public string RootPath {
		get;
	}

	/// <summary>
	/// Gets the comparison identity of the root volume when one is identified,
	/// or an empty string otherwise.
	/// </summary>
	public string VolumeName {
		get;
	}

	/// <summary>
	/// Gets the ordered nonempty pathname components exactly as parsed after
	/// separator normalization.
	/// </summary>
	public IReadOnlyList<string> Components {
		get;
	}

	/// <summary>Gets whether the pathname names an absolute root.</summary>
	public bool IsAbsolute {
		get;
	}

	/// <summary>Gets whether a Windows pathname uses drive-relative syntax.</summary>
	public bool IsDriveRelative {
		get;
	}

	/// <summary>
	/// Gets whether a Windows pathname is rooted on the current volume without
	/// identifying that volume.
	/// </summary>
	public bool IsCurrentVolumeRooted {
		get;
	}

	/// <summary>Gets whether the pathname contains any form of explicit root syntax.</summary>
	public bool HasRoot {
		get {
			return this.IsAbsolute
				|| this.IsDriveRelative
				|| this.IsCurrentVolumeRooted
			;
		}
	}
}
