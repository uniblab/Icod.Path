namespace Icod.Path;

/// <summary>Performs platform-explicit lexical pathname normalization without observing the filesystem.</summary>
public static class PathLexicalNormalizer {
	/// <summary>Normalizes a pathname to an absolute lexical form.</summary>
	/// <param name="path">The input pathname.</param>
	/// <param name="basePath">The absolute base directory used for relative input.</param>
	/// <param name="semantics">The pathname grammar.</param>
	/// <returns>A canonical-path result containing no followed links.</returns>
	public static CanonicalPathResult Normalize(
		string path,
		string basePath,
		PathPlatformSemantics semantics
	) {
		ArgumentNullException.ThrowIfNull( path );
		ArgumentNullException.ThrowIfNull( basePath );
		ArgumentNullException.ThrowIfNull( semantics );
		var outcome = NormalizeToParts( path, basePath, semantics );
		if ( !outcome.Succeeded ) {
			return CanonicalPathResult.Failed( outcome.Failure! );
		}
		var parts = outcome.Parts!;
		return CanonicalPathResult.Success(
			parts.Path,
			new PathRootInfo( parts.RootPath, parts.VolumeName )
		);
	}

	/// <summary>Normalizes a pathname and returns the parsed absolute root and components for shared consumers.</summary>
	/// <param name="path">The input pathname.</param>
	/// <param name="basePath">The absolute base directory used for relative input.</param>
	/// <param name="semantics">The pathname grammar.</param>
	/// <returns>The normalized parsed path or a structured failure.</returns>
	internal static LexicalNormalizationOutcome NormalizeToParts(
		string path,
		string basePath,
		PathPlatformSemantics semantics
	) {
		if ( 0 == path.Length ) {
			return LexicalNormalizationOutcome.Failed(
				new CanonicalPathFailure(
					CanonicalPathFailureCode.EmptyPath,
					path,
					"pathname is empty"
				)
			);
		}
		if ( 0 <= path.IndexOf( '\0' ) ) {
			return Invalid( path, "pathname contains a NUL character" );
		}
		if ( 0 == basePath.Length || 0 <= basePath.IndexOf( '\0' ) ) {
			return LexicalNormalizationOutcome.Failed(
				new CanonicalPathFailure(
					CanonicalPathFailureCode.InvalidBasePath,
					basePath,
					"base pathname is not a valid absolute path"
				)
			);
		}
		return PathPlatformKind.Windows == semantics.Kind
			? NormalizeWindows( path, basePath, semantics )
			: NormalizePosix( path, basePath, semantics )
		;
	}

	private static LexicalNormalizationOutcome NormalizePosix(
		string path,
		string basePath,
		PathPlatformSemantics semantics
	) {
		if ( !basePath.StartsWith( "/", StringComparison.Ordinal ) ) {
			return InvalidBase( basePath );
		}
		var combined = path.StartsWith( "/", StringComparison.Ordinal )
			? path
			: string.Concat(
				basePath,
				basePath.EndsWith( "/", StringComparison.Ordinal ) ? string.Empty : "/",
				path
			)
		;
		var components = NormalizeComponents(
			combined.AsSpan( 1 ),
			semantics,
			combined
		);
		if ( null == components ) {
			return Invalid( combined, "pathname contains an invalid component" );
		}
		var normalized = 0 == components.Count
			? "/"
			: string.Concat( "/", string.Join( '/', components ) )
		;
		return LexicalNormalizationOutcome.Success(
			new LexicalPathParts(
				normalized,
				"/",
				"/",
				components
			)
		);
	}

