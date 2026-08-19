namespace Icod.Path;

/// <summary>Controls which nonexistent components may appear in a successful physical resolution.</summary>
public enum MissingPathComponentPolicy {
	/// <summary>Every pathname component must exist.</summary>
	RequireExisting,
	/// <summary>Only the final pathname component may be absent.</summary>
	AllowFinalComponent,
	/// <summary>Any suffix after the last existing directory may be absent.</summary>
	AllowMissingSuffix,
}

/// <summary>Identifies a deterministic canonical-path failure.</summary>
public enum CanonicalPathFailureCode {
	/// <summary>No failure occurred.</summary>
	None,
	/// <summary>The supplied pathname was empty.</summary>
	EmptyPath,
	/// <summary>The supplied pathname contains an invalid character or malformed root.</summary>
	InvalidPath,
	/// <summary>A relative pathname could not be combined with an absolute base pathname.</summary>
	InvalidBasePath,
	/// <summary>A drive-relative Windows pathname could not be resolved against the supplied base volume.</summary>
	DriveRelativePath,
	/// <summary>The requested pathname or one of its required components does not exist.</summary>
	NotFound,
	/// <summary>A nonfinal pathname component is not a directory.</summary>
	NotDirectory,
	/// <summary>The pathname cannot be inspected because access was denied.</summary>
	AccessDenied,
	/// <summary>The host reported an input/output failure while inspecting the pathname.</summary>
	IoError,
	/// <summary>A symbolic link or supported reparse-point target was unavailable.</summary>
	LinkTargetUnavailable,
	/// <summary>A reparse point was observed whose target semantics are not supported.</summary>
	UnsupportedReparsePoint,
	/// <summary>The symbolic-link chain revisited an already active link.</summary>
	SymbolicLinkLoop,
	/// <summary>The configured symbolic-link traversal limit was exceeded.</summary>
	TooManySymbolicLinks,
	/// <summary>A relative pathname cannot be computed across different roots or volumes.</summary>
	DifferentRoot,
}

/// <summary>Describes a canonical-path failure without writing a command diagnostic.</summary>
/// <param name="Code">The stable failure code.</param>
/// <param name="Path">The pathname at which the failure occurred.</param>
/// <param name="Message">A deterministic, command-neutral description.</param>
/// <param name="Exception">The host exception, when one was reported.</param>
public sealed record CanonicalPathFailure(
	CanonicalPathFailureCode Code,
	string Path,
	string Message,
	Exception? Exception = null
);

/// <summary>Configures physical pathname resolution.</summary>
public sealed class CanonicalPathResolutionOptions {
	/// <summary>Gets or sets the absolute base directory used for a relative input pathname.</summary>
	/// <value><see langword="null"/> uses the provider's current directory.</value>
	public string? BasePath { get; init; }

	/// <summary>Gets or sets the missing-component policy.</summary>
	public MissingPathComponentPolicy MissingComponentPolicy { get; init; } =
		MissingPathComponentPolicy.RequireExisting
	;

	/// <summary>Gets or sets the maximum number of pathname-indirection objects followed during one resolution.</summary>
	public int MaximumSymbolicLinks { get; init; } = 40;

	/// <summary>Gets or sets whether supported symbolic links, junctions, and mounted volumes are expanded while resolving the pathname.</summary>
	/// <value><see langword="true"/> performs physical resolution; <see langword="false"/> preserves link components while still validating the selected existence policy.</value>
	public bool FollowSymbolicLinks { get; init; } = true;

	/// <summary>Gets or sets whether supported pathname indirection in the final component is dereferenced.</summary>
	/// <remarks>This setting applies only when <see cref="FollowSymbolicLinks"/> is <see langword="true"/>.</remarks>
	public bool FollowFinalSymbolicLink { get; init; } = true;

	/// <summary>Gets or sets whether the resolved final pathname must designate a directory.</summary>
	/// <remarks>This supports the trailing-separator contract of strict canonicalization modes.</remarks>
	public bool RequireFinalDirectory { get; init; }

	/// <summary>Gets or sets whether an unsupported reparse point in the final component is a resolution failure.</summary>
	public bool RejectUnsupportedFinalReparsePoint { get; init; } = true;
}

/// <summary>Records one pathname-indirection object traversed during physical pathname resolution.</summary>
/// <param name="LinkPath">The absolute lexical pathname of the indirection object.</param>
/// <param name="TargetText">The target text stored in the indirection object.</param>
/// <param name="ResolvedTargetPath">The absolute lexical pathname produced from that target.</param>
public sealed record ResolvedPathLink(
	string LinkPath,
	string TargetText,
	string ResolvedTargetPath
);

