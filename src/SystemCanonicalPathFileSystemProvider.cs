namespace Icod.Path;

/// <summary>Supplies no-follow canonical-path observations using the current host filesystem.</summary>
public sealed class SystemCanonicalPathFileSystemProvider : ICanonicalPathFileSystemProvider {
	private readonly IPathIndirectionInspector indirectionInspector;

	private SystemCanonicalPathFileSystemProvider() : this( SystemPathIndirectionInspector.Instance ) {
	}

	/// <summary>Initializes a system provider over an injectable physical indirection inspector.</summary>
	/// <param name="indirectionInspector">The no-follow indirection inspector.</param>
	public SystemCanonicalPathFileSystemProvider( IPathIndirectionInspector indirectionInspector ) {
		this.indirectionInspector = indirectionInspector
			?? throw new ArgumentNullException( nameof( indirectionInspector ) );
	}

	/// <summary>Gets the shared system-provider instance.</summary>
	public static SystemCanonicalPathFileSystemProvider Instance { get; } = new();

	/// <inheritdoc/>
	public PathPlatformSemantics Semantics => PathPlatformSemantics.Host;

	/// <inheritdoc/>
	public string CurrentDirectory => Directory.GetCurrentDirectory();

	/// <inheritdoc/>
	public async ValueTask<PathComponentObservation> ObserveAsync(
		string path,
		CancellationToken cancellationToken = default
	) {
		ArgumentException.ThrowIfNullOrEmpty( path );
		cancellationToken.ThrowIfCancellationRequested();
		try {
			var attributes = File.GetAttributes( path );
			var indirection = await indirectionInspector.InspectAsync(
				path,
				cancellationToken
			).ConfigureAwait( false );
			var kind = (attributes & FileAttributes.Device) != 0
				? CanonicalPathEntryKind.Other
				: (attributes & FileAttributes.Directory) != 0
					? CanonicalPathEntryKind.Directory
					: CanonicalPathEntryKind.File;
			return PathComponentObservation.Existing( path, kind, indirection );
		} catch ( FileNotFoundException ) {
			return PathComponentObservation.Missing( path );
		} catch ( DirectoryNotFoundException ) {
			return PathComponentObservation.Missing( path );
		} catch ( UnauthorizedAccessException exception ) {
			return PathComponentObservation.Failed(
				new CanonicalPathFailure(
					CanonicalPathFailureCode.AccessDenied,
					path,
					"access to the pathname was denied",
					exception
				)
			);
		} catch ( IOException exception ) {
			return PathComponentObservation.Failed(
				new CanonicalPathFailure(
					CanonicalPathFailureCode.IoError,
					path,
					"the pathname could not be inspected",
					exception
				)
			);
		} catch ( System.Security.SecurityException exception ) {
			return PathComponentObservation.Failed(
				new CanonicalPathFailure(
					CanonicalPathFailureCode.AccessDenied,
					path,
					"access to the pathname was denied",
					exception
				)
			);
		}
	}
}