	private static LexicalNormalizationOutcome NormalizeWindows(
		string path,
		string basePath,
		PathPlatformSemantics semantics
	) {
		var canonicalBaseText = ReplaceSeparators( basePath, semantics );
		var baseRoot = ParseWindowsRoot( canonicalBaseText );
		if ( baseRoot.IsInvalid || !baseRoot.IsAbsolute ) {
			return InvalidBase( basePath );
		}
		var baseComponents = NormalizeComponents(
			canonicalBaseText.AsSpan( baseRoot.ContentStart ),
			semantics,
			basePath
		);
		if ( null == baseComponents ) {
			return InvalidBase( basePath );
		}
		var canonicalBase = Compose(
			baseRoot.RootPath,
			baseComponents,
			semantics.DirectorySeparator
		);
		var canonicalInput = ReplaceSeparators( path, semantics );
		var inputRoot = ParseWindowsRoot( canonicalInput );
		if ( inputRoot.IsInvalid ) {
			return Invalid( path, "pathname contains a malformed Windows root" );
		}
		string combined;
		if ( inputRoot.IsAbsolute ) {
			combined = canonicalInput;
		} else if ( inputRoot.IsCurrentVolumeRooted ) {
			combined = string.Concat(
				baseRoot.RootPath,
				canonicalInput[inputRoot.ContentStart..]
			);
		} else if ( inputRoot.IsDriveRelative ) {
			if ( !string.Equals(
				inputRoot.VolumeName,
				baseRoot.VolumeName,
				semantics.PathComparison
			) ) {
				return LexicalNormalizationOutcome.Failed(
					new CanonicalPathFailure(
						CanonicalPathFailureCode.DriveRelativePath,
						path,
						"drive-relative pathname does not match the base volume"
					)
				);
			}
			combined = Join(
				canonicalBase,
				canonicalInput[inputRoot.ContentStart..],
				semantics.DirectorySeparator
			);
		} else {
			combined = Join(
				canonicalBase,
				canonicalInput,
				semantics.DirectorySeparator
			);
		}
		var root = ParseWindowsRoot( combined );
		if ( root.IsInvalid || !root.IsAbsolute ) {
			return Invalid( path, "pathname does not have an absolute Windows root" );
		}
		var components = NormalizeComponents(
			combined.AsSpan( root.ContentStart ),
			semantics,
			path
		);
		if ( null == components ) {
			return Invalid( path, "pathname contains an invalid Windows component" );
		}
		return LexicalNormalizationOutcome.Success(
			new LexicalPathParts(
				Compose(
					root.RootPath,
					components,
					semantics.DirectorySeparator
				),
				root.RootPath,
				root.VolumeName,
				components
			)
		);
	}


	/// <summary>Prepares an absolute component sequence for ordered physical resolution without collapsing dot components.</summary>
	/// <param name="path">The input pathname.</param>
	/// <param name="basePath">The absolute base directory used for relative input.</param>
	/// <param name="semantics">The pathname grammar.</param>
	/// <returns>The parsed absolute root and ordered component sequence or a structured failure.</returns>
	internal static PhysicalPathPreparationOutcome PrepareForPhysicalResolution(
		string path,
		string basePath,
		PathPlatformSemantics semantics
	) {
		if ( 0 == path.Length ) {
			return PhysicalPathPreparationOutcome.Failed(
				new CanonicalPathFailure(
					CanonicalPathFailureCode.EmptyPath,
					path,
					"pathname is empty"
				)
			);
		}
		if ( 0 <= path.IndexOf( '\0' ) ) {
			return PhysicalPathPreparationOutcome.Failed(
				new CanonicalPathFailure(
					CanonicalPathFailureCode.InvalidPath,
					path,
					"pathname contains a NUL character"
				)
			);
		}
		if ( 0 == basePath.Length || 0 <= basePath.IndexOf( '\0' ) ) {
			return PhysicalPathPreparationOutcome.Failed(
				new CanonicalPathFailure(
					CanonicalPathFailureCode.InvalidBasePath,
					basePath,
					"base pathname is not a valid absolute path"
				)
			);
		}
		return PathPlatformKind.Windows == semantics.Kind
			? PrepareWindowsPhysical( path, basePath, semantics )
			: PreparePosixPhysical( path, basePath, semantics )
		;
	}