/// <summary>Represents lexical or physical canonicalization.</summary>
public sealed class CanonicalPathResult {
	private CanonicalPathResult(
		string? path,
		PathRootInfo? root,
		IReadOnlyList<ResolvedPathLink> resolvedLinks,
		int missingComponentCount,
		CanonicalPathFailure? failure
	) {
		this.Path = path;
		this.Root = root;
		this.ResolvedLinks = resolvedLinks;
		this.MissingComponentCount = missingComponentCount;
		this.Failure = failure;
	}

	/// <summary>Gets whether canonicalization succeeded.</summary>
	public bool Succeeded => null == this.Failure;

	/// <summary>Gets the canonical absolute pathname, or <see langword="null"/> after failure.</summary>
	public string? Path { get; }

	/// <summary>Gets the parsed root and volume, or <see langword="null"/> after failure.</summary>
	public PathRootInfo? Root { get; }

	/// <summary>Gets the pathname-indirection objects traversed in encounter order.</summary>
	public IReadOnlyList<ResolvedPathLink> ResolvedLinks { get; }

	/// <summary>Gets the number of unresolved components admitted by the missing-component policy.</summary>
	public int MissingComponentCount { get; }

	/// <summary>Gets the structured failure, or <see langword="null"/> after success.</summary>
	public CanonicalPathFailure? Failure { get; }

	/// <summary>Creates a successful canonicalization result.</summary>
	/// <param name="path">The canonical absolute pathname.</param>
	/// <param name="root">The parsed root and volume.</param>
	/// <param name="resolvedLinks">The traversed pathname-indirection objects.</param>
	/// <param name="missingComponentCount">The admitted missing-component count.</param>
	/// <returns>A successful result.</returns>
	public static CanonicalPathResult Success(
		string path,
		PathRootInfo root,
		IEnumerable<ResolvedPathLink>? resolvedLinks = null,
		int missingComponentCount = 0
	) {
		ArgumentException.ThrowIfNullOrEmpty( path );
		ArgumentNullException.ThrowIfNull( root );
		if ( 0 > missingComponentCount ) {
			throw new ArgumentOutOfRangeException( nameof( missingComponentCount ) );
		}
		var links = ( resolvedLinks ?? Array.Empty<ResolvedPathLink>() ).ToArray();
		return new CanonicalPathResult(
			path,
			root,
			Array.AsReadOnly( links ),
			missingComponentCount,
			null
		);
	}

	/// <summary>Creates a failed canonicalization result.</summary>
	/// <param name="failure">The structured failure.</param>
	/// <returns>A failed result that contains no pathname.</returns>
	public static CanonicalPathResult Failed( CanonicalPathFailure failure ) {
		ArgumentNullException.ThrowIfNull( failure );
		return new CanonicalPathResult(
			null,
			null,
			Array.Empty<ResolvedPathLink>(),
			0,
			failure
		);
	}
}

/// <summary>Represents relative-path calculation between two absolute lexical paths.</summary>
public sealed class RelativePathResult {
	private RelativePathResult(
		string? path,
		CanonicalPathFailure? failure
	) {
		this.Path = path;
		this.Failure = failure;
	}

	/// <summary>Gets whether relative-path calculation succeeded.</summary>
	public bool Succeeded => null == this.Failure;

	/// <summary>Gets the relative pathname, or <see langword="null"/> after failure.</summary>
	public string? Path { get; }

	/// <summary>Gets the structured failure, or <see langword="null"/> after success.</summary>
	public CanonicalPathFailure? Failure { get; }

	/// <summary>Creates a successful relative-path result.</summary>
	/// <param name="path">The calculated relative pathname.</param>
	/// <returns>A successful result.</returns>
	public static RelativePathResult Success( string path ) {
		ArgumentException.ThrowIfNullOrEmpty( path );
		return new RelativePathResult( path, null );
	}

	/// <summary>Creates a failed relative-path result.</summary>
	/// <param name="failure">The structured failure.</param>
	/// <returns>A failed result.</returns>
	public static RelativePathResult Failed( CanonicalPathFailure failure ) {
		ArgumentNullException.ThrowIfNull( failure );
		return new RelativePathResult( null, failure );
	}
}

/// <summary>Represents component-aware pathname containment.</summary>
public sealed class PathContainmentResult {
	private PathContainmentResult(
		bool isContained,
		CanonicalPathFailure? failure
	) {
		this.IsContained = isContained;
		this.Failure = failure;
	}

	/// <summary>Gets whether containment evaluation succeeded.</summary>
	public bool Succeeded => null == this.Failure;

	/// <summary>Gets whether the candidate is equal to or below the proposed root.</summary>
	public bool IsContained { get; }

	/// <summary>Gets the structured failure, or <see langword="null"/> after success.</summary>
	public CanonicalPathFailure? Failure { get; }

