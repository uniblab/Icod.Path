namespace Icod.Path;

/// <summary>
/// Supplies one-component, no-follow filesystem observations to the shared canonical-path resolver.
/// Implementations do not recurse, resolve complete link chains, write diagnostics, or choose command status.
/// </summary>
public interface ICanonicalPathFileSystemProvider {
	/// <summary>Gets the pathname grammar implemented by the provider.</summary>
	PathPlatformSemantics Semantics { get; }

	/// <summary>Gets the provider's absolute current directory.</summary>
	string CurrentDirectory { get; }

	/// <summary>Observes one absolute lexical pathname without dereferencing a terminal link.</summary>
	/// <param name="path">The absolute lexical pathname.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The no-follow observation.</returns>
	ValueTask<PathComponentObservation> ObserveAsync(
		string path,
		CancellationToken cancellationToken = default
	);
}