	private static PhysicalPathPreparationOutcome PreparePosixPhysical(
		string path,
		string basePath,
		PathPlatformSemantics semantics
	) {
		if ( !basePath.StartsWith( "/", StringComparison.Ordinal ) ) {
			return PhysicalPathPreparationOutcome.Failed(
				new CanonicalPathFailure(
					CanonicalPathFailureCode.InvalidBasePath,
					basePath,
					"base pathname is not a valid absolute path"
				)
			);
		}
		var usesBaseComponents = !path.StartsWith( "/", StringComparison.Ordinal );
		var components = new List<string>();
		if ( usesBaseComponents ) {
			components.AddRange(
				SplitPhysicalComponents(
					basePath.AsSpan( 1 ),
					semantics,
					basePath
				)
				?? components
			);
		}
		var inputComponents = SplitPhysicalComponents(
			path.AsSpan( usesBaseComponents ? 0 : 1 ),
			semantics,
			path
		);
		if ( null == inputComponents ) {
			return PhysicalPathPreparationOutcome.Failed(
				new CanonicalPathFailure(
					CanonicalPathFailureCode.InvalidPath,
					path,
					"pathname contains an invalid component"
				)
			);
		}
		components.AddRange( inputComponents );
		return PhysicalPathPreparationOutcome.Success(
			new PhysicalPathParts(
				"/",
				"/",
				components,
				usesBaseComponents
			)
		);
	}

	private static PhysicalPathPreparationOutcome PrepareWindowsPhysical(
		string path,
		string basePath,
		PathPlatformSemantics semantics
	) {
		var canonicalBaseText = ReplaceSeparators( basePath, semantics );
		var baseRoot = ParseWindowsRoot( canonicalBaseText );
		if ( baseRoot.IsInvalid || !baseRoot.IsAbsolute ) {
			return PhysicalPathPreparationOutcome.Failed(
				new CanonicalPathFailure(
					CanonicalPathFailureCode.InvalidBasePath,
					basePath,
					"base pathname is not a valid absolute path"
				)
			);
		}
		var baseComponents = SplitPhysicalComponents(
			canonicalBaseText.AsSpan( baseRoot.ContentStart ),
			semantics,
			basePath
		);
		if ( null == baseComponents ) {
			return PhysicalPathPreparationOutcome.Failed(
				new CanonicalPathFailure(
					CanonicalPathFailureCode.InvalidBasePath,
					basePath,
					"base pathname is not a valid absolute path"
				)
			);
		}
		var canonicalInput = ReplaceSeparators( path, semantics );
		var inputRoot = ParseWindowsRoot( canonicalInput );
		if ( inputRoot.IsInvalid ) {
			return PhysicalPathPreparationOutcome.Failed(
				new CanonicalPathFailure(
					CanonicalPathFailureCode.InvalidPath,
					path,
					"pathname contains a malformed Windows root"
				)
			);
		}
		var components = new List<string>();
		string rootPath;
		string volumeName;
		int contentStart;
		bool usesBaseComponents;
		if ( inputRoot.IsAbsolute ) {
			rootPath = inputRoot.RootPath;
			volumeName = inputRoot.VolumeName;
			contentStart = inputRoot.ContentStart;
			usesBaseComponents = false;
		} else if ( inputRoot.IsCurrentVolumeRooted ) {
			rootPath = baseRoot.RootPath;
			volumeName = baseRoot.VolumeName;
			contentStart = inputRoot.ContentStart;
			usesBaseComponents = false;
		} else if ( inputRoot.IsDriveRelative ) {
			if ( !string.Equals(
				inputRoot.VolumeName,
				baseRoot.VolumeName,
				semantics.PathComparison
			) ) {
				return PhysicalPathPreparationOutcome.Failed(
					new CanonicalPathFailure(
						CanonicalPathFailureCode.DriveRelativePath,
						path,
						"drive-relative pathname does not match the base volume"
					)
				);
			}
			rootPath = baseRoot.RootPath;
			volumeName = baseRoot.VolumeName;
			contentStart = inputRoot.ContentStart;
			usesBaseComponents = true;
			components.AddRange( baseComponents );
		} else {
			rootPath = baseRoot.RootPath;
			volumeName = baseRoot.VolumeName;
			contentStart = 0;
			usesBaseComponents = true;
			components.AddRange( baseComponents );
		}
		var inputComponents = SplitPhysicalComponents(
			canonicalInput.AsSpan( contentStart ),
			semantics,
			path
		);
		if ( null == inputComponents ) {
			return PhysicalPathPreparationOutcome.Failed(
				new CanonicalPathFailure(
					CanonicalPathFailureCode.InvalidPath,
					path,
					"pathname contains an invalid Windows component"
				)
			);
		}
		components.AddRange( inputComponents );
		return PhysicalPathPreparationOutcome.Success(
			new PhysicalPathParts(
				rootPath,
				volumeName,
				components,
				usesBaseComponents
			)
		);
	}