	/// <summary>Creates a successful containment result.</summary>
	/// <param name="isContained">Whether the candidate is contained.</param>
	/// <returns>A successful result.</returns>
	public static PathContainmentResult Success( bool isContained ) =>
		new( isContained, null )
	;

	/// <summary>Creates a failed containment result.</summary>
	/// <param name="failure">The structured failure.</param>
	/// <returns>A failed result.</returns>
	public static PathContainmentResult Failed( CanonicalPathFailure failure ) {
		ArgumentNullException.ThrowIfNull( failure );
		return new PathContainmentResult( false, failure );
	}
}

/// <summary>Identifies the effective kind of a non-link pathname object.</summary>
public enum CanonicalPathEntryKind {
	/// <summary>The object kind is not available.</summary>
	Unknown,
	/// <summary>The object is a regular file or file-like object.</summary>
	File,
	/// <summary>The object is a directory.</summary>
	Directory,
	/// <summary>The object exists but is neither a regular file nor a directory.</summary>
	Other,
}

/// <summary>Represents one no-follow pathname observation supplied to the canonical resolver.</summary>
public sealed class PathComponentObservation {
	private PathComponentObservation(
		string path,
		bool observationSucceeded,
		bool exists,
		CanonicalPathEntryKind kind,
		PathIndirectionInfo indirection,
		CanonicalPathFailure? failure
	) {
		Path = path;
		ObservationSucceeded = observationSucceeded;
		Exists = exists;
		Kind = kind;
		Indirection = indirection;
		Failure = failure;
	}

	/// <summary>Gets the pathname that was inspected.</summary>
	public string Path { get; }

	/// <summary>Gets whether the provider completed the observation.</summary>
	public bool ObservationSucceeded { get; }

	/// <summary>Gets whether a pathname object exists without dereferencing a terminal indirection.</summary>
	public bool Exists { get; }

	/// <summary>Gets the physical non-indirection object kind.</summary>
	public CanonicalPathEntryKind Kind { get; }

	/// <summary>Gets the complete physical indirection and reparse-point characterization.</summary>
	public PathIndirectionInfo Indirection { get; }

	/// <summary>Gets whether the object is specifically a POSIX or Windows symbolic link.</summary>
	public bool IsSymbolicLink => Indirection.IsSymbolicLink;

	/// <summary>Gets whether the object participates in supported pathname indirection.</summary>
	public bool IsPathIndirection => Indirection.IsPathIndirection;

	/// <summary>Gets whether the object carries the Windows reparse-point attribute.</summary>
	public bool IsReparsePoint => Indirection.IsReparsePoint;

	/// <summary>Gets the target text preferred for pathname resolution when available.</summary>
	public string? LinkTarget => Indirection.Target;

	/// <summary>Gets a structured provider failure.</summary>
	public CanonicalPathFailure? Failure { get; }

	/// <summary>Creates an observation for an existing pathname object.</summary>
	/// <param name="path">The observed pathname.</param>
	/// <param name="kind">The physical non-indirection object kind.</param>
	/// <param name="indirection">The indirection and reparse-point characterization.</param>
	/// <returns>An existing-object observation.</returns>
	public static PathComponentObservation Existing(
		string path,
		CanonicalPathEntryKind kind,
		PathIndirectionInfo indirection
	) {
		ArgumentNullException.ThrowIfNull( indirection );
		return new PathComponentObservation( path, true, true, kind, indirection, null );
	}

	/// <summary>Creates a source-compatible existing-object observation.</summary>
	/// <param name="path">The observed pathname.</param>
	/// <param name="kind">The physical non-link object kind.</param>
	/// <param name="isSymbolicLink">Whether the object is a supported symbolic link.</param>
	/// <param name="linkTarget">The target text.</param>
	/// <param name="isReparsePoint">Whether the object has the Windows reparse-point attribute.</param>
	/// <returns>An existing-object observation.</returns>
	public static PathComponentObservation Existing(
		string path,
		CanonicalPathEntryKind kind,
		bool isSymbolicLink = false,
		string? linkTarget = null,
		bool isReparsePoint = false
	) => Existing(
		path,
		kind,
		CreateLegacyIndirection( isSymbolicLink, isReparsePoint, linkTarget )
	);

	/// <summary>Creates an observation for a missing pathname object.</summary>
	/// <param name="path">The observed pathname.</param>
	/// <returns>A missing-object observation.</returns>
	public static PathComponentObservation Missing( string path ) => new(
		path,
		true,
		false,
		CanonicalPathEntryKind.Unknown,
		PathIndirectionInfo.None,
		null
	);