	private static List<string>? SplitPhysicalComponents(
		ReadOnlySpan<char> content,
		PathPlatformSemantics semantics,
		string originalPath
	) {
		var components = new List<string>();
		var start = 0;
		for ( var index = 0; index <= content.Length; index++ ) {
			if (
				index < content.Length
				&& !semantics.IsDirectorySeparator( content[index] )
			) {
				continue;
			}
			var component = content[start..index].ToString();
			start = index + 1;
			if ( 0 == component.Length ) {
				continue;
			}
			if (
				PathPlatformKind.Windows == semantics.Kind
				&& "." != component
				&& ".." != component
				&& !IsValidWindowsComponent( component )
			) {
				_ = originalPath;
				return null;
			}
			components.Add( component );
		}
		return components;
	}

	private static List<string>? NormalizeComponents(
		ReadOnlySpan<char> content,
		PathPlatformSemantics semantics,
		string originalPath
	) {
		var components = new List<string>();
		var start = 0;
		for ( var index = 0; index <= content.Length; index++ ) {
			if (
				index < content.Length
				&& !semantics.IsDirectorySeparator( content[index] )
			) {
				continue;
			}
			var component = content[start..index].ToString();
			start = index + 1;
			if ( 0 == component.Length || "." == component ) {
				continue;
			}
			if ( ".." == component ) {
				if ( 0 < components.Count ) {
					components.RemoveAt( components.Count - 1 );
				}
				continue;
			}
			if (
				PathPlatformKind.Windows == semantics.Kind
				&& !IsValidWindowsComponent( component )
			) {
				_ = originalPath;
				return null;
			}
			components.Add( component );
		}
		return components;
	}

	private static bool IsValidWindowsComponent( string component ) {
		foreach ( var value in component ) {
			if (
				32 > value
				|| '"' == value
				|| '<' == value
				|| '>' == value
				|| '|' == value
				|| ':' == value
				|| '*' == value
				|| '?' == value
			) {
				return false;
			}
		}
		return true;
	}

	private static WindowsRoot ParseWindowsRoot( string path ) {
		if (
			4 <= path.Length
			&& IsSeparator( path[0] )
			&& IsSeparator( path[1] )
			&& '?' == path[2]
			&& IsSeparator( path[3] )
		) {
			if (
				8 <= path.Length
				&& path.AsSpan( 4, 3 ).Equals(
					"UNC".AsSpan(),
					StringComparison.OrdinalIgnoreCase
				)
				&& IsSeparator( path[7] )
			) {
				return ParseUnc( path, 8, "\\\\?\\UNC\\" );
			}
			if (
				7 <= path.Length
				&& char.IsAsciiLetter( path[4] )
				&& ':' == path[5]
				&& IsSeparator( path[6] )
			) {
				var drive = string.Concat(
					char.ToUpperInvariant( path[4] ),
					":"
				);
				return new WindowsRoot(
					string.Concat( "\\\\?\\", drive, "\\" ),
					drive,
					7,
					true,
					false,
					false
				);
			}
			return WindowsRoot.Invalid;
		}
		if (
			2 <= path.Length
			&& IsSeparator( path[0] )
			&& IsSeparator( path[1] )
		) {
			return ParseUnc( path, 2, "\\\\" );
		}
		if (
			2 <= path.Length
			&& char.IsAsciiLetter( path[0] )
			&& ':' == path[1]
		) {
			var drive = string.Concat(
				char.ToUpperInvariant( path[0] ),
				":"
			);
			if ( 3 <= path.Length && IsSeparator( path[2] ) ) {
				return new WindowsRoot(
					string.Concat( drive, "\\" ),
					drive,
					3,
					true,
					false,
					false
				);
			}
			return new WindowsRoot(
				string.Empty,
				drive,
				2,
				false,
				true,
				false
			);
		}
		if ( 0 < path.Length && IsSeparator( path[0] ) ) {
			return new WindowsRoot(
				string.Empty,
				string.Empty,
				1,
				false,
				false,
				true
			);
		}
		return new WindowsRoot(
			string.Empty,
			string.Empty,
			0,
			false,
			false,
			false
		);
	}

	private static WindowsRoot ParseUnc(
		string path,
		int start,
		string prefix
	) {
		if ( start >= path.Length ) {
			return WindowsRoot.Invalid;
		}
		var serverEnd = IndexOfSeparator( path, start );
		if ( start == serverEnd || 0 > serverEnd ) {
			return WindowsRoot.Invalid;
		}
		var shareStart = serverEnd + 1;
		if ( shareStart >= path.Length ) {
			return WindowsRoot.Invalid;
		}
		var shareEnd = IndexOfSeparator( path, shareStart );
		if ( 0 > shareEnd ) {
			shareEnd = path.Length;
		}
		if ( shareStart == shareEnd ) {
			return WindowsRoot.Invalid;
		}
		var server = path[start..serverEnd];
		var share = path[shareStart..shareEnd];
		if (
			"." == server
			|| ".." == server
			|| "." == share
			|| ".." == share
			|| !IsValidWindowsComponent( server )
			|| !IsValidWindowsComponent( share )
		) {
			return WindowsRoot.Invalid;
		}
		var volume = string.Concat( "\\\\", server, "\\", share );
		return new WindowsRoot(
			string.Concat( prefix, server, "\\", share, "\\" ),
			volume,
			shareEnd < path.Length ? shareEnd + 1 : shareEnd,
			true,
			false,
			false
		);
	}

	private static int IndexOfSeparator( string path, int start ) {
		for ( var index = start; index < path.Length; index++ ) {
			if ( IsSeparator( path[index] ) ) {
				return index;
			}
		}
		return -1;
	}

	private static bool IsSeparator( char value ) => '\\' == value || '/' == value;

	private static string ReplaceSeparators(
		string path,
		PathPlatformSemantics semantics
	) => null == semantics.AlternateDirectorySeparator
		? path
		: path.Replace(
			semantics.AlternateDirectorySeparator.Value,
			semantics.DirectorySeparator
		)
	;

	private static string Compose(
		string root,
		IReadOnlyList<string> components,
		char separator
	) => 0 == components.Count
		? root
		: string.Concat(
			root,
			string.Join( separator, components )
		)
	;

	private static string Join(
		string left,
		string right,
		char separator
	) {
		if ( 0 == right.Length ) {
			return left;
		}
		if ( left.EndsWith( separator ) ) {
			return string.Concat( left, right );
		}
		return string.Concat( left, separator, right );
	}

	private static LexicalNormalizationOutcome Invalid(
		string path,
		string message
	) => LexicalNormalizationOutcome.Failed(
		new CanonicalPathFailure(
			CanonicalPathFailureCode.InvalidPath,
			path,
			message
		)
	);

	private static LexicalNormalizationOutcome InvalidBase( string path ) =>
		LexicalNormalizationOutcome.Failed(
			new CanonicalPathFailure(
				CanonicalPathFailureCode.InvalidBasePath,
				path,
				"base pathname is not a valid absolute path"
			)
		)
	;

	private readonly record struct WindowsRoot(
		string RootPath,
		string VolumeName,
		int ContentStart,
		bool IsAbsolute,
		bool IsDriveRelative,
		bool IsCurrentVolumeRooted
	) {
		/// <summary>Gets whether root parsing failed.</summary>
		public bool IsInvalid => 0 > this.ContentStart;

		/// <summary>Gets the malformed-root sentinel.</summary>
		public static WindowsRoot Invalid => new(
			string.Empty,
			string.Empty,
			-1,
			false,
			false,
			false
		);
	}
}