	/// <summary>Creates an observation that failed before existence could be determined.</summary>
	/// <param name="failure">The structured failure.</param>
	/// <returns>A failed observation.</returns>
	public static PathComponentObservation Failed( CanonicalPathFailure failure ) {
		ArgumentNullException.ThrowIfNull( failure );
		return new PathComponentObservation(
			failure.Path,
			false,
			false,
			CanonicalPathEntryKind.Unknown,
			PathIndirectionInfo.None,
			failure
		);
	}

	private static PathIndirectionInfo CreateLegacyIndirection(
		bool isSymbolicLink,
		bool isReparsePoint,
		string? linkTarget
	) {
		if ( isReparsePoint ) {
			return PathIndirectionInfo.WindowsReparsePoint(
				isSymbolicLink ? WindowsReparseTags.SymbolicLink : 0,
				false,
				linkTarget,
				linkTarget,
				!string.IsNullOrEmpty( linkTarget ) && !System.IO.Path.IsPathRooted( linkTarget )
			);
		}
		return isSymbolicLink
			? PathIndirectionInfo.PosixSymbolicLink( linkTarget )
			: PathIndirectionInfo.None;
	}
}

/// <summary>Represents no-follow symbolic-link and reparse-point inspection.</summary>
public sealed class PathLinkInspectionResult {
	private PathLinkInspectionResult(
		string? path,
		CanonicalPathEntryKind kind,
		PathIndirectionInfo indirection,
		CanonicalPathFailure? failure
	) {
		Path = path;
		Kind = kind;
		Indirection = indirection;
		Failure = failure;
	}

	/// <summary>Gets whether inspection succeeded.</summary>
	public bool Succeeded => null == Failure;

	/// <summary>Gets the absolute lexical pathname inspected.</summary>
	public string? Path { get; }

	/// <summary>Gets the observed physical object kind.</summary>
	public CanonicalPathEntryKind Kind { get; }

	/// <summary>Gets the complete physical indirection and reparse-point characterization.</summary>
	public PathIndirectionInfo Indirection { get; }

	/// <summary>Gets whether the object is specifically a POSIX or Windows symbolic link.</summary>
	public bool IsSymbolicLink => Indirection.IsSymbolicLink;

	/// <summary>Gets whether the object participates in pathname indirection.</summary>
	public bool IsPathIndirection => Indirection.IsPathIndirection;

	/// <summary>Gets whether the object carries the Windows reparse-point attribute.</summary>
	public bool IsReparsePoint => Indirection.IsReparsePoint;

	/// <summary>Gets the target text preferred for pathname resolution.</summary>
	public string? Target => Indirection.Target;

	/// <summary>Gets the structured failure, or <see langword="null"/> after success.</summary>
	public CanonicalPathFailure? Failure { get; }

	/// <summary>Creates a successful link inspection.</summary>
	/// <param name="path">The inspected absolute lexical pathname.</param>
	/// <param name="kind">The observed physical object kind.</param>
	/// <param name="indirection">The indirection and reparse-point characterization.</param>
	/// <returns>A successful inspection.</returns>
	public static PathLinkInspectionResult Success(
		string path,
		CanonicalPathEntryKind kind,
		PathIndirectionInfo indirection
	) {
		ArgumentNullException.ThrowIfNull( indirection );
		return new PathLinkInspectionResult( path, kind, indirection, null );
	}

	/// <summary>Creates a source-compatible successful link inspection.</summary>
	/// <param name="path">The inspected absolute lexical pathname.</param>
	/// <param name="kind">The observed physical object kind.</param>
	/// <param name="isSymbolicLink">Whether the object is a supported symbolic link.</param>
	/// <param name="isReparsePoint">Whether the object carries the Windows reparse-point attribute.</param>
	/// <param name="target">The target text.</param>
	/// <returns>A successful inspection.</returns>
	public static PathLinkInspectionResult Success(
		string path,
		CanonicalPathEntryKind kind,
		bool isSymbolicLink,
		bool isReparsePoint,
		string? target
	) => Success(
		path,
		kind,
		isReparsePoint
			? PathIndirectionInfo.WindowsReparsePoint(
				isSymbolicLink ? WindowsReparseTags.SymbolicLink : 0,
				false,
				target,
				target,
				!string.IsNullOrEmpty( target ) && !System.IO.Path.IsPathRooted( target )
			)
			: isSymbolicLink
				? PathIndirectionInfo.PosixSymbolicLink( target )
				: PathIndirectionInfo.None
	);

	/// <summary>Creates a failed link inspection.</summary>
	/// <param name="failure">The structured failure.</param>
	/// <returns>A failed inspection.</returns>
	public static PathLinkInspectionResult Failed( CanonicalPathFailure failure ) {
		ArgumentNullException.ThrowIfNull( failure );
		return new PathLinkInspectionResult(
			null,
			CanonicalPathEntryKind.Unknown,
			PathIndirectionInfo.None,
			failure
		);
	}
}