/// <summary>Contains an absolute root and an ordered component stream for physical resolution.</summary>
/// <param name="RootPath">The canonical root spelling.</param>
/// <param name="VolumeName">The comparison identity of the root volume.</param>
/// <param name="Components">The ordered components, including dot components.</param>
/// <param name="UsesBaseComponents">Whether the component stream begins with the supplied base directory.</param>
internal sealed record PhysicalPathParts(
	string RootPath,
	string VolumeName,
	IReadOnlyList<string> Components,
	bool UsesBaseComponents
);

/// <summary>Represents internal preparation for ordered physical resolution.</summary>
internal sealed class PhysicalPathPreparationOutcome {
	private PhysicalPathPreparationOutcome(
		PhysicalPathParts? parts,
		CanonicalPathFailure? failure
	) {
		this.Parts = parts;
		this.Failure = failure;
	}

	/// <summary>Gets whether preparation succeeded.</summary>
	internal bool Succeeded => null == this.Failure;

	/// <summary>Gets the prepared path parts.</summary>
	internal PhysicalPathParts? Parts { get; }

	/// <summary>Gets the structured failure.</summary>
	internal CanonicalPathFailure? Failure { get; }

	/// <summary>Creates a successful preparation outcome.</summary>
	/// <param name="parts">The prepared parts.</param>
	/// <returns>A successful outcome.</returns>
	internal static PhysicalPathPreparationOutcome Success( PhysicalPathParts parts ) {
		ArgumentNullException.ThrowIfNull( parts );
		return new PhysicalPathPreparationOutcome( parts, null );
	}

	/// <summary>Creates a failed preparation outcome.</summary>
	/// <param name="failure">The structured failure.</param>
	/// <returns>A failed outcome.</returns>
	internal static PhysicalPathPreparationOutcome Failed( CanonicalPathFailure failure ) {
		ArgumentNullException.ThrowIfNull( failure );
		return new PhysicalPathPreparationOutcome( null, failure );
	}
}

/// <summary>Contains an absolute normalized pathname split into its root and components.</summary>
/// <param name="Path">The complete absolute lexical pathname.</param>
/// <param name="RootPath">The canonical root spelling.</param>
/// <param name="VolumeName">The comparison identity of the root volume.</param>
/// <param name="Components">The normalized components below the root.</param>
internal sealed record LexicalPathParts(
	string Path,
	string RootPath,
	string VolumeName,
	IReadOnlyList<string> Components
);

/// <summary>Represents an internal lexical normalization operation.</summary>
internal sealed class LexicalNormalizationOutcome {
	private LexicalNormalizationOutcome(
		LexicalPathParts? parts,
		CanonicalPathFailure? failure
	) {
		this.Parts = parts;
		this.Failure = failure;
	}

	/// <summary>Gets whether normalization succeeded.</summary>
	internal bool Succeeded => null == this.Failure;

	/// <summary>Gets the parsed path after success.</summary>
	internal LexicalPathParts? Parts { get; }

	/// <summary>Gets the structured failure.</summary>
	internal CanonicalPathFailure? Failure { get; }

	/// <summary>Creates a successful outcome.</summary>
	/// <param name="parts">The normalized path parts.</param>
	/// <returns>A successful outcome.</returns>
	internal static LexicalNormalizationOutcome Success( LexicalPathParts parts ) {
		ArgumentNullException.ThrowIfNull( parts );
		return new LexicalNormalizationOutcome( parts, null );
	}

	/// <summary>Creates a failed outcome.</summary>
	/// <param name="failure">The structured failure.</param>
	/// <returns>A failed outcome.</returns>
	internal static LexicalNormalizationOutcome Failed( CanonicalPathFailure failure ) {
		ArgumentNullException.ThrowIfNull( failure );
		return new LexicalNormalizationOutcome( null, failure );
	}
}
